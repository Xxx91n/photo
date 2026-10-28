//! 系统托盘：原生菜单 + 左键唤起 + 退出。
//!
//! gpui 没有内建托盘（#49 T4 已接受自建/第三方），这里用 Tauri 的 `tray-icon`：
//! Windows/macOS 走系统原生菜单，Linux/BSD 走 ksni（StatusNotifierItem）避免引入 GTK 系统依赖。
//! 菜单文案经 i18n 目录取词（#60），语言切换后在下一次事件轮询时原位改写菜单文本。

use crate::i18n::{Catalog, keys as k};
use photoprivacy_service::ServiceRuntimeState;
use std::rc::Rc;
use tray_icon::menu::{Menu, MenuEvent, MenuId, MenuItem, PredefinedMenuItem};
use tray_icon::{Icon, MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};

const ICON_SIZE: u32 = 32;
// 与浅色主题强调色同源，浅/深色任务栏上都可见。
const ICON_COLOR: [u8; 4] = [
    (crate::theme::ACCENT_RGB >> 16) as u8,
    (crate::theme::ACCENT_RGB >> 8) as u8,
    crate::theme::ACCENT_RGB as u8,
    0xFF,
];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TrayCommand {
    ShowWindow,
    Exit,
}

pub struct TrayHandle {
    icon: TrayIcon,
    open_window_id: MenuId,
    exit_id: MenuId,
    open_window: MenuItem,
    exit: MenuItem,
    i18n: Rc<Catalog>,
    locale: String,
}

/// 旧 UI `TrayPolicy.ShouldShowTrayIcon(isBackgroundMode, isServiceInstalled)` 的对等实现：
/// 只有后台模式且服务未安装时才显示托盘。
///
/// 新 UI 没有独立的「后台模式」状态，由安装态反推：服务已安装（含探测失败归入的 `Unknown`）
/// 即服务模式。这样服务已安装但未运行（Stopped/pending）时也不会把托盘留在服务模式里。
// Design(#49): 托盘只在后台模式且服务未安装时显示；服务已安装（含 Unknown）不得留下托盘。
pub fn should_show_tray_icon(state: ServiceRuntimeState) -> bool {
    !state.is_installed()
}

/// 托盘创建失败时返回 `None`：窗口保持常显、关窗即退出，绝不出现「窗口藏了但托盘没建起来」
/// 的无入口状态（旧 UI 用 `trayReady` 表达同一约束）。
///
/// `show_tray_icon` 由 [`should_show_tray_icon`] 与 `ui.hide_tray_icon` 共同决定。
// Design(#49): 运行期安装/卸载服务不重建托盘（旧 ServiceModeController 的模式切换编排不迁移，对等核销见 #61）。
pub fn install(show_tray_icon: bool, i18n: Rc<Catalog>, locale: &str) -> Option<TrayHandle> {
    if !show_tray_icon {
        return None;
    }

    match TrayHandle::build(i18n, locale) {
        Ok(handle) => Some(handle),
        Err(err) => {
            eprintln!("[photoprivacy-ui] 托盘创建失败，窗口保持常显: {err}");
            None
        }
    }
}

/// 与旧 UI `MainWindowRuntimePolicy.ShouldHideOnStartup` 逐字对齐：勾选「启动时隐藏」、
/// 运行模式允许托盘、配置未关闭托盘、且托盘真的建起来时，才不建窗口。
/// 服务模式下 `use_tray_icon` 为 false，窗口必须出现，否则用户没有任何入口。
// Design(#49): 静默启动要求托盘实际可用且未被配置关闭；服务模式与托盘创建失败都必须显式出窗。
pub fn should_start_hidden(
    hide_main_window_on_startup: bool,
    use_tray_icon: bool,
    hide_tray_icon: bool,
    tray_icon_ready: bool,
) -> bool {
    hide_main_window_on_startup && use_tray_icon && !hide_tray_icon && tray_icon_ready
}

impl TrayHandle {
    /// 构建托盘图标、原生菜单与左键/退出命令。
    // Design(#49): 托盘文案归新栈 i18n（#60 落地后存续出处随换栈主线）；语言切换经 `poll` 原位改写。
    fn build(i18n: Rc<Catalog>, locale: &str) -> anyhow::Result<Self> {
        // 旧菜单还有 Pause/Resume；本票 AC 只要求显示/退出，Worker 暂停/恢复控制不在本票范围，
        // 对等核销时需在 #61 的清单里显式处置。
        let open_window = MenuItem::new(i18n.get(locale, k::TRAY_OPEN_WINDOW), true, None);
        let exit = MenuItem::new(i18n.get(locale, k::TRAY_EXIT), true, None);
        let menu = Menu::new();
        menu.append_items(&[&open_window, &PredefinedMenuItem::separator(), &exit])?;

        let icon = TrayIconBuilder::new()
            .with_tooltip(i18n.get(locale, k::TRAY_TOOLTIP))
            .with_menu(Box::new(menu))
            .with_icon(dot_icon()?)
            // 旧 UI 左键唤起窗口、右键出菜单；默认左键也会弹菜单，这里关掉。
            .with_menu_on_left_click(false)
            .build()?;

        Ok(Self {
            icon,
            open_window_id: open_window.id().clone(),
            exit_id: exit.id().clone(),
            open_window,
            exit,
            i18n,
            locale: locale.to_string(),
        })
    }

    /// 排空托盘与菜单事件；退出优先于唤起，避免同一拍里既重建窗口又退出。
    /// 语言与上次构建不同时，原位改写菜单文本与工具提示（托盘不随语言切换重建）。
    pub fn poll(&mut self, locale: &str) -> Option<TrayCommand> {
        if locale != self.locale {
            self.locale = locale.to_string();
            self.open_window
                .set_text(self.i18n.get(locale, k::TRAY_OPEN_WINDOW));
            self.exit.set_text(self.i18n.get(locale, k::TRAY_EXIT));
            let _ = self
                .icon
                .set_tooltip(Some(self.i18n.get(locale, k::TRAY_TOOLTIP)));
        }

        let mut command = None;
        while let Ok(event) = TrayIconEvent::receiver().try_recv() {
            if matches!(
                event,
                TrayIconEvent::Click {
                    button: MouseButton::Left,
                    button_state: MouseButtonState::Up,
                    ..
                }
            ) {
                command = Some(TrayCommand::ShowWindow);
            }
        }

        while let Ok(event) = MenuEvent::receiver().try_recv() {
            if event.id == self.exit_id {
                return Some(TrayCommand::Exit);
            }
            if event.id == self.open_window_id {
                command = Some(TrayCommand::ShowWindow);
            }
        }

        command
    }

    /// 移除托盘图标（退出前调用，避免通知区域残留）。
    pub fn hide(&self) {
        let _ = self.icon.set_visible(false);
    }
}

/// 由强调色生成 32px 圆点位图并交给 tray-icon。
fn dot_icon() -> anyhow::Result<Icon> {
    let (rgba, size) = dot_rgba();
    Ok(Icon::from_rgba(rgba, size, size)?)
}

/// 16/24/32px 托盘图标共用一张 32px 位图，由系统缩放；透明背景 + 实心圆点。
fn dot_rgba() -> (Vec<u8>, u32) {
    let mut rgba = vec![0u8; (ICON_SIZE * ICON_SIZE * 4) as usize];
    let center = (ICON_SIZE as f32 - 1.0) / 2.0;
    let radius = ICON_SIZE as f32 * 0.38;

    for y in 0..ICON_SIZE {
        for x in 0..ICON_SIZE {
            let dx = x as f32 - center;
            let dy = y as f32 - center;
            if dx * dx + dy * dy <= radius * radius {
                let offset = ((y * ICON_SIZE + x) * 4) as usize;
                rgba[offset..offset + 4].copy_from_slice(&ICON_COLOR);
            }
        }
    }

    (rgba, ICON_SIZE)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tray_shows_only_while_no_service_is_installed() {
        // 旧 UI `TrayPolicyTests` 三条的真值表：没装服务才允许后台托盘（#58 承接项）。
        assert!(should_show_tray_icon(ServiceRuntimeState::NotInstalled));
        assert!(!should_show_tray_icon(ServiceRuntimeState::Running));
        assert!(!should_show_tray_icon(ServiceRuntimeState::Stopped));
    }

    #[test]
    fn every_installed_service_state_suppresses_the_tray() {
        // 防：服务已安装但未运行（Stopped/pending）或探测失败（Unknown）时仍留托盘，
        // 用户误以为 UI 与 Worker 同生共死（#58；与旧 UI `ServiceExists` 语义一致）。
        for state in [
            ServiceRuntimeState::Running,
            ServiceRuntimeState::StartPending,
            ServiceRuntimeState::PausePending,
            ServiceRuntimeState::Paused,
            ServiceRuntimeState::ContinuePending,
            ServiceRuntimeState::Stopped,
            ServiceRuntimeState::StopPending,
            ServiceRuntimeState::Unknown,
        ] {
            assert!(state.is_installed(), "{state:?} 必须按已安装处理");
            assert!(!should_show_tray_icon(state), "{state:?} 不允许显示托盘");
        }

        assert!(!ServiceRuntimeState::NotInstalled.is_installed());
        assert!(should_show_tray_icon(ServiceRuntimeState::NotInstalled));
    }

    #[test]
    fn silent_start_requires_a_ready_tray_in_background_mode() {
        // 旧 `MainWindowRuntimePolicy` 真值表：服务模式、托盘未建、配置关闭托盘、未勾选静默启动，
        // 任一不满足都必须出窗，否则用户失去唯一入口（#59/#58）。
        assert!(should_start_hidden(true, true, false, true));
        assert!(!should_start_hidden(true, false, false, true));
        assert!(!should_start_hidden(true, true, false, false));
        assert!(!should_start_hidden(true, true, true, true));
        assert!(!should_start_hidden(false, true, false, true));
    }

    #[test]
    fn service_mode_never_hides_the_window() {
        // 组合门控：安装态 → 托盘可见性 → 静默启动。服务模式下即使勾选静默启动也必须出窗（#58）。
        for state in [
            ServiceRuntimeState::Running,
            ServiceRuntimeState::Stopped,
            ServiceRuntimeState::Unknown,
        ] {
            assert!(
                !should_start_hidden(true, should_show_tray_icon(state), false, true),
                "{state:?} 不允许静默启动"
            );
        }
        assert!(should_start_hidden(
            true,
            should_show_tray_icon(ServiceRuntimeState::NotInstalled),
            false,
            true
        ));
    }

    #[test]
    fn tray_icon_is_a_transparent_cornered_dot() {
        // 防：RGBA 长度与尺寸不符导致 tray-icon 拒绝图标，或整张图透明让任务栏上看不见（#59）。
        let (rgba, size) = dot_rgba();
        assert_eq!(rgba.len(), (size * size * 4) as usize);

        let center = ((size / 2 * size + size / 2) * 4) as usize;
        assert_eq!(rgba[center + 3], 0xFF, "圆心必须不透明");
        assert_eq!(rgba[3], 0x00, "四角必须透明");
    }
}
