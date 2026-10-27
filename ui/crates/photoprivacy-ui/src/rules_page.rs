//! 规则页：五族 × 六开关（rules.json 的全部持久化面）。
//! 旧 UI 的 Enabled 列不落盘、Worker 不消费；搜索与 effective-args 预览不在 #56 的 AC 内，
//! 本页不复刻（预览若做必须由 C# WipeRuleEngine 出值，避免双真相源）。
//! 文案经 `AppView::t` 按当前语言取词（#60），键常量集中在 `crate::i18n::keys`。

use crate::app::{AppView, MirrorRow};
use crate::i18n::keys as k;
use crate::theme::Theme;
use gpui::prelude::*;
use gpui::{Context, Div, FontWeight, IntoElement, Rgba, div, px};
use photoprivacy_rules::{Family, Group, Notice, RulesEditor};

const FAMILY_WIDTH: f32 = 90.;
const EXTENSIONS_WIDTH: f32 = 170.;
const TOGGLE_WIDTH: f32 = 54.;
const ROW_HEIGHT: f32 = 44.;

fn family_key(family: Family) -> &'static str {
    match family {
        Family::Jpeg => k::RULES_FAMILY_JPEG,
        Family::Raw => k::RULES_FAMILY_RAW,
        Family::Video => k::RULES_FAMILY_VIDEO,
        Family::Pdf => k::RULES_FAMILY_PDF,
        Family::Eps => k::RULES_FAMILY_EPS,
    }
}

fn group_key(group: Group) -> &'static str {
    match group {
        Group::StripAll => k::RULES_COL_STRIP_ALL,
        Group::PreserveIcc => k::RULES_COL_PRESERVE_ICC,
        Group::StripExif => k::RULES_COL_STRIP_EXIF,
        Group::StripXmp => k::RULES_COL_STRIP_XMP,
        Group::StripIptc => k::RULES_COL_STRIP_IPTC,
        Group::StripTime => k::RULES_COL_STRIP_TIME,
    }
}

/// rules crate 的 Notice 只出英文；展示层按键翻译，LoadWarning 保留 crate 的具体原因。
fn notice_text(app: &AppView, notice: &Notice) -> String {
    match notice {
        Notice::LoadWarning(message) => message.clone(),
        Notice::Saved => app.t(k::TOAST_RULES_SAVED),
        Notice::Reset => app.t(k::TOAST_RULES_RESET),
        Notice::SaveFailed(error) => app.tr(k::RULES_SAVE_FAILED, &[error]),
        Notice::ResetFailed(error) => app.tr(k::RULES_RESET_FAILED, &[error]),
    }
}

pub(crate) fn render(app: &AppView, theme: &Theme, cx: &mut Context<AppView>) -> Div {
    let editor = app.rules.borrow();

    let header = div()
        .flex()
        .mirror_row(app.rtl())
        .items_center()
        .justify_between()
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
                        .child(app.t(k::NAV_RULES)),
                )
                .child(
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text_muted)
                        .child(app.t(k::RULES_PANEL_SUBTITLE)),
                ),
        )
        .child(
            div()
                .flex()
                .items_center()
                .gap(px(8.))
                .when(editor.dirty(), |element| {
                    element.child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.warning)
                            .child(app.t(k::RULES_UNSAVED)),
                    )
                })
                .when_some(editor.notice(), |element, notice| {
                    element.child(
                        div()
                            .text_size(px(11.))
                            .text_color(notice_color(notice, theme))
                            .child(notice_text(app, notice)),
                    )
                })
                .child(save_button(app, cx, theme))
                .child(reset_button(app, cx, theme)),
        );

    let expert_card = div()
        .flex()
        .flex_col()
        .gap(px(8.))
        .p(px(12.))
        .bg(theme.surface)
        .border_1()
        .border_color(theme.border)
        .rounded(px(10.))
        .child(
            div()
                .id("rules-expert")
                .flex()
                .mirror_row(app.rtl())
                .items_center()
                .gap(px(8.))
                .cursor_pointer()
                .on_click(cx.listener(|this, _, _, cx| {
                    let next = !this.rules.borrow().expert_mode();
                    this.rules.borrow_mut().set_expert_mode(next);
                    cx.notify();
                }))
                .child(checkbox(editor.expert_mode(), theme))
                .child(
                    div()
                        .text_size(px(12.))
                        .font_weight(FontWeight::MEDIUM)
                        .child(app.t(k::RULES_EXPERT_MODE)),
                )
                .child(
                    div()
                        .text_size(px(11.))
                        .text_color(theme.text_muted)
                        .child(app.t(k::RULES_EXPERT_HIDES_NOTE)),
                ),
        )
        .when(!editor.expert_mode(), |element| {
            element.child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(6.))
                    .px(px(8.))
                    .py(px(6.))
                    .rounded(px(7.))
                    .bg(theme.surface_raised)
                    .text_size(px(11.))
                    .text_color(theme.warning)
                    .child(app.t(k::RULES_EXPERT_INFOBAR)),
            )
        });

    div()
        .flex_1()
        .flex()
        .flex_col()
        .gap(px(12.))
        .px(px(24.))
        .pb(px(16.))
        .pt(px(4.))
        .child(header)
        .child(expert_card)
        .child(table(app, &editor, theme, cx))
        .child(
            div()
                .text_size(px(11.))
                .text_color(theme.text_faint)
                .child(app.tr(
                    k::RULES_STORED_IN,
                    &[&editor.store_path().display().to_string()],
                )),
        )
}

fn table(app: &AppView, editor: &RulesEditor, theme: &Theme, cx: &mut Context<AppView>) -> Div {
    div()
        .flex()
        .flex_col()
        .bg(theme.surface)
        .border_1()
        .border_color(theme.border)
        .rounded(px(12.))
        .overflow_hidden()
        .child(table_header(app, theme))
        .children(
            Family::ALL
                .into_iter()
                .map(|family| family_row(app, family, editor, theme, cx)),
        )
}

fn table_header(app: &AppView, theme: &Theme) -> Div {
    div()
        .flex()
        .mirror_row(app.rtl())
        .items_center()
        .px(px(12.))
        .h(px(30.))
        .bg(theme.surface_raised)
        .border_b_1()
        .border_color(theme.border)
        .text_size(px(10.))
        .text_color(theme.text_faint)
        .child(div().w(px(FAMILY_WIDTH)).child(app.t(k::RULES_COL_FAMILY)))
        .child(
            div()
                .w(px(EXTENSIONS_WIDTH))
                .child(app.t(k::RULES_COL_EXTENSIONS)),
        )
        .children(Group::ALL.into_iter().map(|group| {
            div()
                .w(px(TOGGLE_WIDTH))
                .flex()
                .justify_center()
                .child(app.t(group_key(group)))
        }))
        .child(div().flex_1().child(app.t(k::RULES_COL_WARNING)))
}

fn family_row(
    app: &AppView,
    family: Family,
    editor: &RulesEditor,
    theme: &Theme,
    cx: &mut Context<AppView>,
) -> Div {
    let mut row = div()
        .flex()
        .mirror_row(app.rtl())
        .items_center()
        .px(px(12.))
        .h(px(ROW_HEIGHT))
        .border_b_1()
        .border_color(theme.border)
        .child(
            div()
                .w(px(FAMILY_WIDTH))
                .text_size(px(12.))
                .font_weight(FontWeight::MEDIUM)
                .child(app.t(family_key(family))),
        )
        .child(
            div()
                .w(px(EXTENSIONS_WIDTH))
                .text_size(px(11.))
                .text_color(theme.text_muted)
                .child(family.extensions()),
        );

    for group in Group::ALL {
        let checked = editor.rules().get(family, group);
        row = row.child(
            div()
                .id(format!("rule-{}-{}", family.key(), group.key()))
                .w(px(TOGGLE_WIDTH))
                .flex()
                .items_center()
                .justify_center()
                .cursor_pointer()
                .on_click(cx.listener(move |this, _, _, cx| {
                    this.rules.borrow_mut().toggle(family, group);
                    cx.notify();
                }))
                .child(checkbox(checked, theme)),
        );
    }

    row.child(warning_cell(app, family, theme))
}

fn warning_cell(app: &AppView, family: Family, theme: &Theme) -> Div {
    div()
        .flex_1()
        .flex()
        .items_center()
        .text_size(px(11.))
        .when(family.requires_warning(), |element| {
            element.child(
                div()
                    .px(px(6.))
                    .py(px(1.))
                    .rounded(px(4.))
                    .bg(theme.surface_raised)
                    .text_color(theme.warning)
                    .child(app.t(k::RULES_CAUTION)),
            )
        })
}

fn checkbox(checked: bool, theme: &Theme) -> Div {
    div()
        .size(px(16.))
        .flex()
        .items_center()
        .justify_center()
        .rounded(px(4.))
        .border_1()
        .border_color(if checked { theme.accent } else { theme.border })
        .bg(if checked { theme.accent } else { theme.surface })
        .text_size(px(10.))
        .text_color(theme.on_accent)
        .when(checked, |element| element.child("✓"))
}

fn save_button(app: &AppView, cx: &mut Context<AppView>, theme: &Theme) -> impl IntoElement {
    div()
        .id("rules-save")
        .px(px(12.))
        .py(px(5.))
        .rounded(px(7.))
        .bg(theme.accent)
        .text_color(theme.on_accent)
        .text_size(px(12.))
        .font_weight(FontWeight::MEDIUM)
        .cursor_pointer()
        .hover(|element| element.opacity(0.9))
        .on_click(cx.listener(|this, _, _, cx| {
            this.rules.borrow_mut().save();
            cx.notify();
        }))
        .child(app.t(k::RULES_BTN_SAVE))
}

fn reset_button(app: &AppView, cx: &mut Context<AppView>, theme: &Theme) -> impl IntoElement {
    div()
        .id("rules-reset")
        .px(px(12.))
        .py(px(5.))
        .rounded(px(7.))
        .bg(theme.surface_raised)
        .text_color(theme.text)
        .text_size(px(12.))
        .cursor_pointer()
        .hover(|element| element.bg(theme.surface))
        .on_click(cx.listener(|this, _, _, cx| {
            this.rules.borrow_mut().reset();
            cx.notify();
        }))
        .child(app.t(k::RULES_BTN_RESET))
}

fn notice_color(notice: &Notice, theme: &Theme) -> Rgba {
    match notice {
        Notice::SaveFailed(_) | Notice::ResetFailed(_) => theme.danger,
        Notice::LoadWarning(_) => theme.warning,
        Notice::Saved | Notice::Reset => theme.success,
    }
}
