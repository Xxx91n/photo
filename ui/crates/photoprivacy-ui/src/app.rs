use crate::theme::{self, Theme, ThemeMode};
use crate::worker_connection::UiEvent;
use gpui::prelude::*;
use gpui::{Context, FontWeight, IntoElement, Render, Rgba, Subscription, Window, div, px};
use photoprivacy_ipc::{ConnectionSnapshot, ConnectionState};
use std::time::Duration;
use tokio::sync::{mpsc, watch};

pub struct AppView {
    snapshot: ConnectionSnapshot,
    snapshots: watch::Receiver<ConnectionSnapshot>,
    events: mpsc::UnboundedReceiver<UiEvent>,
    theme_mode: ThemeMode,
    _appearance_subscription: Subscription,
}

impl AppView {
    pub fn new(
        window: &mut Window,
        cx: &mut Context<Self>,
        snapshots: watch::Receiver<ConnectionSnapshot>,
        events: mpsc::UnboundedReceiver<UiEvent>,
    ) -> Self {
        let appearance_subscription = cx.observe_window_appearance(window, |_, _, cx| cx.notify());
        let snapshot = snapshots.borrow().clone();
        let view = Self {
            snapshot,
            snapshots,
            events,
            theme_mode: ThemeMode::System,
            _appearance_subscription: appearance_subscription,
        };
        view.spawn_updates(window, cx);
        view
    }

    /// 后台快照与「唤起窗口」事件都从 tokio 线程来；这里在 UI 线程轮询，避免跨线程触碰 gpui 状态。
    fn spawn_updates(&self, window: &Window, cx: &mut Context<Self>) {
        cx.spawn_in(window, async move |this, cx| {
            loop {
                cx.background_executor()
                    .timer(Duration::from_millis(200))
                    .await;

                let show_window = this
                    .update(cx, |this, cx| {
                        let mut show_window = false;
                        if this.snapshots.has_changed().unwrap_or(false) {
                            this.snapshot = this.snapshots.borrow_and_update().clone();
                            cx.notify();
                        }
                        while let Ok(event) = this.events.try_recv() {
                            if matches!(event, UiEvent::ShowWindow) {
                                show_window = true;
                            }
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

    fn state_color(state: ConnectionState, theme: &Theme) -> Rgba {
        match state {
            ConnectionState::Connected => theme.success,
            ConnectionState::Connecting => theme.accent,
            ConnectionState::Reconnecting => theme.warning,
            ConnectionState::Disconnected => theme.danger,
        }
    }

    fn theme_switch(mode: ThemeMode, theme: &Theme, cx: &mut Context<Self>) -> impl IntoElement {
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

    fn detail_row(label: &'static str, value: String, theme: &Theme) -> impl IntoElement {
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
            .pb(px(10.))
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
                            .child("Metadata cleanup · new UI skeleton"),
                    ),
            )
            .child(Self::theme_switch(self.theme_mode, &theme, cx));

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
                    .map(|(label, value)| Self::detail_row(label, value, &theme)),
            );

        let body = div()
            .flex_1()
            .flex()
            .flex_col()
            .gap(px(12.))
            .px(px(24.))
            .pb(px(12.))
            .child(status_card)
            .child(details);

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
            .child(body)
            .child(footer)
    }
}
