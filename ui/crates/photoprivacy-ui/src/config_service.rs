//! 配置页的后台服务：防抖保存 → Worker 热重载 → 外部变更回灌。
//!
//! 线程边界：gpui UI 线程只收发消息，文件 IO 与 IPC 都在 tokio 运行时里。
//! 行为对齐旧 UI 的 `ScheduleDebouncedConfigApply` + `ConfigEditor` + `ConfigFileWatcher`：
//! - 每次编辑重置 500ms 防抖窗口；
//! - 防抖到期后原子写 config.json，再调 `ReloadConfig`；
//! - Worker 不可用时仍保存成功，只把「未热重载」如实回报（旧 UI 的 `msg.auto_saved_worker_down`）；
//! - 外部写入（手改/其他工具）经轮询指纹发现后回灌 UI；自身写入不会反弹。
//!
//! 旧 UI 用 FileSystemWatcher；这里用 500ms 内容指纹轮询取得同样的用户可见行为，
//! 且不引入平台 watcher 依赖。

use crate::config::{AppConfig, default_backup_directory_for};
use crate::config_file::ConfigFile;
use photoprivacy_ipc::{Endpoint, IpcClient};
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{mpsc, watch};
use tokio::time::{Instant, MissedTickBehavior, sleep_until};

// Design(#55): 配置保存防抖 500ms，与旧 UI ConfigFileWatcher/ConfigEditor 同窗口。
pub const SAVE_DEBOUNCE: Duration = Duration::from_millis(500);
pub const EXTERNAL_POLL_INTERVAL: Duration = Duration::from_millis(500);

#[derive(Debug, Clone)]
pub enum ConfigCommand {
    Edit(Box<AppConfig>),
}

#[derive(Debug, Clone, PartialEq)]
pub enum ConfigEvent {
    /// 启动时的首次加载。
    Loaded(AppConfig),
    /// 磁盘被外部改写后回灌。
    ExternalReload(AppConfig),
    /// 外部文件存在但解析失败；UI 保留当前值并显示错误。
    ExternalLoadFailed(String),
    /// 写盘成功。`config` 是实际落盘的合并结果（含默认备份目录等读改写），UI 用它刷新显示；
    /// `worker_error=None` 表示 Worker 已重载，`Some` 说明配置已落盘但未热重载。
    Saved {
        config: Box<AppConfig>,
        worker_error: Option<String>,
    },
    /// 写盘本身失败。
    SaveFailed(String),
}

/// 启动后台服务；返回的 JoinHandle 由调用方持有（drop 后任务继续运行，直到命令通道关闭）。
pub fn spawn(
    runtime: &tokio::runtime::Handle,
    config_path: std::path::PathBuf,
    client: Arc<IpcClient>,
    endpoints: watch::Receiver<Option<Endpoint>>,
    commands: mpsc::UnboundedReceiver<ConfigCommand>,
    events: mpsc::UnboundedSender<ConfigEvent>,
) -> tokio::task::JoinHandle<()> {
    runtime.spawn(run(
        ConfigFile::new(config_path),
        client,
        endpoints,
        commands,
        events,
        SAVE_DEBOUNCE,
        EXTERNAL_POLL_INTERVAL,
    ))
}

#[allow(clippy::too_many_arguments)]
pub async fn run(
    file: ConfigFile,
    client: Arc<IpcClient>,
    endpoints: watch::Receiver<Option<Endpoint>>,
    mut commands: mpsc::UnboundedReceiver<ConfigCommand>,
    events: mpsc::UnboundedSender<ConfigEvent>,
    debounce: Duration,
    poll_interval: Duration,
) {
    let mut fingerprint = file.fingerprint();
    match file.load_or_default() {
        Ok(config) => {
            let _ = events.send(ConfigEvent::Loaded(config));
        }
        Err(err) => {
            let _ = events.send(ConfigEvent::ExternalLoadFailed(err.to_string()));
        }
    }

    let mut pending: Option<AppConfig> = None;
    let mut deadline: Option<Instant> = None;
    let mut ticker = tokio::time::interval(poll_interval);
    ticker.set_missed_tick_behavior(MissedTickBehavior::Delay);

    loop {
        let debounce_elapsed = async {
            match deadline {
                Some(deadline) => sleep_until(deadline).await,
                None => std::future::pending().await,
            }
        };

        tokio::select! {
            command = commands.recv() => {
                match command {
                    Some(ConfigCommand::Edit(config)) => {
                        pending = Some(*config);
                        deadline = Some(Instant::now() + debounce);
                    }
                    None => break,
                }
            }
            () = debounce_elapsed => {
                deadline = None;
                let Some(draft) = pending.take() else { continue };
                // 与旧 UI ConfigEditor.UpdateConfig 一致：写前重新读盘。文件坏了就拒绝覆盖，
                // 而不是用内存默认值把用户手改坏的配置抹掉。
                let disk = match file.load_or_default() {
                    Ok(disk) => disk,
                    Err(err) => {
                        let _ = events.send(ConfigEvent::SaveFailed(format!(
                            "config.json 当前内容无法解析，已拒绝覆盖: {err}"
                        )));
                        continue;
                    }
                };
                // 只覆盖配置页编辑的字段；rules/retry/exiftool 扩展参数等外部改动保留。
                let merged = merge_editable_fields(disk, &draft);
                match file.save(&merged) {
                    Ok(()) => {
                        // 先记下自身写入的指纹，轮询不会把这次写盘当成外部变更再回灌一次。
                        fingerprint = file.fingerprint();
                        // 先取出端点再 await：watch 的 Ref 不能跨 await（future 必须 Send）。
                        let endpoint = endpoints.borrow().clone();
                        let worker_error = match endpoint {
                            Some(endpoint) => client
                                .reload_config(&endpoint)
                                .await
                                .err()
                                .map(|err| err.to_string()),
                            None => Some("Worker 未连接，配置已保存但未热重载".to_string()),
                        };
                        let _ = events.send(ConfigEvent::Saved {
                            config: Box::new(merged),
                            worker_error,
                        });
                    }
                    Err(err) => {
                        let _ = events.send(ConfigEvent::SaveFailed(err.to_string()));
                    }
                }
            }
            _ = ticker.tick() => {
                let current = file.fingerprint();
                if current != fingerprint {
                    fingerprint = current;
                    // 文件被删除时保留当前值（旧 UI 的 FSW 也不监听 Deleted），
                    // 等文件被重新创建后再回灌。
                    if current.is_none() {
                        continue;
                    }
                    // 有待保存的编辑时以 UI 草稿为准，避免外部回灌覆盖用户刚输入的内容。
                    if pending.is_none() {
                        match file.load_or_default() {
                            Ok(config) => { let _ = events.send(ConfigEvent::ExternalReload(config)); }
                            Err(err) => { let _ = events.send(ConfigEvent::ExternalLoadFailed(err.to_string())); }
                        }
                    }
                }
            }
        }
    }
}

/// 把配置页编辑的字段覆盖到磁盘上的最新配置上，其余字段（rules/retry/ExifTool 扩展参数、
/// watch 内部参数、backup 保留策略等）保持磁盘值。旧 UI 的 `ConfigEditor.UpdateConfig` 是
/// 同样的 read-modify-write 形态，避免整份快照写回时丢掉外部改动。
fn merge_editable_fields(mut disk: AppConfig, draft: &AppConfig) -> AppConfig {
    disk.exiftool.path = draft.exiftool.path.clone();
    disk.watch.hot_folder = draft.watch.hot_folder.clone();
    disk.watch.auto_excluded_directories = draft.watch.auto_excluded_directories.clone();
    disk.backup.enabled = draft.backup.enabled;
    // 旧 UI ConfigEditor.ResolveBackupDirectory：空白时把 `<hot>/bak` 落盘，而不是留空串。
    disk.backup.directory = if draft.backup.directory.trim().is_empty() {
        default_backup_directory_for(&draft.watch.hot_folder)
    } else {
        draft.backup.directory.clone()
    };
    disk.quarantine.enabled = draft.quarantine.enabled;
    disk.quarantine.directory = draft.quarantine.directory.clone();
    disk.audit.log_directory = draft.audit.log_directory.clone();
    disk.audit.log_level = draft.audit.log_level.clone();
    disk.audit.diagnostic_mode = draft.audit.diagnostic_mode;
    disk.ui.hide_main_window_on_startup = draft.ui.hide_main_window_on_startup;
    disk.ui.hide_tray_icon = draft.ui.hide_tray_icon;
    disk.ui.theme_variant = draft.ui.theme_variant.clone();
    disk.ui.theme_id = draft.ui.theme_id.clone();
    disk.ui.locale = draft.ui.locale.clone();
    disk.ui.sidebar_width = draft.ui.sidebar_width;
    disk
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TempDir(std::path::PathBuf);

    impl TempDir {
        fn new(name: &str) -> Self {
            let nanos = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos();
            let dir = std::env::temp_dir().join(format!(
                "photoprivacy-service-{}-{name}-{nanos}",
                std::process::id()
            ));
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }

        fn config_path(&self) -> std::path::PathBuf {
            self.0.join("config.json")
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    struct Harness {
        commands: mpsc::UnboundedSender<ConfigCommand>,
        events: mpsc::UnboundedReceiver<ConfigEvent>,
        _task: tokio::task::JoinHandle<()>,
    }

    fn start(dir: &TempDir) -> Harness {
        // 发送端必须活到测试结束，否则 watch 的 changed() 会先报关闭。
        let (_endpoint_tx, endpoint_rx) = watch::channel(None);
        let (command_tx, command_rx) = mpsc::unbounded_channel();
        let (event_tx, event_rx) = mpsc::unbounded_channel();
        let task = tokio::spawn(run(
            ConfigFile::new(dir.config_path()),
            Arc::new(IpcClient::new()),
            endpoint_rx,
            command_rx,
            event_tx,
            Duration::from_millis(100),
            Duration::from_millis(100),
        ));
        Harness {
            commands: command_tx,
            events: event_rx,
            _task: task,
        }
    }

    async fn next_event(harness: &mut Harness) -> ConfigEvent {
        tokio::time::timeout(Duration::from_secs(2), harness.events.recv())
            .await
            .expect("等待配置事件超时")
            .expect("事件通道不应关闭")
    }

    #[tokio::test(start_paused = true)]
    async fn rapid_edits_collapse_into_one_save() {
        // 防：每次按键都写盘并触发 ReloadConfig，Worker 被连续重载、配置页卡顿（旧 UI 500ms 防抖，#55）。
        let dir = TempDir::new("debounce");
        let mut harness = start(&dir);
        assert!(matches!(
            next_event(&mut harness).await,
            ConfigEvent::Loaded(_)
        ));

        let mut first = AppConfig::default();
        first.audit.log_level = "warn".to_string();
        harness
            .commands
            .send(ConfigCommand::Edit(Box::new(first)))
            .unwrap();
        tokio::task::yield_now().await;

        let mut second = AppConfig::default();
        second.audit.log_level = "error".to_string();
        harness
            .commands
            .send(ConfigCommand::Edit(Box::new(second)))
            .unwrap();
        tokio::task::yield_now().await;

        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;
        let event = next_event(&mut harness).await;
        match event {
            ConfigEvent::Saved {
                config,
                worker_error,
            } => {
                assert_eq!(
                    worker_error,
                    Some("Worker 未连接，配置已保存但未热重载".to_string())
                );
                // Saved 携带实际落盘结果，UI 据此刷新（旧 UI 保存后 ApplyRuntimeConfigToUiState）。
                assert_eq!(config.audit.log_level, "error");
            }
            other => panic!("应收到 Saved，实际 {other:?}"),
        }

        let saved = fs::read_to_string(dir.config_path()).unwrap();
        assert_eq!(
            AppConfig::from_json(&saved).unwrap().audit.log_level,
            "error"
        );

        // 防抖窗口内第二次编辑被合并：不应再出现第二个 Saved 或自身写入触发的 ExternalReload。
        tokio::time::advance(Duration::from_millis(500)).await;
        tokio::task::yield_now().await;
        assert!(matches!(
            harness.events.try_recv(),
            Err(mpsc::error::TryRecvError::Empty)
        ));
    }

    #[tokio::test(start_paused = true)]
    async fn external_write_is_reloaded() {
        // 防：外部编辑器改配置后新 UI 仍显示旧值（#55）。自身写入不反弹由防抖测试覆盖。
        let dir = TempDir::new("external");
        let config_file = ConfigFile::new(dir.config_path());
        let mut initial = AppConfig::default();
        initial.ui.locale = "en".to_string();
        config_file.save(&initial).unwrap();

        let mut harness = start(&dir);
        match next_event(&mut harness).await {
            ConfigEvent::Loaded(config) => assert_eq!(config.ui.locale, "en"),
            other => panic!("首个事件应为 Loaded，实际 {other:?}"),
        }

        let mut external = initial.clone();
        external.ui.locale = "ja".to_string();
        config_file.save(&external).unwrap();
        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;

        match next_event(&mut harness).await {
            ConfigEvent::ExternalReload(config) => assert_eq!(config.ui.locale, "ja"),
            other => panic!("应收到外部回灌，实际 {other:?}"),
        }
    }

    #[tokio::test(start_paused = true)]
    async fn invalid_external_json_is_reported_and_keeps_ui_values() {
        // 防：外部手改坏 JSON 后新 UI 静默回落到默认值并把默认配置写回磁盘（#55）。
        let dir = TempDir::new("invalid-external");
        let config_file = ConfigFile::new(dir.config_path());
        config_file.save(&AppConfig::default()).unwrap();
        let mut harness = start(&dir);
        assert!(matches!(
            next_event(&mut harness).await,
            ConfigEvent::Loaded(_)
        ));

        fs::write(dir.config_path(), "{broken").unwrap();
        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;

        match next_event(&mut harness).await {
            ConfigEvent::ExternalLoadFailed(message) => {
                assert!(message.contains("解析失败"), "{message}");
            }
            other => panic!("应收到解析失败事件，实际 {other:?}"),
        }
        assert_eq!(fs::read_to_string(dir.config_path()).unwrap(), "{broken");
    }

    #[tokio::test(start_paused = true)]
    async fn save_failure_is_reported() {
        // 防：写盘失败被吞掉，界面显示「已保存」但重启后配置丢失（#55）。
        let dir = TempDir::new("save-failure");
        // 用「父路径是文件」制造不可写目标。
        let blocker = dir.0.join("blocker");
        fs::write(&blocker, "x").unwrap();
        let config_path = blocker.join("config.json");

        let (_endpoint_tx, endpoint_rx) = watch::channel(None);
        let (command_tx, command_rx) = mpsc::unbounded_channel();
        let (event_tx, mut event_rx) = mpsc::unbounded_channel();
        let _task = tokio::spawn(run(
            ConfigFile::new(config_path),
            Arc::new(IpcClient::new()),
            endpoint_rx,
            command_rx,
            event_tx,
            Duration::from_millis(100),
            Duration::from_millis(100),
        ));

        command_tx
            .send(ConfigCommand::Edit(Box::default()))
            .unwrap();
        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;

        let mut saw_failure = false;
        for _ in 0..2 {
            let event = tokio::time::timeout(Duration::from_secs(2), event_rx.recv())
                .await
                .unwrap()
                .unwrap();
            if matches!(event, ConfigEvent::SaveFailed(_)) {
                saw_failure = true;
                break;
            }
        }
        assert!(saw_failure, "写盘失败必须回报 SaveFailed");
    }

    #[tokio::test(start_paused = true)]
    async fn save_keeps_external_changes_outside_the_config_page() {
        // 防：新 UI 把启动时的整份快照写回，覆盖掉外部对 rules/retry 等非配置页字段的修改（#55）。
        let dir = TempDir::new("merge-external");
        let config_file = ConfigFile::new(dir.config_path());
        let mut initial = AppConfig::default();
        initial.rules.allowed_extensions.push(".webp".to_string());
        config_file.save(&initial).unwrap();

        let mut harness = start(&dir);
        assert!(matches!(
            next_event(&mut harness).await,
            ConfigEvent::Loaded(_)
        ));

        // 外部写入新增 .gif；UI 草稿仍是启动时的旧 rules。
        let mut external = initial.clone();
        external.rules.allowed_extensions.push(".gif".to_string());
        config_file.save(&external).unwrap();

        let mut draft = initial.clone();
        draft.ui.theme_variant = "dark".to_string();
        harness
            .commands
            .send(ConfigCommand::Edit(Box::new(draft)))
            .unwrap();
        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;
        loop {
            match next_event(&mut harness).await {
                ConfigEvent::Saved { .. } => break,
                ConfigEvent::ExternalReload(_) | ConfigEvent::ExternalLoadFailed(_) => continue,
                other => panic!("保存应成功，实际 {other:?}"),
            }
        }

        let saved = AppConfig::from_json(&fs::read_to_string(dir.config_path()).unwrap()).unwrap();
        assert_eq!(saved.ui.theme_variant, "dark");
        assert!(
            saved
                .rules
                .allowed_extensions
                .iter()
                .any(|ext| ext == ".gif"),
            "外部新增的 .gif 不能被整份快照覆盖掉：{:?}",
            saved.rules.allowed_extensions
        );
        assert!(
            saved
                .rules
                .allowed_extensions
                .iter()
                .any(|ext| ext == ".webp")
        );
    }

    #[tokio::test(start_paused = true)]
    async fn invalid_disk_config_is_never_overwritten() {
        // 防：用户手改坏 config.json 后，新 UI 用默认值整份覆盖（旧 UI 写前加载失败会保留原文件，#55）。
        let dir = TempDir::new("invalid-disk-save");
        fs::write(dir.config_path(), "{broken").unwrap();
        let mut harness = start(&dir);
        assert!(matches!(
            next_event(&mut harness).await,
            ConfigEvent::ExternalLoadFailed(_)
        ));

        let mut draft = AppConfig::default();
        draft.ui.theme_variant = "dark".to_string();
        harness
            .commands
            .send(ConfigCommand::Edit(Box::new(draft)))
            .unwrap();
        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;

        let event = next_event(&mut harness).await;
        assert!(
            matches!(event, ConfigEvent::SaveFailed(_)),
            "坏文件必须拒绝覆盖，实际 {event:?}"
        );
        assert_eq!(fs::read_to_string(dir.config_path()).unwrap(), "{broken");
    }

    #[tokio::test(start_paused = true)]
    async fn save_materializes_default_backup_directory() {
        // 防：空白 backup.directory 原样落盘，与旧 UI ConfigEditor 写出的 <hot>/bak 形态不一致（#55）。
        let dir = TempDir::new("backup-default");
        let mut harness = start(&dir);
        assert!(matches!(
            next_event(&mut harness).await,
            ConfigEvent::Loaded(_)
        ));

        let hot = dir.0.join("photos");
        let mut draft = AppConfig::default();
        draft.watch.hot_folder = hot.display().to_string();
        draft.backup.directory = String::new();
        harness
            .commands
            .send(ConfigCommand::Edit(Box::new(draft)))
            .unwrap();
        tokio::time::advance(Duration::from_millis(150)).await;
        tokio::task::yield_now().await;
        loop {
            match next_event(&mut harness).await {
                ConfigEvent::Saved { .. } => break,
                ConfigEvent::ExternalReload(_) | ConfigEvent::ExternalLoadFailed(_) => continue,
                other => panic!("保存应成功，实际 {other:?}"),
            }
        }

        let saved = AppConfig::from_json(&fs::read_to_string(dir.config_path()).unwrap()).unwrap();
        assert_eq!(
            saved.backup.directory,
            hot.join("bak").display().to_string()
        );
    }
}
