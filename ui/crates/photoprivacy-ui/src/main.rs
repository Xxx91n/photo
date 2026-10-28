mod app;
mod config;
mod config_file;
mod config_service;
mod i18n;
mod log_service;
mod rules_page;
mod service_page;
mod theme;
// Design(#49): gpui 无内建托盘（T4 已接受自建/第三方），托盘用 tray-icon 原生菜单实现。
mod tray;
mod ui_options;
mod worker_connection;

use gpui::{
    App, AppContext, AsyncApp, Bounds, Entity, Pixels, QuitMode, SharedString, TitlebarOptions,
    Window, WindowBackgroundAppearance, WindowBounds, WindowOptions, px, size,
};
use log_service::LogBatch;
use photoprivacy_ipc::{
    ConnectionSnapshot, SingleInstanceGuard, UI_INSTANCE_NAME, notify_existing_instance,
};
use photoprivacy_rules::RulesEditor;
use photoprivacy_service::{SERVICE_MANAGEMENT_SUPPORTED, platform_manager};
use std::cell::{Cell, RefCell};
use std::path::PathBuf;
use std::rc::Rc;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{mpsc, watch};
use worker_connection::{ConnectOptions, UiEvent};

const USAGE: &str = "\
PhotoPrivacy 新 UI（gpui）

用法: photoprivacy-ui [--config <config.json>] [--worker <PhotoPrivacyWorker 路径>]

  --config  启动后台 Worker 时传入的配置路径（默认同目录 config/config.json，与旧 UI 一致始终传入）
  --worker  后台 Worker 可执行文件路径（默认同目录 PhotoPrivacyWorker[.exe]，存在才用）
";

/// 托盘/唤起事件的轮询间隔：事件来自托盘库的线程或 tokio 侧，统一切回 UI 线程处理。
const EVENT_POLL_INTERVAL: Duration = Duration::from_millis(200);

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
    // 不回落到 CWD：Worker 以 AppContext.BaseDirectory 为基准，UI 若用相对路径会读写不同文件。
    let config_path = options.config_path.clone().ok_or_else(|| {
        anyhow::anyhow!("无法确定 config.json 路径：未能从可执行文件目录解析默认配置路径")
    })?;
    let ui_options = ui_options::load(options.config_path.as_deref());
    // 日志页读取与 Worker 相同的 config.json 切片；缺失/为空时回落到 `<base>/hot/_audit`
    // （旧 UI 经 AppConfigLoader 把空串补成 DefaultPaths.DefaultAuditLogDirectory）。
    let audit_config = photoprivacy_logs::resolve_audit_config(
        options.config_path.as_deref(),
        &audit_fallback_directory(),
    );
    let log_level = audit_config.log_level;

    let (snapshot_tx, snapshot_rx) = watch::channel(ConnectionSnapshot::connecting());
    let (event_tx, event_rx) = mpsc::unbounded_channel();
    let (log_tx, log_rx) = mpsc::unbounded_channel();
    // 托盘/静默启动门控必须先于建窗拿到安装态（旧 UI 在 Worker 连接编排里先探服务态）；
    // 3s 轮询首拍是异步的赶不上启动决策，所以这里同步探一次，结果同时作为服务快照初值。
    let service_manager = Arc::new(platform_manager(
        options.worker_path.clone(),
        options.config_path.clone(),
    ));
    // Design(#49): 启动路径在 main 线程同步首探安装态，上限 PROBE_TIMEOUT；托盘与静默启动据此在建窗前定档。
    let initial_service_state = service_manager.state();
    let (service_snapshots, service_commands) =
        service_page::start(runtime.handle(), service_manager, initial_service_state);
    let link = worker_connection::start(runtime.handle(), options, snapshot_tx, event_tx);

    // 配置服务复用心跳的 IpcClient（单飞锁在同一实例内生效），并用连接编排发布的端点；
    // 服务模式与后台/托盘模式只是端点值不同，ReloadConfig 链路本身不分支。
    let (config_command_tx, config_command_rx) = mpsc::unbounded_channel();
    let (config_event_tx, config_event_rx) = mpsc::unbounded_channel();
    // 窗口出现前同步读一次，避免 Loaded 到达前的编辑拿默认值覆盖盘上字段。
    let initial_config = config_file::ConfigFile::new(config_path.clone())
        .load_or_default()
        .ok();
    config_service::spawn(
        runtime.handle(),
        config_path.clone(),
        link.client.clone(),
        link.endpoint.clone(),
        config_command_rx,
        config_event_tx,
    );

    let log_service = log_service::start(runtime.handle(), audit_config, link, log_tx);

    // 10 语言键值目录：启动时一次三层加载（内嵌 → exe 目录 → 用户目录），之后只读（#60）。
    let i18n = Rc::new(i18n::Catalog::load());

    // 主题、日志缓冲、服务快照与配置草稿在关窗驻守/重建之间延续；窗口本身不保留。
    let deps = app::AppDeps {
        snapshots: snapshot_rx,
        theme_mode: Rc::new(Cell::new(theme::ThemeMode::System)),
        log_state: Rc::new(RefCell::new(app::LogState::new(log_level))),
        log_service,
        service_snapshots,
        service_commands,
        config_state: Rc::new(RefCell::new(app::ConfigUiState::new(initial_config))),
        config_commands: config_command_tx,
        config_path,
        rules: Rc::new(RefCell::new(RulesEditor::open_default())),
        i18n,
    };

    application().run(move |cx: &mut App| {
        // 启动语言取盘上 config 的 ui.locale（缺失/为空时 config.rs 已回落 zh-CN）。
        let startup_locale = deps.config_state.borrow().config.ui.locale.clone();
        let show_tray_icon = tray::should_show_tray_icon(
            initial_service_state,
            ui_options.hide_tray_icon,
            SERVICE_MANAGEMENT_SUPPORTED,
        );
        // 托盘必须在主线程、平台就绪后创建（tray-icon 要求同线程有消息循环）。
        let tray_handle = tray::install(show_tray_icon, deps.i18n.clone(), &startup_locale);
        let has_tray = tray_handle.is_some();
        let start_hidden = tray::should_start_hidden(
            ui_options.hide_main_window_on_startup,
            show_tray_icon,
            has_tray,
        );

        spawn_event_pump(
            cx,
            tray_handle,
            event_rx,
            log_rx,
            config_event_rx,
            deps.clone(),
        );

        // 关窗语义：托盘在 → 窗口销毁、进程与托盘继续驻守；托盘唤起会重建窗口。
        // 没有托盘 → 关窗即退出（与旧 UI 的 UseTrayIcon == false 分支一致）。
        // 必须显式关掉 gpui 的「最后一个窗口关闭即退出」默认——QuitMode::Default 在非 macOS 上
        // 会无条件退出，驻守时窗口一关进程就没了。
        cx.set_quit_mode(QuitMode::Explicit);
        cx.on_window_closed(move |cx, _| {
            if cx.windows().is_empty() && !has_tray {
                cx.quit();
            }
        })
        .detach();

        if !start_hidden {
            open_main_window(cx, deps);
        }
    });

    drop(guard);
    runtime.shutdown_timeout(Duration::from_millis(300));
    Ok(())
}

/// 事件泵：轮询托盘/唤起事件、排空日志批次与配置事件，并在 UI 线程执行；窗口关闭后仍继续驻守。
fn spawn_event_pump(
    cx: &mut App,
    mut tray: Option<tray::TrayHandle>,
    mut events: mpsc::UnboundedReceiver<UiEvent>,
    mut log_batches: mpsc::UnboundedReceiver<LogBatch>,
    mut config_events: mpsc::UnboundedReceiver<config_service::ConfigEvent>,
    deps: app::AppDeps,
) {
    cx.spawn(async move |cx| {
        loop {
            cx.background_executor().timer(EVENT_POLL_INTERVAL).await;

            // 日志批次必须由 app 级任务排空：窗口关闭期间视图任务已退出，通道不能无界堆积。
            let mut logs_changed = false;
            while let Ok(batch) = log_batches.try_recv() {
                if deps.log_state.borrow_mut().apply_batch(batch) {
                    logs_changed = true;
                }
            }
            if logs_changed {
                cx.refresh();
            }

            // 配置事件同样由 app 级任务应用：窗口关闭期间外部改动/保存结果不能丢。
            let mut config_changed = false;
            while let Ok(event) = config_events.try_recv() {
                if let Some(theme) = deps.config_state.borrow_mut().apply_event(event) {
                    deps.theme_mode.set(theme);
                }
                config_changed = true;
            }
            if config_changed {
                cx.refresh();
            }

            let mut show_window = false;
            let mut exit = false;

            while let Ok(event) = events.try_recv() {
                if matches!(event, UiEvent::ShowWindow) {
                    show_window = true;
                }
            }

            if let Some(tray) = &mut tray {
                let locale = deps.config_state.borrow().config.ui.locale.clone();
                while let Some(command) = tray.poll(&locale) {
                    match command {
                        tray::TrayCommand::ShowWindow => show_window = true,
                        tray::TrayCommand::Exit => exit = true,
                    }
                }
            }

            if exit {
                // 旧 UI 退出前会向后台 Worker 发 Shutdown，但 Worker 只在 CLI 模式接受该请求
                //（`WorkerIpcRequestHandler`），后台/服务模式必被拒；这里直接退进程，效果一致。
                if let Some(tray) = &tray {
                    tray.hide();
                }
                cx.update(|cx| cx.quit());
                break;
            }

            if show_window {
                show_main_window(cx, deps.clone());
            }
        }
    })
    .detach();
}

/// 唤起已有窗口，或在托盘驻守后重建窗口。
// Design(#49): gpui 无窗口隐藏 API——关窗驻守销毁窗口，托盘唤起时重建；主题与日志状态跨重建保留，
// 窗口位置/尺寸会重置为居中（旧 UI 是隐藏/显示，尺寸沿用）。
fn show_main_window(cx: &mut AsyncApp, deps: app::AppDeps) {
    let windows = cx.update(|cx| cx.windows());
    if let Some(handle) = windows.into_iter().next() {
        let _ = handle.update(cx, |_, window, _| window.activate_window());
    } else {
        open_main_window_async(cx, deps);
    }
    cx.update(|cx| cx.activate(true));
}

/// 同步创建主窗口（启动路径）。
fn open_main_window(cx: &mut App, deps: app::AppDeps) {
    let title = window_title(&deps);
    cx.open_window(centered_window_options(cx, title), move |window, cx| {
        build_main_view(window, cx, deps)
    })
    .expect("创建主窗口失败");
}

/// 异步重建主窗口（托盘/单实例唤起路径）；退出竞争中失败只记日志。
fn open_main_window_async(cx: &mut AsyncApp, deps: app::AppDeps) {
    let title = window_title(&deps);
    let options = cx.update(|cx| centered_window_options(cx, title));
    // 退出过程中仍可能收到最后一次唤起（应用已在 quit 状态时 open_window 会返回 Err）；
    // 这里不 panic，避免把正常退出变成崩溃。
    if let Err(err) = cx.open_window(options, move |window, cx| build_main_view(window, cx, deps)) {
        eprintln!("[photoprivacy-ui] 重建主窗口失败: {err}");
    }
}

/// 窗口标题按当前语言取词；窗口重建时刷新，驻守期间标题不热更（可接受粒度）。
fn window_title(deps: &app::AppDeps) -> SharedString {
    let locale = deps.config_state.borrow().config.ui.locale.clone();
    deps.i18n.get(&locale, i18n::keys::APP_TITLE).into()
}

/// 构造 AppView；窗口级依赖统一从 `AppDeps` 取。
fn build_main_view(window: &mut Window, cx: &mut App, deps: app::AppDeps) -> Entity<app::AppView> {
    cx.activate(false);
    cx.new(|cx| app::AppView::new(window, cx, deps))
}

/// 居中主窗口的尺寸与选项：配置页四组并排需要更宽的画布。
fn centered_window_options(cx: &App, title: SharedString) -> WindowOptions {
    window_options(Bounds::centered(None, size(px(980.), px(720.)), cx), title)
}

/// 主窗口的固定选项（标题栏、背景、最小尺寸）。
fn window_options(bounds: Bounds<Pixels>, title: SharedString) -> WindowOptions {
    WindowOptions {
        window_bounds: Some(WindowBounds::Windowed(bounds)),
        titlebar: Some(TitlebarOptions {
            title: Some(title),
            appears_transparent: false,
            traffic_light_position: None,
        }),
        window_background: WindowBackgroundAppearance::Opaque,
        window_min_size: Some(size(px(820.), px(600.))),
        show: true,
        focus: true,
        ..Default::default()
    }
}
