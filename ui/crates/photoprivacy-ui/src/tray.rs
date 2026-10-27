//! 系统托盘：原生菜单 + 左键唤起 + 退出。
//!
//! gpui 没有内建托盘（#49 T4 已接受自建/第三方），这里用 Tauri 的 `tray-icon`：
//! Windows/macOS 走系统原生菜单，Linux/BSD 走 ksni（StatusNotifierItem）避免引入 GTK 系统依赖。
//! 菜单文案暂为英文，10 语言接入归 #60。

use tray_icon::menu::{Menu, MenuEvent, MenuId, MenuItem, PredefinedMenuItem};
use tray_icon::{Icon, MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};

const TOOLTIP: &str = "PhotoPrivacy";
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
}

/// 托盘创建失败时返回 `None`：窗口保持常显、关窗即退出，绝不出现「窗口藏了但托盘没建起来」
/// 的无入口状态（旧 UI 用 `trayReady` 表达同一约束）。
///
/// 旧 UI 的 `TrayPolicy` 只在「后台模式且服务未安装」时显示托盘；安装态探测与服务模式切换归 #58，
/// 本版按配置常驻（服务模式也显示托盘），#58 补齐探测后收敛。
// Design(#58): 托盘可见性暂按配置常驻；服务安装态探测与模式切换归 #58。
pub fn install(hide_tray_icon: bool) -> Option<TrayHandle> {
    if hide_tray_icon {
        return None;
    }

    match TrayHandle::build() {
        Ok(handle) => Some(handle),
        Err(err) => {
            eprintln!("[photoprivacy-ui] 托盘创建失败，窗口保持常显: {err}");
            None
        }
    }
}

/// 与旧 UI `MainWindowRuntimePolicy.ShouldHideOnStartup` 的真值表对齐：只有勾选「启动时隐藏」
/// 且托盘真的可用时才不建窗口（`UseTrayIcon && !HideTrayIcon && trayIconReady`）。
///
/// 旧 UI 还要求运行模式为 tray（服务模式从不隐藏窗口）；服务壳与安装态探测归 #58，
/// 本版在托盘可用时即允许隐藏，因此服务模式下也会隐藏——#58 收敛。
pub fn should_start_hidden(hide_main_window_on_startup: bool, tray_available: bool) -> bool {
    hide_main_window_on_startup && tray_available
}

impl TrayHandle {
    fn build() -> anyhow::Result<Self> {
        // 旧菜单还有 Pause/Resume；本票 AC 只要求显示/退出，Worker 暂停/恢复控制不在本票范围。
        let open_window = MenuItem::new("Open Window", true, None);
        let exit = MenuItem::new("Exit", true, None);
        let menu = Menu::new();
        menu.append_items(&[&open_window, &PredefinedMenuItem::separator(), &exit])?;

        let icon = TrayIconBuilder::new()
            .with_tooltip(TOOLTIP)
            .with_menu(Box::new(menu))
            .with_icon(dot_icon()?)
            // 旧 UI 左键唤起窗口、右键出菜单；默认左键也会弹菜单，这里关掉。
            .with_menu_on_left_click(false)
            .build()?;

        Ok(Self {
            icon,
            open_window_id: open_window.id().clone(),
            exit_id: exit.id().clone(),
        })
    }

    /// 排空托盘与菜单事件；退出优先于唤起，避免同一拍里既重建窗口又退出。
    pub fn poll(&self) -> Option<TrayCommand> {
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

    pub fn hide(&self) {
        let _ = self.icon.set_visible(false);
    }
}

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
    fn silent_start_requires_a_tray() {
        // 防：托盘没建起来（hide_tray_icon 或创建失败）仍隐藏窗口，用户失去唯一入口（#59）。
        assert!(should_start_hidden(true, true));
        assert!(!should_start_hidden(true, false));
        assert!(!should_start_hidden(false, true));
        assert!(!should_start_hidden(false, false));
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
