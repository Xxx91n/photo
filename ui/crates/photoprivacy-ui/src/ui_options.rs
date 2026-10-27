//! 只读取驻守行为需要的 `ui.*` 开关。
//!
//! 配置页的完整读写、防抖保存与热重载归 #55；本模块刻意不复用那里的实现，
//! 只按 `config/config.sample.json` 的键名读两个布尔值，文件缺失或损坏时回落到
//! `AppConfig.Default`（false/false），与旧 UI `LoadConfigOrDefault` 的兜底一致。

use serde_json::Value;
use std::path::Path;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct UiOptions {
    pub hide_main_window_on_startup: bool,
    pub hide_tray_icon: bool,
}

pub fn load(path: Option<&Path>) -> UiOptions {
    path.and_then(|path| std::fs::read_to_string(path).ok())
        .map(|text| parse(&text))
        .unwrap_or_default()
}

// Design(#55): 这里只认驻守相关的两个开关，配置页的读写/热重载由 #55 接管。
fn parse(text: &str) -> UiOptions {
    // Windows 编辑器/脚本常写 UTF-8 BOM；serde_json 不认 BOM，先剥掉再解析。
    let text = text.strip_prefix('\u{feff}').unwrap_or(text);
    let Ok(json) = serde_json::from_str::<Value>(text) else {
        return UiOptions::default();
    };

    UiOptions {
        hide_main_window_on_startup: read_bool(&json, "/ui/hide_main_window_on_startup"),
        hide_tray_icon: read_bool(&json, "/ui/hide_tray_icon"),
    }
}

fn read_bool(json: &Value, pointer: &str) -> bool {
    json.pointer(pointer)
        .and_then(Value::as_bool)
        .unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_resident_flags_from_config_json() {
        // 防：键名/层级漂移后静默启动与托盘开关失效（config.sample.json 的 ui.* 契约，#59）。
        let options = parse(
            r#"{
                "ui": {
                    "hide_main_window_on_startup": true,
                    "hide_tray_icon": true
                }
            }"#,
        );
        assert!(options.hide_main_window_on_startup);
        assert!(options.hide_tray_icon);
    }

    #[test]
    fn utf8_bom_prefixed_config_is_accepted() {
        // 防：用资源管理器/记事本存出的带 BOM config.json 被当成损坏文件，静默启动设置失效（#59）。
        let options = parse(
            "\u{feff}{\"ui\":{\"hide_main_window_on_startup\":true,\"hide_tray_icon\":false}}",
        );
        assert!(options.hide_main_window_on_startup);
        assert!(!options.hide_tray_icon);
    }

    #[test]
    fn missing_ui_section_falls_back_to_visible_window_and_tray() {
        // 防：旧配置没有 ui 段时被当成「隐藏窗口」，让用户启动后找不到入口（#59）。
        let options = parse(r#"{"schema_version": 2}"#);
        assert_eq!(options, UiOptions::default());
    }

    #[test]
    fn malformed_json_does_not_block_startup() {
        // 防：手改坏 config.json 后 UI 直接拒绝启动；旧 UI 是回落默认值继续跑（#59）。
        assert_eq!(parse("{ not json"), UiOptions::default());
    }

    #[test]
    fn non_boolean_values_fall_back_to_default() {
        // 防：把 "true"/1 之类的弱类型值静默当成 true（与 .NET 读取器的严格布尔语义不一致）。
        let options = parse(r#"{"ui":{"hide_main_window_on_startup":"true","hide_tray_icon":1}}"#);
        assert_eq!(options, UiOptions::default());
    }
}
