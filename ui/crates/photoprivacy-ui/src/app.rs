use crate::service_page::{ServiceCommand, ServiceFeedback, ServiceSnapshot};
use crate::theme::{self, Theme, ThemeMode};
use crate::worker_connection::UiEvent;
use gpui::prelude::*;
use gpui::{Context, Div, FontWeight, IntoElement, Render, Rgba, Subscription, Window, div, px};
use photoprivacy_ipc::{ConnectionSnapshot, ConnectionState};
use photoprivacy_service::{ServiceRuntimeState, button_state};
use std::time::Duration;
use tokio::sync::{mpsc, watch};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Page {
    Overview,
    Service,
}

impl Page {
    fn label(self) -> &'static str {
        match self {
            Page::Overview => "Overview",
            Page::Service => "Service",
        }
    }

    fn all() -> [Page; 2] {
        [Page::Overview, Page::Service]
    }
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
    events: mpsc::UnboundedReceiver<UiEvent>,
    service: ServiceSnapshot,
    service_snapshots: watch::Receiver<ServiceSnapshot>,
    service_commands: mpsc::UnboundedSender<ServiceCommand>,
    theme_mode: ThemeMode,
    _appearance_subscription: Subscription,
}

impl AppView {
    pub fn new(
        window: &mut Window,
        cx: &mut Context<Self>,
        snapshots: watch::Receiver<ConnectionSnapshot>,
        events: mpsc::UnboundedReceiver<UiEvent>,
        service_snapshots: watch::Receiver<ServiceSnapshot>,
        service_commands: mpsc::UnboundedSender<ServiceCommand>,
    ) -> Self {
        let appearance_subscription = cx.observe_window_appearance(window, |_, _, cx| cx.notify());
        let snapshot = snapshots.borrow().clone();
        let service = service_snapshots.borrow().clone();
        let view = Self {
            page: Page::Overview,
            snapshot,
            snapshots,
            events,
            service,
            service_snapshots,
            service_commands,
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
                        if this.service_snapshots.has_changed().unwrap_or(false) {
                            this.service = this.service_snapshots.borrow_and_update().clone();
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

    fn service_state_color(state: ServiceRuntimeState, theme: &Theme) -> Rgba {
        match state {
            ServiceRuntimeState::Running => theme.success,
            ServiceRuntimeState::StartPending | ServiceRuntimeState::ContinuePending => {
                theme.accent
            }
            ServiceRuntimeState::Paused | ServiceRuntimeState::PausePending => theme.warning,
            ServiceRuntimeState::StopPending => theme.warning,
            ServiceRuntimeState::Stopped => theme.text_muted,
            ServiceRuntimeState::NotInstalled => theme.text_faint,
            ServiceRuntimeState::Unknown => theme.warning,
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

    fn page_switch(active: Page, theme: &Theme, cx: &mut Context<Self>) -> impl IntoElement {
        div()
            .flex()
            .items_center()
            .gap(px(2.))
            .p(px(2.))
            .bg(theme.surface_raised)
            .border_1()
            .border_color(theme.border)
            .rounded(px(9.))
            .children(Page::all().into_iter().map(move |candidate| {
                let selected = candidate == active;
                div()
                    .id(candidate.label())
                    .px(px(10.))
                    .py(px(4.))
                    .rounded(px(7.))
                    .text_size(px(12.))
                    .when(selected, |element| {
                        element
                            .bg(theme.surface)
                            .text_color(theme.text)
                            .font_weight(FontWeight::MEDIUM)
                    })
                    .when(!selected, |element| element.text_color(theme.text_muted))
                    .hover(|element| element.bg(theme.surface))
                    .cursor_pointer()
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.page = candidate;
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

        if matches!(kind, ButtonKind::Ghost) {
            button
                .cursor_pointer()
                .hover(|element| element.bg(theme.surface))
                .on_click(cx.listener(move |_, _, _, _| {
                    let _ = commands.send(command);
                }))
        } else {
            // 实心主按钮/危险按钮的 hover 改成 surface 会让 on_accent 前景失去对比度，只保留点击。
            button
                .cursor_pointer()
                .on_click(cx.listener(move |_, _, _, _| {
                    let _ = commands.send(command);
                }))
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
                    .items_center()
                    .gap(px(16.))
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
                                    .child("Metadata cleanup · new UI"),
                            ),
                    )
                    .child(Self::page_switch(self.page, &theme, cx)),
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

        let overview = div()
            .flex()
            .flex_col()
            .gap(px(12.))
            .child(status_card)
            .child(details);
        let service = self.service_page(&theme, cx);

        let body = div()
            .flex_1()
            .flex()
            .flex_col()
            .gap(px(12.))
            .px(px(24.))
            .pb(px(12.))
            .when(self.page == Page::Overview, |element| {
                element.child(overview)
            })
            .when(self.page == Page::Service, |element| element.child(service));

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
