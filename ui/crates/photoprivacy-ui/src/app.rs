use crate::log_service::{LogBatch, LogService};
use crate::service_page::{ServiceCommand, ServiceFeedback, ServiceSnapshot};
use crate::theme::{self, Theme, ThemeMode};
use gpui::prelude::*;
use gpui::{
    AnyElement, Context, Div, FontWeight, IntoElement, Render, Rgba, Stateful, Subscription,
    UniformListScrollHandle, Window, div, px, uniform_list,
};
use photoprivacy_ipc::{ConnectionSnapshot, ConnectionState, ServiceReachability};
use photoprivacy_logs::{AuditRecord, EventTone, LevelFilter, LogBuffer};
use photoprivacy_service::{ServiceRuntimeState, button_state};
use std::cell::{Cell, RefCell};
use std::rc::Rc;
use std::time::Duration;
use tokio::sync::{mpsc, watch};

const SNAPSHOT_POLL_INTERVAL: Duration = Duration::from_millis(200);

/// 日志页跨窗口重建保留的状态：托盘驻守时窗口会销毁/重建，日志缓冲不能随视图一起丢。
pub struct LogState {
    logs: LogBuffer,
    visible_indices: Vec<usize>,
    level_filter: LevelFilter,
    generation: u64,
}

impl LogState {
    /// 用初始级别创建空的共享日志状态。
    pub fn new(level_filter: LevelFilter) -> Self {
        Self {
            logs: LogBuffer::new(),
            visible_indices: Vec::new(),
            level_filter,
            generation: 0,
        }
    }

    /// 应用一个日志批次（代际校验通过时），返回状态是否变化。
    /// 由 app 级事件泵调用，窗口关闭期间也持续排空通道，避免无界队列堆积。
    pub fn apply_batch(&mut self, batch: LogBatch) -> bool {
        if batch.generation < self.generation {
            return false;
        }
        self.logs.push_batch(batch.records);
        self.visible_indices = self.logs.visible_indices(self.level_filter);
        true
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Page {
    Overview,
    Service,
    Logs,
}

impl Page {
    fn label(self) -> &'static str {
        match self {
            Self::Overview => "Overview",
            Self::Service => "Service",
            Self::Logs => "Logs",
        }
    }

    fn all() -> [Self; 3] {
        [Self::Overview, Self::Service, Self::Logs]
    }
}

/// 视图跨窗口重建需要沿用的依赖：Worker/服务快照、主题、日志状态与后台控制面。
/// 打包成一个类型，避免同一组参数在事件泵/开窗/视图构造之间反复穿行。
#[derive(Clone)]
pub struct AppDeps {
    pub snapshots: watch::Receiver<ConnectionSnapshot>,
    pub theme_mode: Rc<Cell<ThemeMode>>,
    pub log_state: Rc<RefCell<LogState>>,
    pub log_service: LogService,
    pub service_snapshots: watch::Receiver<ServiceSnapshot>,
    pub service_commands: mpsc::UnboundedSender<ServiceCommand>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ButtonKind {
    Primary,
    Ghost,
    Danger,
}

pub struct AppView {
    page: Page,
    snapshot: ConnectionSnapshot,
    snapshots: watch::Receiver<ConnectionSnapshot>,
    /// 主题、日志与服务快照都跨窗口重建保留（关窗驻守后托盘会重建窗口）。
    theme_mode: Rc<Cell<ThemeMode>>,
    log_state: Rc<RefCell<LogState>>,
    service: ServiceSnapshot,
    service_snapshots: watch::Receiver<ServiceSnapshot>,
    service_commands: mpsc::UnboundedSender<ServiceCommand>,
    log_service: LogService,
    log_scroll: UniformListScrollHandle,
    _appearance_subscription: Subscription,
}

impl AppView {
    /// 构造视图：Worker/服务快照、主题与日志状态都从 `AppDeps` 取，跨窗口重建复用。
    pub fn new(window: &mut Window, cx: &mut Context<Self>, deps: AppDeps) -> Self {
        let AppDeps {
            snapshots,
            theme_mode,
            log_state,
            log_service,
            service_snapshots,
            service_commands,
        } = deps;
        let appearance_subscription = cx.observe_window_appearance(window, |_, _, cx| cx.notify());
        let snapshot = snapshots.borrow().clone();
        let service = service_snapshots.borrow().clone();
        let view = Self {
            page: Page::Overview,
            snapshot,
            snapshots,
            theme_mode,
            log_state,
            service,
            service_snapshots,
            service_commands,
            log_service,
            log_scroll: UniformListScrollHandle::new(),
            _appearance_subscription: appearance_subscription,
        };
        view.spawn_updates(window, cx);
        view
    }

    /// Worker 快照从 tokio 线程来；这里在 UI 线程轮询，避免跨线程触碰 gpui 状态。
    /// 托盘、唤起与日志批次由 main 的 app 级事件泵处理（窗口不存在时也要能响应）。
    fn spawn_updates(&self, window: &Window, cx: &mut Context<Self>) {
        cx.spawn_in(window, async move |this, cx| {
            loop {
                cx.background_executor().timer(SNAPSHOT_POLL_INTERVAL).await;

                // 关窗驻守后本视图实体会被释放；此时必须退出循环，否则每次重建窗口都会留下一个
                // 永远 upgrade 失败的空转任务。
                let alive = this
                    .update(cx, |this, cx| {
                        if this.snapshots.has_changed().unwrap_or(false) {
                            this.snapshot = this.snapshots.borrow_and_update().clone();
                            cx.notify();
                        }
                        if this.service_snapshots.has_changed().unwrap_or(false) {
                            this.service = this.service_snapshots.borrow_and_update().clone();
                            cx.notify();
                        }
                    })
                    .is_ok();
                if !alive {
                    break;
                }
            }
        })
        .detach();
    }

    /// 按当前级别过滤共享日志并刷新可见索引。
    fn rebuild_visible(&self) {
        let mut state = self.log_state.borrow_mut();
        let visible = state.logs.visible_indices(state.level_filter);
        state.visible_indices = visible;
    }

    /// 清空共享日志缓冲并推进代际；旧代际批次由 app 级事件泵在应用时丢弃。
    fn clear_logs(&mut self) {
        let mut state = self.log_state.borrow_mut();
        state.generation = state.generation.wrapping_add(1);
        state.logs.clear();
        state.visible_indices.clear();
        let generation = state.generation;
        drop(state);
        self.log_scroll = UniformListScrollHandle::new();
        self.log_service.clear(generation);
    }

    fn state_color(state: ConnectionState, theme: &Theme) -> Rgba {
        match state {
            ConnectionState::Connected => theme.success,
            ConnectionState::Connecting => theme.accent,
            ConnectionState::Reconnecting => theme.warning,
            ConnectionState::Disconnected => theme.danger,
        }
    }

    fn tone_color(tone: EventTone, theme: &Theme) -> Rgba {
        match tone {
            EventTone::Success => theme.success,
            EventTone::Neutral => theme.text,
            EventTone::Muted => theme.text_faint,
            EventTone::Info => theme.text_muted,
            EventTone::Warning => theme.warning,
            EventTone::Danger => theme.danger,
        }
    }

    fn theme_switch(mode: ThemeMode, theme: &Theme, cx: &mut Context<Self>) -> Div {
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
                        this.theme_mode.set(candidate);
                        cx.notify();
                    }))
                    .child(candidate.label())
            }))
    }

    fn nav_tab(page: Page, current: Page, theme: &Theme, cx: &mut Context<Self>) -> Stateful<Div> {
        let active = page == current;
        div()
            .id(page.label())
            .px(px(11.))
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
                this.page = page;
                cx.notify();
            }))
            .child(page.label())
    }

    fn level_filter_group(
        current_level: LevelFilter,
        theme: &Theme,
        cx: &mut Context<Self>,
    ) -> Div {
        div()
            .flex()
            .items_center()
            .gap(px(2.))
            .p(px(2.))
            .bg(theme.surface_raised)
            .border_1()
            .border_color(theme.border)
            .rounded(px(9.))
            .children(LevelFilter::all().into_iter().map(move |candidate| {
                let active = candidate == current_level;
                div()
                    .id(candidate.label())
                    .px(px(9.))
                    .py(px(4.))
                    .rounded(px(7.))
                    .text_size(px(11.))
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
                        this.log_state.borrow_mut().level_filter = candidate;
                        this.rebuild_visible();
                        cx.notify();
                    }))
                    .child(candidate.label())
            }))
    }

    fn detail_row(label: &'static str, value: String, theme: &Theme) -> Div {
        div()
            .flex()
            .items_center()
            .justify_between()
            .gap(px(16.))
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(label),
            )
            .child(div().text_size(px(12.)).text_color(theme.text).child(value))
    }

    /// 服务可达性 → 展示文案。
    fn service_label(state: ServiceReachability) -> &'static str {
        match state {
            ServiceReachability::Unknown => "—",
            ServiceReachability::Reachable => "reachable",
            ServiceReachability::Unreachable => "unreachable",
        }
    }

    /// Overview 页：连接状态与 Worker 详情。
    fn overview_body(&self, theme: &Theme) -> AnyElement {
        let state = self.snapshot.state;
        let dot = Self::state_color(state, theme);
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

        let status_card = div()
            .flex()
            .flex_col()
            .gap(px(8.))
            .p(px(16.))
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(8.))
                    .child(div().size(px(8.)).rounded_full().bg(dot))
                    .child(
                        div()
                            .text_size(px(14.))
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

        let rows = [
            ("Worker mode", mode.clone()),
            ("Reported mode", or_dash(self.snapshot.status_mode.clone())),
            (
                "Service IPC",
                Self::service_label(self.snapshot.service_reachability).to_string(),
            ),
            (
                "Protocol",
                or_dash(
                    self.snapshot
                        .protocol_version
                        .map(|version| format!("v{version}")),
                ),
            ),
            ("ExifTool", or_dash(self.snapshot.exif_tool_version.clone())),
            ("Round trips", self.snapshot.round_trips.to_string()),
        ];

        let details = div()
            .flex()
            .flex_col()
            .gap(px(10.))
            .p(px(16.))
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child("WORKER"),
            )
            .children(
                rows.into_iter()
                    .map(|(label, value)| Self::detail_row(label, value, theme)),
            );

        div()
            .flex_1()
            .flex()
            .flex_col()
            .gap(px(12.))
            .px(px(24.))
            .pb(px(12.))
            .child(status_card)
            .child(details)
            .into_any_element()
    }

    /// Logs 页：级别过滤、清空与审计尾部列表。
    fn logs_body(&mut self, theme: &Theme, cx: &mut Context<Self>) -> AnyElement {
        let (level_filter, visible_len, logs_empty, filtered_empty) = {
            let state = self.log_state.borrow();
            (
                state.level_filter,
                state.visible_indices.len(),
                state.logs.is_empty(),
                !state.logs.is_empty() && state.visible_indices.is_empty(),
            )
        };
        let filter_group = Self::level_filter_group(level_filter, theme, cx);
        let count_label = format!("{visible_len} entries");
        let toolbar = div()
            .flex()
            .items_center()
            .gap(px(12.))
            .child(filter_group)
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child(count_label),
            )
            .child(div().flex_1())
            .child(
                div()
                    .id("clear-logs")
                    .px(px(12.))
                    .py(px(6.))
                    .rounded(px(8.))
                    .border_1()
                    .border_color(theme.border)
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .hover(|element| element.bg(theme.surface_raised).text_color(theme.text))
                    .cursor_pointer()
                    .on_click(cx.listener(|this, _, _, cx| {
                        this.clear_logs();
                        cx.notify();
                    }))
                    .child("Clear"),
            );

        let heading = div()
            .flex()
            .flex_col()
            .gap(px(3.))
            .child(
                div()
                    .text_size(px(18.))
                    .font_weight(FontWeight::SEMIBOLD)
                    .child("Logs"),
            )
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child("Audit tail from the daily JSONL — local tail plus IPC backfill."),
            );

        let card = if logs_empty {
            Self::log_card(
                theme,
                Self::empty_state(
                    "No audit entries yet",
                    "Audit events appear here as files are processed.",
                    theme,
                ),
            )
        } else if filtered_empty {
            Self::log_card(
                theme,
                Self::empty_state(
                    "No entries under the current level filter",
                    "Lower the level or wait for matching events.",
                    theme,
                ),
            )
        } else {
            let row_theme = *theme;
            let list = uniform_list(
                "audit-log",
                visible_len,
                cx.processor(move |this, range: std::ops::Range<usize>, _window, _cx| {
                    let state = this.log_state.borrow();
                    let mut rows = Vec::with_capacity(range.len());
                    for index in range {
                        let Some(entry_index) = state.visible_indices.get(index).copied() else {
                            continue;
                        };
                        let Some(entry) = state.logs.get(entry_index) else {
                            continue;
                        };
                        rows.push(Self::log_row(entry, &row_theme).into_any_element());
                    }
                    rows
                }),
            )
            .track_scroll(&self.log_scroll)
            .size_full();

            Self::log_card(
                theme,
                div()
                    .flex_1()
                    .flex()
                    .flex_col()
                    .overflow_hidden()
                    .child(Self::log_header(theme))
                    .child(div().flex_1().overflow_hidden().child(list)),
            )
        };

        div()
            .flex_1()
            .flex()
            .flex_col()
            .gap(px(12.))
            .px(px(24.))
            .pb(px(12.))
            .child(heading)
            .child(toolbar)
            .child(card)
            .into_any_element()
    }

    fn log_card(theme: &Theme, content: impl IntoElement) -> Div {
        div()
            .flex_1()
            .flex()
            .flex_col()
            .overflow_hidden()
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(content)
    }

    fn log_header(theme: &Theme) -> Div {
        div()
            .h(px(32.))
            .flex_none()
            .w_full()
            .flex()
            .items_center()
            .gap(px(12.))
            .px(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .text_size(px(11.))
            .text_color(theme.text_faint)
            .child(div().w(px(64.)).flex_none().child("TIME"))
            .child(div().w(px(150.)).flex_none().child("EVENT"))
            .child(div().w(px(250.)).flex_none().child("SOURCE"))
            .child(div().flex_1().child("MESSAGE"))
    }

    fn log_row(entry: &AuditRecord, theme: &Theme) -> Div {
        let tone = Self::tone_color(entry.tone, theme);
        div()
            .h(px(40.))
            .w_full()
            .flex()
            .items_center()
            .gap(px(12.))
            .px(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .hover(|element| element.bg(theme.surface_raised))
            .child(
                div()
                    .w(px(64.))
                    .flex_none()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(entry.time_text.clone()),
            )
            .child(
                div().w(px(150.)).flex_none().child(
                    div()
                        .px(px(8.))
                        .py(px(2.))
                        .rounded_full()
                        .bg(theme.surface_raised)
                        .text_size(px(11.))
                        .font_weight(FontWeight::MEDIUM)
                        .text_color(tone)
                        .truncate()
                        .child(entry.display_event.clone()),
                ),
            )
            .child(
                div()
                    .w(px(250.))
                    .flex_none()
                    .truncate()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(entry.source_path_masked.clone()),
            )
            .child(
                div()
                    .flex_1()
                    .truncate()
                    .text_size(px(12.))
                    .text_color(theme.text)
                    .child(entry.message.clone()),
            )
    }

    fn empty_state(title: &'static str, description: &'static str, theme: &Theme) -> Div {
        div()
            .flex_1()
            .flex()
            .flex_col()
            .items_center()
            .justify_center()
            .gap(px(6.))
            .p(px(24.))
            .child(
                div()
                    .text_size(px(15.))
                    .font_weight(FontWeight::MEDIUM)
                    .text_color(theme.text)
                    .child(title),
            )
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(description),
            )
    }

    fn service_state_color(state: ServiceRuntimeState, theme: &Theme) -> Rgba {
        match state {
            ServiceRuntimeState::Running => theme.success,
            ServiceRuntimeState::StartPending | ServiceRuntimeState::ContinuePending => {
                theme.accent
            }
            ServiceRuntimeState::Paused
            | ServiceRuntimeState::PausePending
            | ServiceRuntimeState::StopPending
            | ServiceRuntimeState::Unknown => theme.warning,
            ServiceRuntimeState::Stopped => theme.text_muted,
            ServiceRuntimeState::NotInstalled => theme.text_faint,
        }
    }

    fn service_row(
        label: &'static str,
        description: &'static str,
        action: impl IntoElement,
        theme: &Theme,
    ) -> Div {
        div()
            .flex()
            .items_center()
            .justify_between()
            .gap(px(16.))
            .py(px(10.))
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(3.))
                    .child(div().text_size(px(13.)).text_color(theme.text).child(label))
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(description),
                    ),
            )
            .child(action)
    }

    fn action_button(
        label: &'static str,
        command: ServiceCommand,
        enabled: bool,
        kind: ButtonKind,
        theme: &Theme,
        commands: mpsc::UnboundedSender<ServiceCommand>,
        cx: &mut Context<Self>,
    ) -> impl IntoElement {
        let (background, foreground, border) = if !enabled {
            (theme.surface_raised, theme.text_faint, theme.border)
        } else {
            match kind {
                ButtonKind::Primary => (theme.accent, theme.on_accent, theme.accent),
                ButtonKind::Ghost => (theme.surface_raised, theme.text, theme.border),
                ButtonKind::Danger => (theme.danger, theme.on_accent, theme.danger),
            }
        };

        let button = div()
            .id(label)
            .px(px(12.))
            .py(px(6.))
            .rounded(px(7.))
            .border_1()
            .border_color(border)
            .bg(background)
            .text_size(px(12.))
            .text_color(foreground)
            .child(label);

        if !enabled {
            return button;
        }

        let on_click = cx.listener(move |_, _, _, _| {
            let _ = commands.send(command);
        });
        let button = button.cursor_pointer().on_click(on_click);
        if matches!(kind, ButtonKind::Ghost) {
            button.hover(|element| element.bg(theme.surface))
        } else {
            // 实心主按钮/危险按钮的 hover 改成 surface 会让 on_accent 前景失去对比度，只保留点击。
            button
        }
    }

    fn divider(theme: &Theme) -> Div {
        div().h(px(1.)).bg(theme.border)
    }

    fn feedback_banner(feedback: &ServiceFeedback, theme: &Theme) -> Div {
        let (color, label) = match feedback.status {
            photoprivacy_service::ServiceCommandStatus::Success => (theme.success, "Success"),
            photoprivacy_service::ServiceCommandStatus::Skipped => (theme.text_muted, "Skipped"),
            photoprivacy_service::ServiceCommandStatus::ElevationCancelled => {
                (theme.warning, "Cancelled")
            }
            photoprivacy_service::ServiceCommandStatus::Failed => (theme.danger, "Failed"),
        };

        div()
            .flex()
            .items_center()
            .gap(px(8.))
            .p(px(10.))
            .rounded(px(8.))
            .border_1()
            .border_color(color)
            .bg(theme.surface)
            .child(
                div()
                    .text_size(px(11.))
                    .font_weight(FontWeight::MEDIUM)
                    .text_color(color)
                    .child(label),
            )
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text)
                    .child(feedback.message.clone()),
            )
    }

    fn service_page(&self, theme: &Theme, cx: &mut Context<Self>) -> Div {
        let service = self.service.clone();
        let commands = self.service_commands.clone();
        let buttons = button_state(service.state);
        let busy = service.busy;
        let enabled = |available: bool| available && !busy;

        let status_dot = Self::service_state_color(service.state, theme);
        let status_text = service.state.label().to_string();
        let status_row = Self::service_row(
            "Status",
            if busy {
                "Applying service command…"
            } else {
                "Refreshed every 3 s"
            },
            div()
                .flex()
                .items_center()
                .gap(px(8.))
                .child(div().size(px(8.)).rounded_full().bg(status_dot))
                .child(
                    div()
                        .text_size(px(13.))
                        .font_weight(FontWeight::MEDIUM)
                        .text_color(theme.text)
                        .child(status_text),
                ),
            theme,
        );

        let install_row = Self::service_row(
            "Install service",
            "Registers PhotoPrivacy as a system service.",
            Self::action_button(
                "Install",
                ServiceCommand::Install,
                enabled(buttons.install),
                ButtonKind::Primary,
                theme,
                commands.clone(),
                cx,
            ),
            theme,
        );

        let start_stop_row = Self::service_row(
            "Start / stop",
            "Controls the installed service process.",
            div()
                .flex()
                .items_center()
                .gap(px(6.))
                .child(Self::action_button(
                    "Start",
                    ServiceCommand::Start,
                    enabled(buttons.start),
                    ButtonKind::Ghost,
                    theme,
                    commands.clone(),
                    cx,
                ))
                .child(Self::action_button(
                    "Stop",
                    ServiceCommand::Stop,
                    enabled(buttons.stop),
                    ButtonKind::Ghost,
                    theme,
                    commands.clone(),
                    cx,
                ))
                .child(Self::action_button(
                    "Refresh",
                    ServiceCommand::Refresh,
                    true,
                    ButtonKind::Ghost,
                    theme,
                    commands.clone(),
                    cx,
                )),
            theme,
        );

        let uninstall_row = Self::service_row(
            "Uninstall service",
            "Stops and removes the service registration.",
            Self::action_button(
                "Uninstall",
                ServiceCommand::Uninstall,
                enabled(buttons.uninstall),
                ButtonKind::Danger,
                theme,
                commands,
                cx,
            ),
            theme,
        );

        let card = div()
            .flex()
            .flex_col()
            .px(px(16.))
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(status_row)
            .child(Self::divider(theme))
            .child(install_row)
            .child(Self::divider(theme))
            .child(start_stop_row)
            .child(Self::divider(theme))
            .child(uninstall_row);

        div()
            .flex()
            .flex_col()
            .gap(px(10.))
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child("SERVICE"),
            )
            .child(card)
            .children(
                service
                    .feedback
                    .as_ref()
                    .map(|feedback| Self::feedback_banner(feedback, theme)),
            )
    }
}

fn or_dash(value: Option<String>) -> String {
    value
        .filter(|value| !value.is_empty())
        .unwrap_or_else(|| "—".to_string())
}

impl Render for AppView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = Theme::resolve(self.theme_mode.get(), theme::is_dark(window.appearance()));
        let header = div()
            .flex()
            .items_center()
            .gap(px(16.))
            .px(px(24.))
            .pt(px(20.))
            .pb(px(10.))
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .child(
                        div()
                            .text_size(px(18.))
                            .font_weight(FontWeight::SEMIBOLD)
                            .child("PhotoPrivacy"),
                    )
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child("Metadata cleanup"),
                    ),
            )
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(2.))
                    .p(px(2.))
                    .bg(theme.surface_raised)
                    .border_1()
                    .border_color(theme.border)
                    .rounded(px(9.))
                    .children(
                        Page::all()
                            .into_iter()
                            .map(|page| Self::nav_tab(page, self.page, &theme, cx)),
                    ),
            )
            .child(div().flex_1())
            .child(Self::theme_switch(self.theme_mode.get(), &theme, cx));

        let content = match self.page {
            Page::Overview => self.overview_body(&theme),
            Page::Service => self.service_page(&theme, cx).into_any_element(),
            Page::Logs => self.logs_body(&theme, cx),
        };

        let footer = div()
            .flex()
            .items_center()
            .justify_between()
            .px(px(24.))
            .py(px(14.))
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child("Single instance: launching again brings this window forward."),
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
            .child(content)
            .child(footer)
    }
}
