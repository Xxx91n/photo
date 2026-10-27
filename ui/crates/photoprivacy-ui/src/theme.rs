use crate::config::{THEME_VARIANTS, normalize_theme_variant};
use gpui::{Rgba, WindowAppearance, rgb};

/// 浅色主题强调色，也是托盘图标的取色出处（避免两处写死同一个十六进制值）。
pub const ACCENT_RGB: u32 = 0x2F6FEB;

/// 主题选择：默认跟随系统，允许强制 light/dark。`ui.theme_variant` 是持久化形态。
#[repr(usize)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ThemeMode {
    System = 0,
    Light = 1,
    Dark = 2,
}

impl ThemeMode {
    pub fn label(self) -> &'static str {
        match self {
            ThemeMode::System => "System",
            ThemeMode::Light => "Light",
            ThemeMode::Dark => "Dark",
        }
    }

    /// 写回 config.json 的 `ui.theme_variant` 值（旧 UI 的取值域）。
    pub fn variant(self) -> &'static str {
        THEME_VARIANTS[self as usize]
    }

    pub fn from_variant(value: &str) -> Self {
        let normalized = normalize_theme_variant(value);
        if normalized == THEME_VARIANTS[1] {
            ThemeMode::Light
        } else if normalized == THEME_VARIANTS[2] {
            ThemeMode::Dark
        } else {
            ThemeMode::System
        }
    }

    pub fn all() -> [ThemeMode; 3] {
        [ThemeMode::System, ThemeMode::Light, ThemeMode::Dark]
    }
}

pub fn is_dark(appearance: WindowAppearance) -> bool {
    matches!(
        appearance,
        WindowAppearance::Dark | WindowAppearance::VibrantDark
    )
}

/// 中性色占主导、强调色只用于连接状态的浅/深两套 token（#49 T4 审美方向：排版先行、弱边框）。
#[derive(Clone, Copy)]
pub struct Theme {
    pub bg: Rgba,
    pub surface: Rgba,
    pub surface_raised: Rgba,
    pub border: Rgba,
    pub text: Rgba,
    pub text_muted: Rgba,
    pub text_faint: Rgba,
    pub accent: Rgba,
    /// 实心强调色/危险色按钮上的前景色；深色主题的浅强调色用深底文字保对比度。
    pub on_accent: Rgba,
    pub success: Rgba,
    pub warning: Rgba,
    pub danger: Rgba,
}

impl Theme {
    pub fn light() -> Self {
        Self {
            bg: rgb(0xFAFAF9),
            surface: rgb(0xFFFFFF),
            surface_raised: rgb(0xF2F2F0),
            border: rgb(0xE4E4E0),
            text: rgb(0x1A1A19),
            text_muted: rgb(0x6B6B66),
            text_faint: rgb(0x9A9A94),
            accent: rgb(ACCENT_RGB),
            on_accent: rgb(0xFFFFFF),
            success: rgb(0x1F8A5F),
            warning: rgb(0xB7791F),
            danger: rgb(0xC0392B),
        }
    }

    pub fn dark() -> Self {
        Self {
            bg: rgb(0x111110),
            surface: rgb(0x191918),
            surface_raised: rgb(0x212120),
            border: rgb(0x2E2E2C),
            text: rgb(0xECECEA),
            text_muted: rgb(0x9B9B95),
            text_faint: rgb(0x6E6E69),
            accent: rgb(0x6E9BFF),
            on_accent: rgb(0x111110),
            success: rgb(0x4CC38A),
            warning: rgb(0xD8A657),
            danger: rgb(0xE5726A),
        }
    }

    pub fn resolve(mode: ThemeMode, system_dark: bool) -> Self {
        let dark = match mode {
            ThemeMode::System => system_dark,
            ThemeMode::Light => false,
            ThemeMode::Dark => true,
        };
        if dark { Self::dark() } else { Self::light() }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn system_mode_follows_the_os_appearance() {
        // 防：Vibrant* 变体被当成另一种外观，系统深浅色切换后界面不跟随（#54）。
        assert!(is_dark(WindowAppearance::Dark));
        assert!(is_dark(WindowAppearance::VibrantDark));
        assert!(!is_dark(WindowAppearance::Light));
        assert!(!is_dark(WindowAppearance::VibrantLight));
    }

    #[test]
    fn explicit_mode_overrides_the_os_appearance() {
        // 防：强制 Light/Dark 被系统外观覆盖（或写反），切换按钮点下去没有效果（#54）。
        let forced_light = Theme::resolve(ThemeMode::Light, true);
        let forced_dark = Theme::resolve(ThemeMode::Dark, false);
        assert_eq!(forced_light.bg, Theme::light().bg);
        assert_eq!(forced_dark.bg, Theme::dark().bg);
        assert_eq!(Theme::resolve(ThemeMode::System, true).bg, Theme::dark().bg);
        assert_eq!(
            Theme::resolve(ThemeMode::System, false).bg,
            Theme::light().bg
        );
    }

    #[test]
    fn theme_variant_round_trips_through_config_values() {
        // 防：界面显示 Dark 但 config.json 写成 "Dark"/"default"，旧 UI 读回后回落到 system（#55）。
        for mode in ThemeMode::all() {
            assert_eq!(ThemeMode::from_variant(mode.variant()), mode);
        }
        assert_eq!(ThemeMode::from_variant("DARK"), ThemeMode::Dark);
        assert_eq!(ThemeMode::from_variant("garbage"), ThemeMode::System);
    }
}
