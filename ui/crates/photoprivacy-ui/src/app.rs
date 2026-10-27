use crate::log_service::{LogBatch, LogService};
use crate::theme::{self, Theme, ThemeMode};
use crate::worker_connection::UiEvent;
use gpui::prelude::*;
use gpui::{
    AnyElement, Context, Div, FontWeight, IntoElement, Render, Rgba, Stateful, Subscription,
    UniformListScrollHandle, Window, div, px, uniform_list,
};
use photoprivacy_ipc::{ConnectionSnapshot, ConnectionState};
use photoprivacy_logs::{AuditRecord, EventTone, LevelFilter, LogBuffer};
use std::time::Duration;
use tokio::sync::{mpsc, watch};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Page {
    Overview,
    Logs,
}

impl Page {
    fn label(self) -> &'static str {
        match self {
            Self::Overview => "Overview",
            Self::Logs => "Logs",
        }
    }

    fn all() -> [Self; 2] {
        [Self::Overview, Self::Logs]
    }
}

pub struct AppView {
    page: Page,
    snapshot: ConnectionSnapshot,
    snapshots: watch::Receiver<ConnectionSnapshot>,
    events: mpsc::UnboundedReceiver<UiEvent>,
    theme_mode: ThemeMode,
    logs: LogBuffer,
    visible_indices: Vec<usize>,
    level_filter: LevelFilter,
    log_batches: mpsc::UnboundedReceiver<LogBatch>,
    log_service: LogService,
    log_generation: u64,
    log_scroll: UniformListScrollHandle,
    _appearance_subscription: Subscription,
}

impl AppView {
    pub fn new(
        window: &mut Window,
        cx: &mut Context<Self>,
        snapshots: watch::Receiver<ConnectionSnapshot>,
        events: mpsc::UnboundedReceiver<UiEvent>,
        log_batches: mpsc::UnboundedReceiver<LogBatch>,
        log_service: LogService,
        initial_level: LevelFilter,
    ) -> Self {
        let appearance_subscription = cx.observe_window_appearance(window, |_, _, cx| cx.notify());
        let snapshot = snapshots.borrow().clone();
        let view = Self {
            page: Page::Overview,
            snapshot,
            snapshots,
            events,
            theme_mode: ThemeMode::System,
            logs: LogBuffer::new(),
            visible_indices: Vec::new(),
            level_filter: initial_level,
            log_batches,
            log_service,
            log_generation: 0,
            log_scroll: UniformListScrollHandle::new(),
            _appearance_subscription: appearance_subscription,
        };
        view.spawn_updates(window, cx);
        view
    }

    /// 后台快照、日志批次与「唤起窗口」事件都从 tokio 线程来；这里在 UI 线程轮询，
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
                        let mut logs_changed = false;

                        if this.snapshots.has_changed().unwrap_or(false) {
                            this.snapshot = this.snapshots.borrow_and_update().clone();
                            cx.notify();
                        }
                        while let Ok(event) = this.events.try_recv() {
                            if matches!(event, UiEvent::ShowWindow) {
                                show_window = true;
                            }
                        }
                        while let Ok(batch) = this.log_batches.try_recv() {
                            if batch.generation >= this.log_generation {
                                this.logs.push_batch(batch.records);
                                logs_changed = true;
                            }
                        }
                        if logs_changed {
                            this.rebuild_visible();
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

    fn rebuild_visible(&mut self) {
        self.visible_indices = self.logs.visible_indices(self.level_filter);
    }

    fn clear_logs(&mut self) {
        self.log_generation = self.log_generation.wrapping_add(1);
        // 清空命令发出前已进入通道的批次属于旧代际，UI 侧一并丢弃。
        while self.log_batches.try_recv().is_ok() {}
        self.logs.clear();
        self.visible_indices.clear();
        self.log_scroll = UniformListScrollHandle::new();
        self.log_service.clear(self.log_generation);
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
                        this.theme_mode = candidate;
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
                        this.level_filter = candidate;
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

    fn logs_body(&mut self, theme: &Theme, cx: &mut Context<Self>) -> AnyElement {
        let filter_group = Self::level_filter_group(self.level_filter, theme, cx);
        let count_label = format!("{} entries", self.visible_indices.len());
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

        let filtered_empty = !self.logs.is_empty() && self.visible_indices.is_empty();
        let card = if self.logs.is_empty() {
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
                self.visible_indices.len(),
                cx.processor(move |this, range: std::ops::Range<usize>, _window, _cx| {
                    let mut rows = Vec::with_capacity(range.len());
                    for index in range {
                        let Some(entry_index) = this.visible_indices.get(index).copied() else {
                            continue;
                        };
                        let Some(entry) = this.logs.get(entry_index) else {
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
}

fn or_dash(value: Option<String>) -> String {
    value
        .filter(|value| !value.is_empty())
        .unwrap_or_else(|| "—".to_string())
}

impl Render for AppView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = Theme::resolve(self.theme_mode, theme::is_dark(window.appearance()));
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
            .child(Self::theme_switch(self.theme_mode, &theme, cx));

        let content = match self.page {
            Page::Overview => self.overview_body(&theme),
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
