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

use crate::config::AppConfig;
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
    /// 写盘成功。`worker_reloaded=false` 时 `error` 说明 Worker 侧失败原因。
    Saved {
        worker_reloaded: bool,
        error: Option<String>,
    },
    /// 写盘本身失败。
    SaveFailed(String),
}

/// 启动后台服务；返回的 JoinHandle 由调用方持有（drop 后任务继续运行，直到命令通道关闭）。
pub fn spawn(
    runtime: &tokio::runtime::Handle,
    config_path: std::path::PathBuf,
    endpoints: watch::Receiver<Option<Endpoint>>,
    commands: mpsc::UnboundedReceiver<ConfigCommand>,
    events: mpsc::UnboundedSender<ConfigEvent>,
) -> tokio::task::JoinHandle<()> {
    runtime.spawn(run(
        ConfigFile::new(config_path),
        Arc::new(IpcClient::new()),
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
                let Some(config) = pending.take() else { continue };
                match file.save(&config) {
                    Ok(()) => {
                        // 先记下自身写入的指纹，轮询不会把这次写盘当成外部变更再回灌一次。
                        fingerprint = file.fingerprint();
                        let endpoint = endpoints.borrow().clone();
                        let event = match endpoint {
                            Some(endpoint) => match client.reload_config(&endpoint).await {
                                Ok(_) => ConfigEvent::Saved { worker_reloaded: true, error: None },
                                Err(err) => ConfigEvent::Saved {
                                    worker_reloaded: false,
                                    error: Some(err.to_string()),
                                },
                            },
                            None => ConfigEvent::Saved {
                                worker_reloaded: false,
                                error: Some("Worker 未连接，配置已保存但未热重载".to_string()),
                            },
                        };
                        let _ = events.send(event);
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
        assert_eq!(
            event,
            ConfigEvent::Saved {
                worker_reloaded: false,
                error: Some("Worker 未连接，配置已保存但未热重载".to_string()),
            }
        );

        let saved = fs::read_to_string(dir.config_path()).unwrap();
        assert_eq!(
            AppConfig::from_json(&saved).unwrap().audit.log_level,
            "error"
        );

        // 防抖窗口内第二次编辑被合并：不应再出现第二个 Saved 或自身写入触发的 ExternalReload。
        tokio::time::advance(Duration::from_millis(500)).await;
        tokio::task::yield_now().await;
        assert!(harness.events.try_recv().is_err());
    }

    #[tokio::test(start_paused = true)]
    async fn external_write_is_reloaded_but_self_write_is_not() {
        // 防：旧 UI 里「UI→磁盘→UI」自激回灌；或外部编辑器改配置后新 UI 仍显示旧值（#55）。
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
}
