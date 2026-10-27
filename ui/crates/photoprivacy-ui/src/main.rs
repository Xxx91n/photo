mod app;
mod log_service;
mod service_page;
mod theme;
mod worker_connection;

use gpui::{
    App, AppContext, Bounds, TitlebarOptions, WindowBackgroundAppearance, WindowBounds,
    WindowOptions, px, size,
};
use photoprivacy_ipc::{
    ConnectionSnapshot, SingleInstanceGuard, UI_INSTANCE_NAME, notify_existing_instance,
};
use std::path::PathBuf;
use std::time::Duration;
use worker_connection::ConnectOptions;

const USAGE: &str = "\
PhotoPrivacy 新 UI（gpui）

用法: photoprivacy-ui [--config <config.json>] [--worker <PhotoPrivacyWorker 路径>]

  --config  启动后台 Worker 时传入的配置路径（默认同目录 config/config.json，与旧 UI 一致始终传入）
  --worker  后台 Worker 可执行文件路径（默认同目录 PhotoPrivacyWorker[.exe]，存在才用）
";

struct Args {
    config_path: Option<PathBuf>,
    worker_path: Option<PathBuf>,
}

impl Args {
    fn parse() -> Self {
        let mut config_path = None;
        let mut worker_path = None;
        let mut args = std::env::args_os().skip(1);

        while let Some(arg) = args.next() {
            match arg.to_str().unwrap_or_default() {
                "--config" => config_path = args.next().map(PathBuf::from),
                "--worker" => worker_path = args.next().map(PathBuf::from),
                "-h" | "--help" => {
                    println!("{USAGE}");
                    std::process::exit(0);
                }
                _ => {}
            }
        }

        Self {
            config_path,
            worker_path,
        }
    }
}

// 平台后端在这里就地装配，替代 gpui_platform 聚合 crate（原因见 ui/Cargo.toml）。
#[cfg(target_os = "windows")]
// Design(#49): 平台后端由本 crate 装配，禁止再引入 gpui_platform 聚合 crate。
fn application() -> gpui::Application {
    let platform = std::rc::Rc::new(
        gpui_windows::WindowsPlatform::new(false).expect("初始化 Windows 图形后端失败"),
    );
    gpui::Application::with_platform(platform)
}

#[cfg(target_os = "macos")]
fn application() -> gpui::Application {
    gpui::Application::with_platform(std::rc::Rc::new(gpui_macos::MacPlatform::new(false)))
}

#[cfg(any(target_os = "linux", target_os = "freebsd"))]
fn application() -> gpui::Application {
    gpui::Application::with_platform(gpui_linux::current_platform(false))
}

fn audit_fallback_directory() -> PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|dir| dir.join("hot").join("_audit")))
        .unwrap_or_else(|| PathBuf::from("hot").join("_audit"))
}

fn main() -> anyhow::Result<()> {
    let args = Args::parse();

    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .enable_all()
        .build()?;

    // 单实例守卫：与旧 UI 同名同命名空间，任一入口已运行时，第二个实例只唤起不重开。
    let guard = SingleInstanceGuard::acquire(UI_INSTANCE_NAME);
    if !guard.is_owner() {
        let notified = runtime.block_on(notify_existing_instance(Duration::from_millis(500)));
        eprintln!("[photoprivacy-ui] 已有实例在运行（唤起窗口: {notified}），本实例退出");
        return Ok(());
    }

    let options = ConnectOptions::discover().with_overrides(args.config_path, args.worker_path);
    // 日志页读取与 Worker 相同的 config.json 切片；缺失/为空时回落到 `<base>/hot/_audit`
    // （旧 UI 经 AppConfigLoader 把空串补成 DefaultPaths.DefaultAuditLogDirectory）。
    let audit_config = photoprivacy_logs::resolve_audit_config(
        options.config_path.as_deref(),
        &audit_fallback_directory(),
    );
    let log_level = audit_config.log_level;

    let (snapshot_tx, snapshot_rx) = tokio::sync::watch::channel(ConnectionSnapshot::connecting());
    let (event_tx, event_rx) = tokio::sync::mpsc::unbounded_channel();
    let (log_tx, log_rx) = tokio::sync::mpsc::unbounded_channel();
    let (service_snapshots, service_commands) = service_page::start(
        runtime.handle(),
        options.worker_path.clone(),
        options.config_path.clone(),
    );
    let link = worker_connection::start(runtime.handle(), options, snapshot_tx, event_tx);
    let log_service = log_service::start(runtime.handle(), audit_config, link, log_tx);

    application().run(move |cx: &mut App| {
        cx.on_window_closed(|cx, _| {
            if cx.windows().is_empty() {
                cx.quit();
            }
        })
        .detach();

        let bounds = Bounds::centered(None, size(px(820.), px(580.)), cx);
        cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                titlebar: Some(TitlebarOptions {
                    title: Some("PhotoPrivacy".into()),
                    appears_transparent: false,
                    traffic_light_position: None,
                }),
                window_background: WindowBackgroundAppearance::Opaque,
                window_min_size: Some(size(px(680.), px(520.))),
                ..Default::default()
            },
            move |window, cx| {
                cx.activate(false);
                cx.new(|cx| {
                    app::AppView::new(
                        window,
                        cx,
                        app::AppViewInit {
                            snapshots: snapshot_rx,
                            events: event_rx,
                            log_batches: log_rx,
                            log_service,
                            initial_level: log_level,
                            service_snapshots,
                            service_commands,
                        },
                    )
                })
            },
        )
        .expect("创建主窗口失败");
    });

    drop(guard);
    runtime.shutdown_timeout(Duration::from_millis(300));
    Ok(())
}
