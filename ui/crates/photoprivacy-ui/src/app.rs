//! 主窗口与配置页。
//!
//! 配置页只做三件事：编辑 `AppConfig` 草稿、把草稿交给 `config_service` 防抖保存、
//! 把服务回报的状态显示出来。文件 IO 与 IPC 都不在 UI 线程上（见 `config_service`）。

use crate::config::{
    AppConfig, LOCALES, LOG_LEVELS, normalize_theme_variant, resolve_auto_excluded_subdirectories,
};
use crate::config_service::{ConfigCommand, ConfigEvent};
use crate::theme::{self, Theme, ThemeMode};
use crate::worker_connection::UiEvent;
use gpui::prelude::*;
use gpui::{
    AnyElement, Context, CursorStyle, FocusHandle, FontWeight, IntoElement, KeyDownEvent,
    MouseButton, PathPromptOptions, Render, Rgba, Subscription, Window, div, px,
};
use photoprivacy_ipc::{ConnectionSnapshot, ConnectionState};
use std::collections::HashMap;
use std::path::PathBuf;
use std::time::Duration;
use tokio::sync::{mpsc, watch};

const CONTROL_WIDTH: f32 = 430.0;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
enum FieldId {
    ExifToolPath,
    HotFolder,
    BackupDirectory,
    QuarantineDirectory,
    AuditLogDirectory,
}

impl FieldId {
    const ALL: [FieldId; 5] = [
        FieldId::ExifToolPath,
        FieldId::HotFolder,
        FieldId::BackupDirectory,
        FieldId::QuarantineDirectory,
        FieldId::AuditLogDirectory,
    ];

    fn element_id(self) -> &'static str {
        match self {
            FieldId::ExifToolPath => "config-exiftool-path",
            FieldId::HotFolder => "config-hot-folder",
            FieldId::BackupDirectory => "config-backup-directory",
            FieldId::QuarantineDirectory => "config-quarantine-directory",
            FieldId::AuditLogDirectory => "config-audit-log-directory",
        }
    }

    fn browse_id(self) -> &'static str {
        match self {
            FieldId::ExifToolPath => "browse-exiftool-path",
            FieldId::HotFolder => "browse-hot-folder",
            FieldId::BackupDirectory => "browse-backup-directory",
            FieldId::QuarantineDirectory => "browse-quarantine-directory",
            FieldId::AuditLogDirectory => "browse-audit-log-directory",
        }
    }

    fn placeholder(self) -> &'static str {
        match self {
            FieldId::ExifToolPath => r"C:\Program Files\ExifTool\exiftool.exe",
            FieldId::HotFolder => "Folder the worker watches",
            FieldId::BackupDirectory => "Default: <hot folder>/bak",
            FieldId::QuarantineDirectory => "Default: <hot folder>/_quarantine",
            FieldId::AuditLogDirectory => "Default: <hot folder>/_audit",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
enum SaveState {
    Idle,
    Pending,
    Saved,
    SavedWorkerDown(String),
    Failed(String),
    ExternalLoadFailed(String),
}

struct FieldState {
    focus: FocusHandle,
    cursor: usize,
}

pub struct AppView {
    snapshot: ConnectionSnapshot,
    snapshots: watch::Receiver<ConnectionSnapshot>,
    events: mpsc::UnboundedReceiver<UiEvent>,
    theme_mode: ThemeMode,
    _appearance_subscription: Subscription,
    config: AppConfig,
    config_events: mpsc::UnboundedReceiver<ConfigEvent>,
    config_commands: mpsc::UnboundedSender<ConfigCommand>,
    save_state: SaveState,
    fields: HashMap<FieldId, FieldState>,
    config_path: PathBuf,
}

impl AppView {
    pub fn new(
        window: &mut Window,
        cx: &mut Context<Self>,
        snapshots: watch::Receiver<ConnectionSnapshot>,
        events: mpsc::UnboundedReceiver<UiEvent>,
        config_events: mpsc::UnboundedReceiver<ConfigEvent>,
        config_commands: mpsc::UnboundedSender<ConfigCommand>,
        config_path: PathBuf,
    ) -> Self {
        let appearance_subscription = cx.observe_window_appearance(window, |_, _, cx| cx.notify());
        let snapshot = snapshots.borrow().clone();
        let config = AppConfig::default();
        let mut fields = HashMap::new();
        for id in FieldId::ALL {
            fields.insert(
                id,
                FieldState {
                    focus: cx.focus_handle(),
                    cursor: field_value(&config, id).len(),
                },
            );
        }

        let view = Self {
            snapshot,
            snapshots,
            events,
            theme_mode: ThemeMode::from_variant(&config.ui.theme_variant),
            _appearance_subscription: appearance_subscription,
            config,
            config_events,
            config_commands,
            save_state: SaveState::Idle,
            fields,
            config_path,
        };
        view.spawn_updates(window, cx);
        view
    }

    /// 后台快照、唤起事件与配置事件都从 tokio 线程来；这里在 UI 线程轮询，
    /// 避免跨线程触碰 gpui 状态。
    fn spawn_updates(&self, window: &Window, cx: &mut Context<Self>) {
        cx.spawn_in(window, async move |this, cx| {
            loop {
                cx.background_executor()
                    .timer(Duration::from_millis(200))
                    .await;

                let show_window = this
                    .update(cx, |this, cx| {
                        let mut show_window = false;
                        let mut changed = false;
                        if this.snapshots.has_changed().unwrap_or(false) {
                            this.snapshot = this.snapshots.borrow_and_update().clone();
                            changed = true;
                        }
                        while let Ok(event) = this.events.try_recv() {
                            if matches!(event, UiEvent::ShowWindow) {
                                show_window = true;
                            }
                        }
                        while let Ok(event) = this.config_events.try_recv() {
                            this.apply_config_event(event);
                            changed = true;
                        }
                        if changed {
                            cx.notify();
                        }
                        show_window
                    })
                    .unwrap_or(false);

                if show_window {
                    let _ = cx.update(|window, _| window.activate_window());
                }
            }
        })
        .detach();
    }

    fn apply_config_event(&mut self, event: ConfigEvent) {
        match event {
            ConfigEvent::Loaded(config) | ConfigEvent::ExternalReload(config) => {
                self.set_config(config);
                self.save_state = SaveState::Idle;
            }
            ConfigEvent::ExternalLoadFailed(error) => {
                self.save_state = SaveState::ExternalLoadFailed(error);
            }
            ConfigEvent::Saved {
                worker_reloaded,
                error,
            } => {
                self.save_state = if worker_reloaded {
                    SaveState::Saved
                } else {
                    SaveState::SavedWorkerDown(error.unwrap_or_default())
                };
            }
            ConfigEvent::SaveFailed(error) => {
                self.save_state = SaveState::Failed(error);
            }
        }
    }

    fn set_config(&mut self, mut config: AppConfig) {
        config.ui.theme_variant = normalize_theme_variant(&config.ui.theme_variant).to_string();
        self.theme_mode = ThemeMode::from_variant(&config.ui.theme_variant);
        self.config = config;

        let lengths: Vec<(FieldId, usize)> = FieldId::ALL
            .iter()
            .map(|id| (*id, field_value(&self.config, *id).len()))
            .collect();
        for (id, length) in lengths {
            if let Some(state) = self.fields.get_mut(&id) {
                state.cursor = length;
            }
        }
    }

    fn save_state_text(&self, theme: &Theme) -> (String, Rgba) {
        match &self.save_state {
            SaveState::Idle => (String::new(), theme.text_faint),
            SaveState::Pending => ("Saving…".to_string(), theme.text_muted),
            SaveState::Saved => ("Saved · Worker reloaded".to_string(), theme.success),
            SaveState::SavedWorkerDown(error) => (
                format!("Saved · Worker not reloaded: {error}"),
                theme.warning,
            ),
            SaveState::Failed(error) => (format!("Save failed: {error}"), theme.danger),
            SaveState::ExternalLoadFailed(error) => {
                (format!("config.json error: {error}"), theme.danger)
            }
        }
    }

    fn mark_dirty(&mut self, cx: &mut Context<Self>) {
        // 旧 UI 的日志级别下拉会联动 LogEnabled；ConfigEditor 再由 LogLevel 推导 diagnostic_mode。
        self.config.audit.diagnostic_mode = matches!(
            self.config.audit.log_level.to_ascii_lowercase().as_str(),
            "all" | "debug"
        );
        let _ = self
            .config_commands
            .send(ConfigCommand::Edit(Box::new(self.config.clone())));
        self.save_state = SaveState::Pending;
        cx.notify();
    }

    fn set_theme_mode(&mut self, mode: ThemeMode, cx: &mut Context<Self>) {
        if self.theme_mode == mode {
            return;
        }
        self.theme_mode = mode;
        self.config.ui.theme_variant = mode.variant().to_string();
        self.mark_dirty(cx);
    }

    fn edit_field(&mut self, id: FieldId, event: &KeyDownEvent, cx: &mut Context<Self>) {
        let modifiers = &event.keystroke.modifiers;
        if modifiers.control || modifiers.platform {
            if event.keystroke.key.eq_ignore_ascii_case("v")
                && let Some(item) = cx.read_from_clipboard()
                && let Some(text) = item.text()
            {
                let filtered: String = text
                    .chars()
                    .filter(|character| *character != '\r' && *character != '\n')
                    .collect();
                self.insert_field_text(id, &filtered, cx);
            }
            return;
        }

        match event.keystroke.key.as_str() {
            "backspace" => self.backspace_field(id, cx),
            "delete" => self.delete_field(id, cx),
            "left" => self.move_cursor(id, false),
            "right" => self.move_cursor(id, true),
            "home" => self.set_cursor(id, 0),
            "end" => {
                let end = field_value(&self.config, id).len();
                self.set_cursor(id, end);
            }
            "enter" | "tab" | "escape" => {}
            _ => {
                if let Some(text) = event
                    .keystroke
                    .key_char
                    .as_deref()
                    .filter(|text| !text.is_empty())
                {
                    self.insert_field_text(id, text, cx);
                }
            }
        }
    }

    fn insert_field_text(&mut self, id: FieldId, text: &str, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let new_cursor = {
            let value = field_value_mut(&mut self.config, id);
            let cursor = floor_char_boundary(value, cursor);
            value.insert_str(cursor, text);
            cursor + text.len()
        };
        self.set_cursor(id, new_cursor);
        self.mark_dirty(cx);
    }

    fn backspace_field(&mut self, id: FieldId, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let (new_cursor, changed) = {
            let value = field_value_mut(&mut self.config, id);
            let cursor = floor_char_boundary(value, cursor);
            let start = prev_char_boundary(value, cursor);
            if start == cursor {
                (cursor, false)
            } else {
                value.replace_range(start..cursor, "");
                (start, true)
            }
        };
        self.set_cursor(id, new_cursor);
        if changed {
            self.mark_dirty(cx);
        }
    }

    fn delete_field(&mut self, id: FieldId, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let (new_cursor, changed) = {
            let value = field_value_mut(&mut self.config, id);
            let cursor = floor_char_boundary(value, cursor);
            let end = next_char_boundary(value, cursor);
            if end == cursor {
                (cursor, false)
            } else {
                value.replace_range(cursor..end, "");
                (cursor, true)
            }
        };
        self.set_cursor(id, new_cursor);
        if changed {
            self.mark_dirty(cx);
        }
    }

    fn move_cursor(&mut self, id: FieldId, forward: bool) {
        let cursor = self.field_cursor(id);
        let value = field_value(&self.config, id);
        let cursor = floor_char_boundary(value, cursor);
        let next = if forward {
            next_char_boundary(value, cursor)
        } else {
            prev_char_boundary(value, cursor)
        };
        self.set_cursor(id, next);
    }

    fn field_cursor(&self, id: FieldId) -> usize {
        self.fields
            .get(&id)
            .map(|state| state.cursor)
            .unwrap_or_default()
    }

    fn set_cursor(&mut self, id: FieldId, cursor: usize) {
        if let Some(state) = self.fields.get_mut(&id) {
            state.cursor = cursor;
        }
    }

    fn field_state(&self, id: FieldId, window: &Window) -> (bool, usize, FocusHandle) {
        let state = self
            .fields
            .get(&id)
            .expect("配置页字段的焦点句柄在构造时已建立");
        (
            state.focus.is_focused(window),
            state.cursor,
            state.focus.clone(),
        )
    }

    fn pick_path(&mut self, id: FieldId, files: bool, window: &mut Window, cx: &mut Context<Self>) {
        let receiver = cx.prompt_for_paths(PathPromptOptions {
            files,
            directories: !files,
            multiple: false,
            prompt: Some(
                if files {
                    "Select ExifTool"
                } else {
                    "Select folder"
                }
                .into(),
            ),
        });
        cx.spawn_in(window, async move |this, cx| {
            let selected = receiver
                .await
                .ok()
                .and_then(|result| result.ok())
                .flatten()
                .and_then(|paths| paths.into_iter().next());
            let Some(path) = selected else {
                return;
            };
            let text = path.to_string_lossy().into_owned();
            this.update(cx, |this, cx| {
                let length = text.len();
                *field_value_mut(&mut this.config, id) = text;
                this.set_cursor(id, length);
                this.mark_dirty(cx);
            })
            .ok();
        })
        .detach();
    }

    fn add_excluded_directory(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let receiver = cx.prompt_for_paths(PathPromptOptions {
            files: false,
            directories: true,
            multiple: false,
            prompt: Some("Select folder to exclude".into()),
        });
        cx.spawn_in(window, async move |this, cx| {
            let selected = receiver
                .await
                .ok()
                .and_then(|result| result.ok())
                .flatten()
                .and_then(|paths| paths.into_iter().next());
            let Some(path) = selected else {
                return;
            };
            let text = path.to_string_lossy().into_owned();
            this.update(cx, |this, cx| {
                this.config.watch.auto_excluded_directories.push(text);
                this.mark_dirty(cx);
            })
            .ok();
        })
        .detach();
    }

    fn state_color(state: ConnectionState, theme: &Theme) -> Rgba {
        match state {
            ConnectionState::Connected => theme.success,
            ConnectionState::Connecting => theme.accent,
            ConnectionState::Reconnecting => theme.warning,
            ConnectionState::Disconnected => theme.danger,
        }
    }

    fn theme_switch(mode: ThemeMode, theme: Theme, cx: &mut Context<Self>) -> AnyElement {
        div()
            .flex()
            .items_center()
            .gap(px(2.))
            .p(px(2.))
            .bg(theme.surface_raised)
            .border_1()
            .border_color(theme.border)
            .rounded(px(9.))
            .children(ThemeMode::all().into_iter().map(move |candidate| {
                let active = candidate == mode;
                div()
                    .id(candidate.label())
                    .px(px(10.))
                    .py(px(4.))
                    .rounded(px(7.))
                    .text_size(px(12.))
                    .when(active, |element| {
                        element
                            .bg(theme.surface)
                            .text_color(theme.text)
                            .font_weight(FontWeight::MEDIUM)
                    })
                    .when(!active, |element| element.text_color(theme.text_muted))
                    .hover(|element| element.bg(theme.surface))
                    .cursor_pointer()
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.set_theme_mode(candidate, cx);
                    }))
                    .child(candidate.label())
            }))
            .into_any_element()
    }

    fn row_base(
        label: &'static str,
        description: &'static str,
        control: AnyElement,
        theme: Theme,
    ) -> gpui::Div {
        div()
            .flex()
            .items_center()
            .justify_between()
            .gap(px(24.))
            .px(px(16.))
            .py(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .flex_1()
                    .child(div().text_size(px(13.)).text_color(theme.text).child(label))
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(description),
                    ),
            )
            .child(div().flex_shrink_0().child(control))
    }

    fn settings_row(
        label: &'static str,
        description: &'static str,
        control: AnyElement,
        theme: Theme,
    ) -> AnyElement {
        Self::row_base(label, description, control, theme).into_any_element()
    }

    fn toggle_row(
        id: &'static str,
        label: &'static str,
        description: &'static str,
        on: bool,
        theme: Theme,
        cx: &mut Context<Self>,
        toggle: impl Fn(&mut Self, &mut Context<Self>) + 'static,
    ) -> AnyElement {
        Self::row_base(label, description, Self::toggle_switch(on, theme), theme)
            .id(id)
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, _, cx| toggle(this, cx)))
            .into_any_element()
    }

    fn toggle_switch(on: bool, theme: Theme) -> AnyElement {
        div()
            .flex()
            .items_center()
            .justify_start()
            .w(px(34.))
            .h(px(20.))
            .px(px(2.))
            .rounded_full()
            .bg(if on { theme.accent } else { theme.border })
            .when(on, |element| element.justify_end())
            .child(div().size(px(16.)).rounded_full().bg(theme.surface))
            .into_any_element()
    }

    fn choice_chip(
        id: String,
        label: &'static str,
        active: bool,
        theme: Theme,
        cx: &mut Context<Self>,
        select: impl Fn(&mut Self, &mut Context<Self>) + 'static,
    ) -> AnyElement {
        div()
            .id(id)
            .px(px(10.))
            .py(px(4.))
            .rounded(px(7.))
            .text_size(px(12.))
            .when(active, |element| {
                element
                    .bg(theme.accent)
                    .text_color(theme.bg)
                    .font_weight(FontWeight::MEDIUM)
            })
            .when(!active, |element| {
                element
                    .bg(theme.surface_raised)
                    .text_color(theme.text_muted)
            })
            .hover(|element| element.text_color(theme.text))
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, _, cx| select(this, cx)))
            .child(label)
            .into_any_element()
    }

    fn text_field(
        id: FieldId,
        value: String,
        focused: bool,
        cursor: usize,
        focus: FocusHandle,
        theme: Theme,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let focus_for_click = focus.clone();
        let cursor_for_click = cursor;
        let content = if value.is_empty() && !focused {
            div()
                .text_size(px(12.))
                .text_color(theme.text_faint)
                .child(id.placeholder())
                .into_any_element()
        } else {
            let cursor = floor_char_boundary(&value, cursor);
            let before = value[..cursor].to_string();
            let after = value[cursor..].to_string();
            div()
                .flex()
                .items_center()
                .min_w(px(0.))
                .overflow_hidden()
                .child(
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text)
                        .whitespace_nowrap()
                        .child(before),
                )
                .when(focused, |element| {
                    element.child(div().w(px(1.)).h(px(14.)).bg(theme.accent))
                })
                .child(
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text)
                        .whitespace_nowrap()
                        .child(after),
                )
                .into_any_element()
        };

        div()
            .id(id.element_id())
            .flex()
            .items_center()
            .flex_1()
            .min_w(px(0.))
            .h(px(30.))
            .px(px(10.))
            .bg(theme.surface)
            .border_1()
            .border_color(if focused { theme.accent } else { theme.border })
            .rounded(px(7.))
            .cursor(CursorStyle::IBeam)
            .track_focus(&focus)
            .on_mouse_down(
                MouseButton::Left,
                cx.listener(move |this, _, window, cx| {
                    window.focus(&focus_for_click, cx);
                    this.set_cursor(id, cursor_for_click);
                    cx.notify();
                }),
            )
            .on_key_down(cx.listener(move |this, event: &KeyDownEvent, _, cx| {
                this.edit_field(id, event, cx);
            }))
            .child(content)
            .into_any_element()
    }

    fn browse_button(id: FieldId, files: bool, theme: Theme, cx: &mut Context<Self>) -> AnyElement {
        div()
            .id(id.browse_id())
            .px(px(10.))
            .py(px(6.))
            .rounded(px(7.))
            .border_1()
            .border_color(theme.border)
            .text_size(px(12.))
            .text_color(theme.text_muted)
            .hover(|element| element.bg(theme.surface_raised))
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, window, cx| {
                this.pick_path(id, files, window, cx);
            }))
            .child("Browse")
            .into_any_element()
    }

    #[allow(clippy::too_many_arguments)]
    fn path_control(
        id: FieldId,
        value: String,
        focused: bool,
        cursor: usize,
        focus: FocusHandle,
        files: bool,
        theme: Theme,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        div()
            .flex()
            .items_center()
            .gap(px(8.))
            .w(px(CONTROL_WIDTH))
            .child(Self::text_field(
                id, value, focused, cursor, focus, theme, cx,
            ))
            .child(Self::browse_button(id, files, theme, cx))
            .into_any_element()
    }

    fn group(title: &'static str, rows: Vec<AnyElement>, theme: Theme) -> AnyElement {
        div()
            .flex()
            .flex_col()
            .gap(px(8.))
            .child(
                div()
                    .text_size(px(11.))
                    .font_weight(FontWeight::MEDIUM)
                    .text_color(theme.text_faint)
                    .child(title),
            )
            .child(
                div()
                    .flex()
                    .flex_col()
                    .bg(theme.surface)
                    .border_1()
                    .border_color(theme.border)
                    .rounded(px(12.))
                    .overflow_hidden()
                    .children(rows),
            )
            .into_any_element()
    }

    fn list_row(
        label: &'static str,
        description: &'static str,
        list: AnyElement,
        theme: Theme,
    ) -> AnyElement {
        div()
            .flex()
            .flex_col()
            .gap(px(8.))
            .px(px(16.))
            .py(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .child(div().text_size(px(13.)).text_color(theme.text).child(label))
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(description),
                    ),
            )
            .child(list)
            .into_any_element()
    }

    fn config_page(&self, window: &Window, cx: &mut Context<Self>) -> AnyElement {
        let theme = Theme::resolve(self.theme_mode, theme::is_dark(window.appearance()));
        let (status_text, status_color) = self.save_state_text(&theme);
        let mut groups: Vec<AnyElement> = Vec::new();

        // 路径组
        let mut path_rows = Vec::new();
        let (exif_focused, exif_cursor, exif_focus) =
            self.field_state(FieldId::ExifToolPath, window);
        path_rows.push(Self::settings_row(
            "ExifTool path",
            "Executable used for every cleanup run",
            Self::path_control(
                FieldId::ExifToolPath,
                self.config.exiftool.path.clone(),
                exif_focused,
                exif_cursor,
                exif_focus,
                true,
                theme,
                cx,
            ),
            theme,
        ));
        let (hot_focused, hot_cursor, hot_focus) = self.field_state(FieldId::HotFolder, window);
        path_rows.push(Self::settings_row(
            "Hot folder",
            "Directory the Worker watches for new media",
            Self::path_control(
                FieldId::HotFolder,
                self.config.watch.hot_folder.clone(),
                hot_focused,
                hot_cursor,
                hot_focus,
                false,
                theme,
                cx,
            ),
            theme,
        ));
        groups.push(Self::group("PATHS", path_rows, theme));

        // 行为组
        let mut behavior_rows = Vec::new();
        behavior_rows.push(Self::toggle_row(
            "toggle-backup",
            "Back up before cleaning",
            "Keep an untouched copy of every original",
            self.config.backup.enabled,
            theme,
            cx,
            |this, cx| {
                this.config.backup.enabled = !this.config.backup.enabled;
                this.mark_dirty(cx);
            },
        ));
        if self.config.backup.enabled {
            let (focused, cursor, focus) = self.field_state(FieldId::BackupDirectory, window);
            behavior_rows.push(Self::settings_row(
                "Backup directory",
                "Empty means <hot folder>/bak",
                Self::path_control(
                    FieldId::BackupDirectory,
                    self.config.backup.directory.clone(),
                    focused,
                    cursor,
                    focus,
                    false,
                    theme,
                    cx,
                ),
                theme,
            ));
        }
        behavior_rows.push(Self::toggle_row(
            "toggle-quarantine",
            "Quarantine failed files",
            "Move unreadable or failed media aside instead of deleting",
            self.config.quarantine.enabled,
            theme,
            cx,
            |this, cx| {
                this.config.quarantine.enabled = !this.config.quarantine.enabled;
                this.mark_dirty(cx);
            },
        ));
        if self.config.quarantine.enabled {
            let (focused, cursor, focus) = self.field_state(FieldId::QuarantineDirectory, window);
            behavior_rows.push(Self::settings_row(
                "Quarantine directory",
                "Empty means <hot folder>/_quarantine",
                Self::path_control(
                    FieldId::QuarantineDirectory,
                    self.config.quarantine.directory.clone(),
                    focused,
                    cursor,
                    focus,
                    false,
                    theme,
                    cx,
                ),
                theme,
            ));
        }
        behavior_rows.push(Self::toggle_row(
            "toggle-hide-startup",
            "Hide window on startup",
            "Start in the background with only the tray icon (tray parity lands in #59)",
            self.config.ui.hide_main_window_on_startup,
            theme,
            cx,
            |this, cx| {
                this.config.ui.hide_main_window_on_startup =
                    !this.config.ui.hide_main_window_on_startup;
                this.mark_dirty(cx);
            },
        ));
        behavior_rows.push(Self::toggle_row(
            "toggle-hide-tray",
            "Hide tray icon",
            "Run without a tray icon (tray parity lands in #59)",
            self.config.ui.hide_tray_icon,
            theme,
            cx,
            |this, cx| {
                this.config.ui.hide_tray_icon = !this.config.ui.hide_tray_icon;
                this.mark_dirty(cx);
            },
        ));

        let mut theme_chips = Vec::new();
        for candidate in ThemeMode::all() {
            theme_chips.push(Self::choice_chip(
                format!("theme-{}", candidate.variant()),
                candidate.label(),
                self.theme_mode == candidate,
                theme,
                cx,
                move |this, cx| this.set_theme_mode(candidate, cx),
            ));
        }
        behavior_rows.push(Self::settings_row(
            "Theme",
            "Light, dark, or follow the system appearance",
            div()
                .flex()
                .items_center()
                .gap(px(6.))
                .w(px(CONTROL_WIDTH))
                .children(theme_chips)
                .into_any_element(),
            theme,
        ));

        let mut locale_chips = Vec::new();
        for &(code, label) in LOCALES {
            let active = self.config.ui.locale.eq_ignore_ascii_case(code);
            let code = code.to_string();
            locale_chips.push(Self::choice_chip(
                format!("locale-{code}"),
                label,
                active,
                theme,
                cx,
                move |this, cx| {
                    this.config.ui.locale = code.clone();
                    this.mark_dirty(cx);
                },
            ));
        }
        behavior_rows.push(Self::settings_row(
            "Language",
            "Stored in config.json; new-UI translation lands in #60",
            div()
                .flex()
                .flex_wrap()
                .items_center()
                .gap(px(6.))
                .w(px(CONTROL_WIDTH))
                .children(locale_chips)
                .into_any_element(),
            theme,
        ));
        groups.push(Self::group("BEHAVIOR", behavior_rows, theme));

        // 排除目录组
        let mut excluded_rows = Vec::new();
        let system_excluded = resolve_auto_excluded_subdirectories(&self.config);
        let system_list = if system_excluded.is_empty() {
            div()
                .text_size(px(12.))
                .text_color(theme.text_faint)
                .child("None")
                .into_any_element()
        } else {
            div()
                .flex()
                .flex_col()
                .gap(px(2.))
                .children(system_excluded.into_iter().map(|path| {
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text_muted)
                        .truncate()
                        .child(path)
                }))
                .into_any_element()
        };
        excluded_rows.push(Self::list_row(
            "System excluded directories",
            "Audit, quarantine, backup, and custom entries under the hot folder",
            system_list,
            theme,
        ));

        let mut user_excluded = Vec::new();
        if self.config.watch.auto_excluded_directories.is_empty() {
            user_excluded.push(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_faint)
                    .child("No user-excluded directories")
                    .into_any_element(),
            );
        } else {
            for (index, path) in self
                .config
                .watch
                .auto_excluded_directories
                .iter()
                .enumerate()
            {
                let path_to_remove = path.clone();
                user_excluded.push(
                    div()
                        .id(format!("excluded-{index}"))
                        .flex()
                        .items_center()
                        .justify_between()
                        .gap(px(8.))
                        .px(px(8.))
                        .py(px(4.))
                        .rounded(px(6.))
                        .bg(theme.surface_raised)
                        .child(
                            div()
                                .text_size(px(12.))
                                .text_color(theme.text)
                                .truncate()
                                .child(path.clone()),
                        )
                        .child(
                            div()
                                .id(format!("remove-excluded-{index}"))
                                .px(px(6.))
                                .rounded(px(5.))
                                .text_size(px(12.))
                                .text_color(theme.text_muted)
                                .hover(|element| element.text_color(theme.danger))
                                .cursor_pointer()
                                .on_click(cx.listener(move |this, _, _, cx| {
                                    if let Some(position) = this
                                        .config
                                        .watch
                                        .auto_excluded_directories
                                        .iter()
                                        .position(|entry| entry == &path_to_remove)
                                    {
                                        this.config
                                            .watch
                                            .auto_excluded_directories
                                            .remove(position);
                                    }
                                    this.mark_dirty(cx);
                                }))
                                .child("Remove"),
                        )
                        .into_any_element(),
                );
            }
        }
        user_excluded.push(
            div()
                .id("add-excluded")
                .mt(px(2.))
                .px(px(10.))
                .py(px(5.))
                .rounded(px(7.))
                .border_1()
                .border_color(theme.border)
                .text_size(px(12.))
                .text_color(theme.text_muted)
                .hover(|element| element.bg(theme.surface_raised))
                .cursor_pointer()
                .on_click(cx.listener(|this, _, window, cx| {
                    this.add_excluded_directory(window, cx);
                }))
                .child("+ Add folder")
                .into_any_element(),
        );
        excluded_rows.push(Self::list_row(
            "User excluded directories",
            "Entries stored in watch.auto_excluded_directories",
            div()
                .flex()
                .flex_col()
                .gap(px(4.))
                .children(user_excluded)
                .into_any_element(),
            theme,
        ));
        groups.push(Self::group("EXCLUDED DIRECTORIES", excluded_rows, theme));

        // 日志组
        let mut log_rows = Vec::new();
        let mut level_chips = Vec::new();
        for &level in LOG_LEVELS {
            let active = self.config.audit.log_level.eq_ignore_ascii_case(level);
            let level_for_config = level.to_string();
            level_chips.push(Self::choice_chip(
                format!("loglevel-{level}"),
                level,
                active,
                theme,
                cx,
                move |this, cx| {
                    this.config.audit.log_level = level_for_config.clone();
                    this.mark_dirty(cx);
                },
            ));
        }
        log_rows.push(Self::settings_row(
            "Log level",
            "Filters the audit tail; all/debug also enable diagnostic mode",
            div()
                .flex()
                .items_center()
                .gap(px(6.))
                .w(px(CONTROL_WIDTH))
                .children(level_chips)
                .into_any_element(),
            theme,
        ));
        let (audit_focused, audit_cursor, audit_focus) =
            self.field_state(FieldId::AuditLogDirectory, window);
        log_rows.push(Self::settings_row(
            "Audit log directory",
            "Empty means <hot folder>/_audit",
            Self::path_control(
                FieldId::AuditLogDirectory,
                self.config.audit.log_directory.clone(),
                audit_focused,
                audit_cursor,
                audit_focus,
                false,
                theme,
                cx,
            ),
            theme,
        ));
        groups.push(Self::group("LOGS", log_rows, theme));

        div()
            .flex()
            .flex_col()
            .gap(px(12.))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .gap(px(16.))
                    .child(
                        div()
                            .text_size(px(18.))
                            .font_weight(FontWeight::SEMIBOLD)
                            .child("Configuration"),
                    )
                    .when(!status_text.is_empty(), |element| {
                        element.child(
                            div()
                                .text_size(px(11.))
                                .text_color(status_color)
                                .truncate()
                                .child(status_text),
                        )
                    }),
            )
            .children(groups)
            .into_any_element()
    }
}

fn field_value(config: &AppConfig, id: FieldId) -> &str {
    match id {
        FieldId::ExifToolPath => &config.exiftool.path,
        FieldId::HotFolder => &config.watch.hot_folder,
        FieldId::BackupDirectory => &config.backup.directory,
        FieldId::QuarantineDirectory => &config.quarantine.directory,
        FieldId::AuditLogDirectory => &config.audit.log_directory,
    }
}

fn field_value_mut(config: &mut AppConfig, id: FieldId) -> &mut String {
    match id {
        FieldId::ExifToolPath => &mut config.exiftool.path,
        FieldId::HotFolder => &mut config.watch.hot_folder,
        FieldId::BackupDirectory => &mut config.backup.directory,
        FieldId::QuarantineDirectory => &mut config.quarantine.directory,
        FieldId::AuditLogDirectory => &mut config.audit.log_directory,
    }
}

fn floor_char_boundary(text: &str, mut index: usize) -> usize {
    index = index.min(text.len());
    while index > 0 && !text.is_char_boundary(index) {
        index -= 1;
    }
    index
}

fn prev_char_boundary(text: &str, cursor: usize) -> usize {
    text[..cursor]
        .char_indices()
        .next_back()
        .map(|(index, _)| index)
        .unwrap_or(0)
}

fn next_char_boundary(text: &str, cursor: usize) -> usize {
    text[cursor..]
        .char_indices()
        .nth(1)
        .map(|(index, _)| cursor + index)
        .unwrap_or(text.len())
}

fn or_dash(value: Option<String>) -> String {
    value
        .filter(|value| !value.is_empty())
        .unwrap_or_else(|| "—".to_string())
}

impl Render for AppView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = Theme::resolve(self.theme_mode, theme::is_dark(window.appearance()));
        let state = self.snapshot.state;
        let dot = Self::state_color(state, &theme);

        let endpoint = or_dash(self.snapshot.endpoint.clone());
        let mode = or_dash(
            self.snapshot
                .worker_mode
                .map(|mode| mode.label().to_string()),
        );
        let round_trip = match self.snapshot.last_round_trip {
            Some(elapsed) => format!("last round trip {} ms", elapsed.as_millis()),
            None => "no round trip yet".to_string(),
        };

        let header = div()
            .flex()
            .items_center()
            .justify_between()
            .px(px(24.))
            .pt(px(20.))
            .pb(px(8.))
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(3.))
                    .child(
                        div()
                            .text_size(px(18.))
                            .font_weight(FontWeight::SEMIBOLD)
                            .child("PhotoPrivacy"),
                    )
                    .child(
                        div()
                            .text_size(px(12.))
                            .text_color(theme.text_muted)
                            .child("Metadata cleanup · configuration"),
                    ),
            )
            .child(Self::theme_switch(self.theme_mode, theme, cx));

        let connection = div()
            .flex()
            .flex_col()
            .gap(px(2.))
            .px(px(24.))
            .pb(px(8.))
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(8.))
                    .child(div().size(px(8.)).rounded_full().bg(dot))
                    .child(
                        div()
                            .text_size(px(13.))
                            .font_weight(FontWeight::MEDIUM)
                            .child(state.label()),
                    )
                    .child(
                        div()
                            .text_size(px(12.))
                            .text_color(theme.text_muted)
                            .child(format!("· {endpoint} · {mode}")),
                    )
                    .child(div().flex_1())
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_faint)
                            .child(round_trip),
                    ),
            )
            .children(self.snapshot.last_error.clone().map(|error| {
                div()
                    .text_size(px(12.))
                    .text_color(theme.danger)
                    .child(error)
            }));

        let page = self.config_page(window, cx);

        let footer = div()
            .flex()
            .items_center()
            .justify_between()
            .gap(px(16.))
            .px(px(24.))
            .py(px(10.))
            .border_t_1()
            .border_color(theme.border)
            .child(
                div()
                    .flex_1()
                    .min_w(px(0.))
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .truncate()
                    .child(self.config_path.display().to_string()),
            )
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child(concat!("v", env!("CARGO_PKG_VERSION"))),
            );

        div()
            .size_full()
            .bg(theme.bg)
            .text_color(theme.text)
            .flex()
            .flex_col()
            .child(header)
            .child(connection)
            .child(
                div()
                    .flex_1()
                    .min_h(px(0.))
                    .id("config-scroll")
                    .overflow_y_scroll()
                    .px(px(24.))
                    .pb(px(16.))
                    .child(page),
            )
            .child(footer)
    }
}
