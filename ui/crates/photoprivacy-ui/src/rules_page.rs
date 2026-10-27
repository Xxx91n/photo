//! 规则页：五族 × 六开关（rules.json 的全部持久化面）。
//! 旧 UI 的 Enabled 列不落盘、Worker 不消费；搜索与 effective-args 预览不在 #56 的 AC 内，
//! 本页不复刻（预览若做必须由 C# WipeRuleEngine 出值，避免双真相源）。

use crate::app::AppView;
use crate::theme::Theme;
use gpui::prelude::*;
use gpui::{Context, Div, FontWeight, IntoElement, Rgba, div, px};
use photoprivacy_rules::{Family, Group, Notice, RulesEditor};

const FAMILY_WIDTH: f32 = 90.;
const EXTENSIONS_WIDTH: f32 = 170.;
const TOGGLE_WIDTH: f32 = 54.;
const ROW_HEIGHT: f32 = 44.;

pub(crate) fn render(app: &AppView, theme: &Theme, cx: &mut Context<AppView>) -> Div {
    let editor = app.rules.borrow();

    let header =
        div()
            .flex()
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
                            .child("Rules"),
                    )
                    .child(div().text_size(px(12.)).text_color(theme.text_muted).child(
                        "Format families and six metadata toggles · shared config/rules.json",
                    )),
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
                                .child("Unsaved changes"),
                        )
                    })
                    .when_some(editor.notice(), |element, notice| {
                        element.child(
                            div()
                                .text_size(px(11.))
                                .text_color(notice_color(notice, theme))
                                .child(notice.text()),
                        )
                    })
                    .child(save_button(cx, theme))
                    .child(reset_button(cx, theme)),
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
                        .child("Expert mode"),
                )
                .child(
                    div()
                        .text_size(px(11.))
                        .text_color(theme.text_muted)
                        .child("· hides the safety note for Video / PDF / EPS"),
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
                    .child(
                        "Video, PDF and EPS can lose data. PDF never truly deletes metadata; \
                         EPS native tags cannot be removed. Review these rows before saving.",
                    ),
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
        .child(table(&editor, theme, cx))
        .child(
            div()
                .text_size(px(11.))
                .text_color(theme.text_faint)
                .child(format!("Stored in {}", editor.store_path().display())),
        )
}

fn table(editor: &RulesEditor, theme: &Theme, cx: &mut Context<AppView>) -> Div {
    div()
        .flex()
        .flex_col()
        .bg(theme.surface)
        .border_1()
        .border_color(theme.border)
        .rounded(px(12.))
        .overflow_hidden()
        .child(table_header(theme))
        .children(
            Family::ALL
                .into_iter()
                .map(|family| family_row(family, editor, theme, cx)),
        )
}

fn table_header(theme: &Theme) -> Div {
    div()
        .flex()
        .items_center()
        .px(px(12.))
        .h(px(30.))
        .bg(theme.surface_raised)
        .border_b_1()
        .border_color(theme.border)
        .text_size(px(10.))
        .text_color(theme.text_faint)
        .child(div().w(px(FAMILY_WIDTH)).child("FAMILY"))
        .child(div().w(px(EXTENSIONS_WIDTH)).child("EXTENSIONS"))
        .children(Group::ALL.into_iter().map(|group| {
            div()
                .w(px(TOGGLE_WIDTH))
                .flex()
                .justify_center()
                .child(group.short_label())
        }))
        .child(div().flex_1().child("WARNING"))
}

fn family_row(
    family: Family,
    editor: &RulesEditor,
    theme: &Theme,
    cx: &mut Context<AppView>,
) -> Div {
    let mut row = div()
        .flex()
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
                .child(family.label()),
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

    row.child(warning_cell(family, theme))
}

fn warning_cell(family: Family, theme: &Theme) -> Div {
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
                    .child("Caution"),
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

fn save_button(cx: &mut Context<AppView>, theme: &Theme) -> impl IntoElement {
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
        .child("Save")
}

fn reset_button(cx: &mut Context<AppView>, theme: &Theme) -> impl IntoElement {
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
        .child("Reset")
}

fn notice_color(notice: &Notice, theme: &Theme) -> Rgba {
    match notice {
        Notice::SaveFailed(_) | Notice::ResetFailed(_) => theme.danger,
        Notice::LoadWarning(_) => theme.warning,
        Notice::Saved | Notice::Reset => theme.success,
    }
}
