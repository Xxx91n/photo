use crate::config::{
    AppConfig, LOCALES, LOG_LEVELS, detect_exiftool_path, normalize_theme_variant,
    resolve_auto_excluded_subdirectories,
};
use crate::config_service::{ConfigCommand, ConfigEvent};
use crate::i18n::{self, Catalog, keys as k};
use crate::log_service::{LogBatch, LogService};
use crate::rules_page;
use crate::service_page::{ServiceCommand, ServiceFeedback, ServiceSnapshot};
use crate::theme::{self, Theme, ThemeMode};
use gpui::prelude::*;
use gpui::{
    AnyElement, Context, CursorStyle, Div, FocusHandle, FontWeight, IntoElement, KeyDownEvent,
    MouseButton, PathPromptOptions, Render, Rgba, Stateful, Styled, Subscription,
    UniformListScrollHandle, Window, div, px, uniform_list,
};
use photoprivacy_ipc::{ConnectionSnapshot, ConnectionState, ServiceReachability, WorkerMode};
use photoprivacy_logs::{AuditRecord, EventTone, LevelFilter, LogBuffer};
use photoprivacy_rules::RulesEditor;
use photoprivacy_service::{ServiceRuntimeState, button_state};
use std::cell::{Cell, RefCell};
use std::collections::HashMap;
use std::path::PathBuf;
use std::rc::Rc;
use std::time::{Duration, Instant};
use tokio::sync::{mpsc, watch};

const SNAPSHOT_POLL_INTERVAL: Duration = Duration::from_millis(200);
const CONTROL_WIDTH: f32 = 430.0;
/// 旧 UI 的 SaveStatus 3 秒后自动清空（MainWindow.ScheduleSaveStatusClear）。
const SAVE_STATUS_TTL: Duration = Duration::from_secs(3);

/// 日志页跨窗口重建保留的状态：托盘驻守时窗口会销毁/重建，日志缓冲不能随视图一起丢。
pub struct LogState {
    logs: LogBuffer,
    visible_indices: Vec<usize>,
    level_filter: LevelFilter,
    generation: u64,
}

impl LogState {
    /// 用初始级别创建空的共享日志状态。
    pub fn new(level_filter: LevelFilter) -> Self {
        Self {
            logs: LogBuffer::new(),
            visible_indices: Vec::new(),
            level_filter,
            generation: 0,
        }
    }

    /// 应用一个日志批次（代际校验通过时），返回状态是否变化。
    /// 由 app 级事件泵调用，窗口关闭期间也持续排空通道，避免无界队列堆积。
    pub fn apply_batch(&mut self, batch: LogBatch) -> bool {
        if batch.generation < self.generation {
            return false;
        }
        self.logs.push_batch(batch.records);
        self.visible_indices = self.logs.visible_indices(self.level_filter);
        true
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum SaveState {
    Idle,
    Pending,
    Saved,
    SavedWorkerDown(String),
    Failed(String),
    ExternalLoadFailed(String),
}

/// 配置页跨窗口重建保留的状态：草稿、保存状态、ExifTool 提示与编辑版本。
/// 配置事件由 main 的 app 级事件泵应用，窗口关闭期间也持续消费。
pub struct ConfigUiState {
    pub config: AppConfig,
    pub save_state: SaveState,
    pub save_status_deadline: Option<Instant>,
    pub exiftool_hint: Option<String>,
    /// 每次编辑递增；Saved 回来时只有等于当前值才能刷新，避免回滚保存期间的新输入。
    pub edit_revision: u64,
}

impl ConfigUiState {
    pub fn new(initial: Option<AppConfig>) -> Self {
        Self {
            config: initial.unwrap_or_default(),
            save_state: SaveState::Idle,
            save_status_deadline: None,
            exiftool_hint: None,
            edit_revision: 0,
        }
    }

    /// 应用配置服务事件；返回 `Some(theme)` 表示主题需要同步到共享 `theme_mode`。
    pub fn apply_event(&mut self, event: ConfigEvent) -> Option<ThemeMode> {
        match event {
            ConfigEvent::Loaded(config) => {
                let theme = self.set_config(config, true);
                self.save_state = SaveState::Idle;
                self.save_status_deadline = None;
                Some(theme)
            }
            ConfigEvent::ExternalReload(config) => {
                // 外部回灌不再探测 ExifTool：否则会把手改但暂时不存在的路径替换掉。
                let theme = self.set_config(config, false);
                self.save_state = SaveState::Idle;
                self.save_status_deadline = None;
                Some(theme)
            }
            ConfigEvent::ExternalLoadFailed(error) => {
                self.save_state = SaveState::ExternalLoadFailed(error);
                self.save_status_deadline = None;
                None
            }
            ConfigEvent::Saved {
                config,
                revision,
                worker_error,
            } => {
                // 与旧 UI ApplyConfigImmediatelyAsync 的 ApplyRuntimeConfigToUiState 一致：
                // 落盘后按实际写出的配置刷新显示（例如空备份目录已物化为 <hot>/bak）。
                // 保存期间又有新编辑时保留草稿，等下一次 Saved 再刷新，避免输入被回滚。
                let theme =
                    (revision == self.edit_revision).then(|| self.set_config(*config, false));
                self.save_state = match worker_error {
                    Some(error) => SaveState::SavedWorkerDown(error),
                    None => SaveState::Saved,
                };
                self.save_status_deadline = Some(Instant::now() + SAVE_STATUS_TTL);
                theme
            }
            ConfigEvent::SaveFailed(error) => {
                self.save_state = SaveState::Failed(error);
                self.save_status_deadline = Some(Instant::now() + SAVE_STATUS_TTL);
                None
            }
        }
    }

    fn set_config(&mut self, mut config: AppConfig, auto_detect_exiftool: bool) -> ThemeMode {
        config.ui.theme_variant = normalize_theme_variant(&config.ui.theme_variant).to_string();
        // 旧 UI 的 ResolveExifToolPath：首次加载时当前路径不存在则探测并显示提示；
        // 外部回灌/保存回读不清掉已有提示（旧 UI 只在启动时设置 hint）。
        if auto_detect_exiftool {
            if let Some(detected) = detect_exiftool_path(&config.exiftool.path) {
                config.exiftool.path = detected.clone();
                self.exiftool_hint = Some(detected);
            } else {
                self.exiftool_hint = None;
            }
        }
        self.config = config;
        ThemeMode::from_variant(&self.config.ui.theme_variant)
    }
}

#[repr(usize)]
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
enum FieldId {
    ExifToolPath = 0,
    HotFolder = 1,
    BackupDirectory = 2,
    QuarantineDirectory = 3,
    AuditLogDirectory = 4,
}

/// 一个可编辑路径字段的静态元数据。加字段只改这张表与 `FieldId`，不再散落五处 match。
/// placeholder 存 i18n 键（渲染时按当前语言取词），不存文案本身。
struct FieldSpec {
    element_id: &'static str,
    browse_id: &'static str,
    placeholder_key: &'static str,
    files: bool,
    value: fn(&AppConfig) -> &str,
    value_mut: fn(&mut AppConfig) -> &mut String,
}

static FIELD_SPECS: [FieldSpec; 5] = [
    FieldSpec {
        element_id: "config-exiftool-path",
        browse_id: "browse-exiftool-path",
        placeholder_key: k::CONFIG_PLACEHOLDER_EXIFTOOL_PATH,
        files: true,
        value: exiftool_path,
        value_mut: exiftool_path_mut,
    },
    FieldSpec {
        element_id: "config-hot-folder",
        browse_id: "browse-hot-folder",
        placeholder_key: k::CONFIG_PLACEHOLDER_HOT_FOLDER,
        files: false,
        value: hot_folder,
        value_mut: hot_folder_mut,
    },
    FieldSpec {
        element_id: "config-backup-directory",
        browse_id: "browse-backup-directory",
        placeholder_key: k::CONFIG_PLACEHOLDER_BACKUP_DIR,
        files: false,
        value: backup_directory,
        value_mut: backup_directory_mut,
    },
    FieldSpec {
        element_id: "config-quarantine-directory",
        browse_id: "browse-quarantine-directory",
        placeholder_key: k::CONFIG_PLACEHOLDER_QUARANTINE_DIR,
        files: false,
        value: quarantine_directory,
        value_mut: quarantine_directory_mut,
    },
    FieldSpec {
        element_id: "config-audit-log-directory",
        browse_id: "browse-audit-log-directory",
        placeholder_key: k::CONFIG_PLACEHOLDER_AUDIT_LOG_DIR,
        files: false,
        value: audit_log_directory,
        value_mut: audit_log_directory_mut,
    },
];

impl FieldId {
    const ALL: [FieldId; 5] = [
        FieldId::ExifToolPath,
        FieldId::HotFolder,
        FieldId::BackupDirectory,
        FieldId::QuarantineDirectory,
        FieldId::AuditLogDirectory,
    ];

    fn spec(self) -> &'static FieldSpec {
        &FIELD_SPECS[self as usize]
    }

    fn element_id(self) -> &'static str {
        self.spec().element_id
    }

    fn browse_id(self) -> &'static str {
        self.spec().browse_id
    }

    fn placeholder_key(self) -> &'static str {
        self.spec().placeholder_key
    }

    fn files(self) -> bool {
        self.spec().files
    }
}

fn exiftool_path(config: &AppConfig) -> &str {
    &config.exiftool.path
}

fn exiftool_path_mut(config: &mut AppConfig) -> &mut String {
    &mut config.exiftool.path
}

fn hot_folder(config: &AppConfig) -> &str {
    &config.watch.hot_folder
}

fn hot_folder_mut(config: &mut AppConfig) -> &mut String {
    &mut config.watch.hot_folder
}

fn backup_directory(config: &AppConfig) -> &str {
    &config.backup.directory
}

fn backup_directory_mut(config: &mut AppConfig) -> &mut String {
    &mut config.backup.directory
}

fn quarantine_directory(config: &AppConfig) -> &str {
    &config.quarantine.directory
}

fn quarantine_directory_mut(config: &mut AppConfig) -> &mut String {
    &mut config.quarantine.directory
}

fn audit_log_directory(config: &AppConfig) -> &str {
    &config.audit.log_directory
}

fn audit_log_directory_mut(config: &mut AppConfig) -> &mut String {
    &mut config.audit.log_directory
}

struct FieldState {
    focus: FocusHandle,
    cursor: usize,
}

/// 单个字段渲染所需的快照，避免把 focused/cursor/focus 三个值一路作为散参数传递。
struct FieldView {
    value: String,
    focused: bool,
    cursor: usize,
    focus: FocusHandle,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Page {
    Overview,
    Config,
    Service,
    Logs,
    Rules,
}

impl Page {
    /// 元素 id 用语言无关的静态串；显示文案走 i18n。
    fn id(self) -> &'static str {
        match self {
            Self::Overview => "overview",
            Self::Config => "config",
            Self::Service => "service",
            Self::Logs => "logs",
            Self::Rules => "rules",
        }
    }

    fn key(self) -> &'static str {
        match self {
            Self::Overview => k::NAV_OVERVIEW,
            Self::Config => k::NAV_CONFIG,
            Self::Service => k::NAV_SERVICE,
            Self::Logs => k::NAV_LOGS,
            Self::Rules => k::NAV_RULES,
        }
    }

    fn all() -> [Self; 5] {
        [
            Self::Overview,
            Self::Config,
            Self::Service,
            Self::Logs,
            Self::Rules,
        ]
    }
}

/// 视图跨窗口重建需要沿用的依赖：Worker/服务快照、主题、日志状态与后台控制面。
/// 打包成一个类型，避免同一组参数在事件泵/开窗/视图构造之间反复穿行。
#[derive(Clone)]
pub struct AppDeps {
    pub snapshots: watch::Receiver<ConnectionSnapshot>,
    pub theme_mode: Rc<Cell<ThemeMode>>,
    pub log_state: Rc<RefCell<LogState>>,
    pub log_service: LogService,
    pub service_snapshots: watch::Receiver<ServiceSnapshot>,
    pub service_commands: mpsc::UnboundedSender<ServiceCommand>,
    pub config_state: Rc<RefCell<ConfigUiState>>,
    pub config_commands: mpsc::UnboundedSender<ConfigCommand>,
    pub config_path: PathBuf,
    pub rules: Rc<RefCell<RulesEditor>>,
    pub i18n: Rc<Catalog>,
}

/// RTL 语言下主轴行反向，行内「标签 + 控件」的左右顺序整体镜像。
/// gpui 0.3.10 没有 `flex_direction` 通用设置器，只有 row/row_reverse 便捷方法，
/// 用扩展 trait 保持调用方的链式写法。
pub(crate) trait MirrorRow: Styled + Sized {
    fn mirror_row(self, rtl: bool) -> Self {
        if rtl { self.flex_row_reverse() } else { self }
    }
}
impl<E: Styled + Sized> MirrorRow for E {}

/// Worker 运行模式 → 展示键。
fn mode_key(mode: WorkerMode) -> &'static str {
    match mode {
        WorkerMode::Background => k::MODE_BACKGROUND,
        WorkerMode::Service => k::MODE_SERVICE,
    }
}

/// 主题模式 → chips 展示键。
fn theme_key(mode: ThemeMode) -> &'static str {
    match mode {
        ThemeMode::System => k::THEME_OPTION_SYSTEM,
        ThemeMode::Light => k::THEME_OPTION_LIGHT,
        ThemeMode::Dark => k::THEME_OPTION_DARK,
    }
}

/// 日志级别 → chips 展示键。
fn level_chip_key(level: LevelFilter) -> &'static str {
    match level {
        LevelFilter::All => k::LOGLEVEL_CHIP_ALL,
        LevelFilter::Debug => k::LOGLEVEL_CHIP_DEBUG,
        LevelFilter::Info => k::LOGLEVEL_CHIP_INFO,
        LevelFilter::Warn => k::LOGLEVEL_CHIP_WARN,
        LevelFilter::Error => k::LOGLEVEL_CHIP_ERROR,
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ButtonKind {
    Primary,
    Ghost,
    Danger,
}

pub struct AppView {
    page: Page,
    snapshot: ConnectionSnapshot,
    snapshots: watch::Receiver<ConnectionSnapshot>,
    /// 主题、日志与服务快照都跨窗口重建保留（关窗驻守后托盘会重建窗口）。
    theme_mode: Rc<Cell<ThemeMode>>,
    log_state: Rc<RefCell<LogState>>,
    service: ServiceSnapshot,
    service_snapshots: watch::Receiver<ServiceSnapshot>,
    service_commands: mpsc::UnboundedSender<ServiceCommand>,
    log_service: LogService,
    log_scroll: UniformListScrollHandle,
    config_state: Rc<RefCell<ConfigUiState>>,
    config_commands: mpsc::UnboundedSender<ConfigCommand>,
    config_path: PathBuf,
    /// 配置草稿的窗口内镜像；每次轮询从 `config_state` 同步（保存/外部回灌都写共享状态）。
    config: AppConfig,
    save_state: SaveState,
    save_status_deadline: Option<Instant>,
    exiftool_hint: Option<String>,
    edit_revision: u64,
    fields: HashMap<FieldId, FieldState>,
    pub(crate) rules: Rc<RefCell<RulesEditor>>,
    i18n: Rc<Catalog>,
    _appearance_subscription: Subscription,
}

impl AppView {
    /// 构造视图：Worker/服务快照、主题、日志与配置状态都从 `AppDeps` 取，跨窗口重建复用。
    pub fn new(window: &mut Window, cx: &mut Context<Self>, deps: AppDeps) -> Self {
        let AppDeps {
            snapshots,
            theme_mode,
            log_state,
            log_service,
            service_snapshots,
            service_commands,
            config_state,
            config_commands,
            config_path,
            rules,
            i18n,
        } = deps;
        let appearance_subscription = cx.observe_window_appearance(window, |_, _, cx| cx.notify());
        let snapshot = snapshots.borrow().clone();
        let service = service_snapshots.borrow().clone();
        let (config, save_state, save_status_deadline, exiftool_hint, edit_revision) = {
            let shared = config_state.borrow();
            (
                shared.config.clone(),
                shared.save_state.clone(),
                shared.save_status_deadline,
                shared.exiftool_hint.clone(),
                shared.edit_revision,
            )
        };
        let mut fields = HashMap::new();
        for id in FieldId::ALL {
            fields.insert(
                id,
                FieldState {
                    focus: cx.focus_handle(),
                    cursor: field_value(&config, id).len(),
                },
            );
        }
        let view = Self {
            page: Page::Overview,
            snapshot,
            snapshots,
            theme_mode,
            log_state,
            service,
            service_snapshots,
            service_commands,
            log_service,
            log_scroll: UniformListScrollHandle::new(),
            config_state,
            config_commands,
            config_path,
            config,
            save_state,
            save_status_deadline,
            exiftool_hint,
            edit_revision,
            fields,
            rules,
            i18n,
            _appearance_subscription: appearance_subscription,
        };
        view.spawn_updates(window, cx);
        view
    }

    /// 按当前草稿语言取词；语言 chips 点击即改草稿并重渲染，因此切换即时生效（#60）。
    pub(crate) fn t(&self, key: &str) -> String {
        self.i18n.get(&self.config.ui.locale, key)
    }

    /// 带占位符取词（`{0}`…`{n}`）。
    pub(crate) fn tr(&self, key: &str, args: &[&str]) -> String {
        self.i18n.get_format(&self.config.ui.locale, key, args)
    }

    /// 当前草稿语言是否 RTL；布局镜像按页在渲染层做（gpui 0.3.10 无段落级方向 API）。
    pub(crate) fn rtl(&self) -> bool {
        i18n::is_rtl(&self.config.ui.locale)
    }

    /// Worker 快照从 tokio 线程来；这里在 UI 线程轮询，避免跨线程触碰 gpui 状态。
    /// 托盘、唤起与日志批次由 main 的 app 级事件泵处理（窗口不存在时也要能响应）。
    fn spawn_updates(&self, window: &Window, cx: &mut Context<Self>) {
        cx.spawn_in(window, async move |this, cx| {
            loop {
                cx.background_executor().timer(SNAPSHOT_POLL_INTERVAL).await;

                // 关窗驻守后本视图实体会被释放；此时必须退出循环，否则每次重建窗口都会留下一个
                // 永远 upgrade 失败的空转任务。
                let alive = this
                    .update(cx, |this, cx| {
                        let mut changed = false;
                        if this.snapshots.has_changed().unwrap_or(false) {
                            this.snapshot = this.snapshots.borrow_and_update().clone();
                            changed = true;
                        }
                        if this.service_snapshots.has_changed().unwrap_or(false) {
                            this.service = this.service_snapshots.borrow_and_update().clone();
                            changed = true;
                        }
                        // 配置事件由 app 级事件泵写入共享状态；这里同步镜像供渲染与编辑使用。
                        {
                            let shared = this.config_state.borrow();
                            if shared.config != this.config {
                                this.config = shared.config.clone();
                                let lengths: Vec<(FieldId, usize)> = FieldId::ALL
                                    .iter()
                                    .map(|id| (*id, field_value(&this.config, *id).len()))
                                    .collect();
                                for (id, length) in lengths {
                                    if let Some(state) = this.fields.get_mut(&id) {
                                        state.cursor = length;
                                    }
                                }
                                changed = true;
                            }
                            if shared.save_state != this.save_state {
                                this.save_state = shared.save_state.clone();
                                changed = true;
                            }
                            if shared.save_status_deadline != this.save_status_deadline {
                                this.save_status_deadline = shared.save_status_deadline;
                                changed = true;
                            }
                            if shared.exiftool_hint != this.exiftool_hint {
                                this.exiftool_hint = shared.exiftool_hint.clone();
                                changed = true;
                            }
                            if shared.edit_revision != this.edit_revision {
                                this.edit_revision = shared.edit_revision;
                            }
                        }
                        if let Some(deadline) = this.save_status_deadline
                            && Instant::now() >= deadline
                        {
                            this.save_state = SaveState::Idle;
                            this.save_status_deadline = None;
                            if let Ok(mut shared) = this.config_state.try_borrow_mut() {
                                shared.save_state = SaveState::Idle;
                                shared.save_status_deadline = None;
                            }
                            changed = true;
                        }
                        if changed {
                            cx.notify();
                        }
                    })
                    .is_ok();
                if !alive {
                    break;
                }
            }
        })
        .detach();
    }

    /// 按当前级别过滤共享日志并刷新可见索引。
    fn rebuild_visible(&self) {
        let mut state = self.log_state.borrow_mut();
        let visible = state.logs.visible_indices(state.level_filter);
        state.visible_indices = visible;
    }

    /// 清空共享日志缓冲并推进代际；旧代际批次由 app 级事件泵在应用时丢弃。
    fn clear_logs(&mut self) {
        let mut state = self.log_state.borrow_mut();
        state.generation = state.generation.wrapping_add(1);
        state.logs.clear();
        state.visible_indices.clear();
        let generation = state.generation;
        drop(state);
        self.log_scroll = UniformListScrollHandle::new();
        self.log_service.clear(generation);
    }

    fn state_color(state: ConnectionState, theme: &Theme) -> Rgba {
        match state {
            ConnectionState::Connected => theme.success,
            ConnectionState::Connecting => theme.accent,
            ConnectionState::Reconnecting => theme.warning,
            ConnectionState::Disconnected => theme.danger,
        }
    }

    fn tone_color(tone: EventTone, theme: &Theme) -> Rgba {
        match tone {
            EventTone::Success => theme.success,
            EventTone::Neutral => theme.text,
            EventTone::Muted => theme.text_faint,
            EventTone::Info => theme.text_muted,
            EventTone::Warning => theme.warning,
            EventTone::Danger => theme.danger,
        }
    }

    fn mark_dirty(&mut self, cx: &mut Context<Self>) {
        // 旧 UI 的日志级别下拉会联动 LogEnabled；ConfigEditor 再由 LogLevel 推导 diagnostic_mode，
        // 未知级别保留原值（ResolveDiagnosticMode 的 fallback）。
        self.config.audit.diagnostic_mode =
            match self.config.audit.log_level.to_ascii_lowercase().as_str() {
                "all" | "debug" => true,
                "info" | "warn" | "error" => false,
                _ => self.config.audit.diagnostic_mode,
            };
        self.edit_revision += 1;
        {
            let mut shared = self.config_state.borrow_mut();
            shared.config = self.config.clone();
            shared.edit_revision = self.edit_revision;
            shared.save_state = SaveState::Pending;
            shared.save_status_deadline = None;
        }
        let _ = self.config_commands.send(ConfigCommand::Edit {
            config: Box::new(self.config.clone()),
            revision: self.edit_revision,
        });
        self.save_state = SaveState::Pending;
        self.save_status_deadline = None;
        cx.notify();
    }

    fn set_theme_mode(&mut self, mode: ThemeMode, cx: &mut Context<Self>) {
        if self.theme_mode.get() == mode {
            return;
        }
        self.theme_mode.set(mode);
        self.config.ui.theme_variant = mode.variant().to_string();
        self.mark_dirty(cx);
    }

    fn save_state_text(&self, theme: &Theme) -> (String, Rgba) {
        match &self.save_state {
            SaveState::Idle => (String::new(), theme.text_faint),
            SaveState::Pending => (self.t(k::STATUS_SAVING), theme.text_muted),
            SaveState::Saved => (self.t(k::STATUS_SAVED_RELOADED), theme.success),
            SaveState::SavedWorkerDown(error) => (
                self.tr(k::STATUS_SAVED_WORKER_DOWN, &[error]),
                theme.warning,
            ),
            SaveState::Failed(error) => (
                self.tr(k::MSG_SAVE_FAILED_EXCEPTION, &[error]),
                theme.danger,
            ),
            SaveState::ExternalLoadFailed(error) => {
                (self.tr(k::STATUS_CONFIG_ERROR, &[error]), theme.danger)
            }
        }
    }

    fn edit_field(&mut self, id: FieldId, event: &KeyDownEvent, cx: &mut Context<Self>) {
        let modifiers = &event.keystroke.modifiers;
        if (modifiers.control && !modifiers.alt) || modifiers.platform {
            if event.keystroke.key.eq_ignore_ascii_case("v")
                && let Some(item) = cx.read_from_clipboard()
                && let Some(text) = item.text()
            {
                let filtered: String = text
                    .chars()
                    .filter(|character| *character != '\r' && *character != '\n')
                    .collect();
                self.insert_field_text(id, &filtered, cx);
            }
            return;
        }
        // Windows AltGr 以 Ctrl+Alt 上报；带 key_char 时按文本插入，不让修饰键吞掉输入。
        if modifiers.control
            && modifiers.alt
            && let Some(text) = event
                .keystroke
                .key_char
                .as_deref()
                .filter(|text| !text.is_empty())
        {
            self.insert_field_text(id, text, cx);
            return;
        }

        match event.keystroke.key.as_str() {
            "backspace" => self.backspace_field(id, cx),
            "delete" => self.delete_field(id, cx),
            "left" => self.move_cursor(id, false, cx),
            "right" => self.move_cursor(id, true, cx),
            "home" => self.set_cursor(id, 0, cx),
            "end" => {
                let end = field_value(&self.config, id).len();
                self.set_cursor(id, end, cx);
            }
            "enter" | "tab" | "escape" => {}
            _ => {
                if let Some(text) = event
                    .keystroke
                    .key_char
                    .as_deref()
                    .filter(|text| !text.is_empty())
                {
                    self.insert_field_text(id, text, cx);
                }
            }
        }
    }

    fn insert_field_text(&mut self, id: FieldId, text: &str, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let new_cursor = {
            let value = field_value_mut(&mut self.config, id);
            let cursor = floor_char_boundary(value, cursor);
            value.insert_str(cursor, text);
            cursor + text.len()
        };
        self.set_cursor(id, new_cursor, cx);
        self.mark_dirty(cx);
    }

    fn backspace_field(&mut self, id: FieldId, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let (new_cursor, changed) = {
            let value = field_value_mut(&mut self.config, id);
            let cursor = floor_char_boundary(value, cursor);
            let start = prev_char_boundary(value, cursor);
            if start == cursor {
                (cursor, false)
            } else {
                value.replace_range(start..cursor, "");
                (start, true)
            }
        };
        self.set_cursor(id, new_cursor, cx);
        if changed {
            self.mark_dirty(cx);
        }
    }

    fn delete_field(&mut self, id: FieldId, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let (new_cursor, changed) = {
            let value = field_value_mut(&mut self.config, id);
            let cursor = floor_char_boundary(value, cursor);
            let end = next_char_boundary(value, cursor);
            if end == cursor {
                (cursor, false)
            } else {
                value.replace_range(cursor..end, "");
                (cursor, true)
            }
        };
        self.set_cursor(id, new_cursor, cx);
        if changed {
            self.mark_dirty(cx);
        }
    }

    fn move_cursor(&mut self, id: FieldId, forward: bool, cx: &mut Context<Self>) {
        let cursor = self.field_cursor(id);
        let value = field_value(&self.config, id);
        let cursor = floor_char_boundary(value, cursor);
        let next = if forward {
            next_char_boundary(value, cursor)
        } else {
            prev_char_boundary(value, cursor)
        };
        self.set_cursor(id, next, cx);
    }

    fn field_cursor(&self, id: FieldId) -> usize {
        self.fields
            .get(&id)
            .map(|state| state.cursor)
            .unwrap_or_default()
    }

    fn set_cursor(&mut self, id: FieldId, cursor: usize, cx: &mut Context<Self>) {
        if let Some(state) = self.fields.get_mut(&id) {
            state.cursor = cursor;
        }
        cx.notify();
    }

    fn field_view(&self, id: FieldId, window: &Window) -> FieldView {
        let state = self
            .fields
            .get(&id)
            .expect("配置页字段的焦点句柄在构造时已建立");
        FieldView {
            value: field_value(&self.config, id).to_string(),
            focused: state.focus.is_focused(window),
            cursor: state.cursor,
            focus: state.focus.clone(),
        }
    }

    /// 打开系统路径选择器并回填单个路径；两个调用点只在「选中后做什么」上不同。
    fn prompt_single_path(
        &mut self,
        files: bool,
        prompt: String,
        window: &mut Window,
        cx: &mut Context<Self>,
        on_pick: impl FnOnce(&mut Self, String, &mut Context<Self>) + 'static,
    ) {
        let receiver = cx.prompt_for_paths(PathPromptOptions {
            files,
            directories: !files,
            multiple: false,
            prompt: Some(prompt.into()),
        });
        cx.spawn_in(window, async move |this, cx| {
            let selected = receiver
                .await
                .ok()
                .and_then(|result| result.ok())
                .flatten()
                .and_then(|paths| paths.into_iter().next());
            let Some(path) = selected else {
                return;
            };
            let text = path.to_string_lossy().into_owned();
            this.update(cx, |this, cx| on_pick(this, text, cx)).ok();
        })
        .detach();
    }

    fn pick_path(&mut self, id: FieldId, window: &mut Window, cx: &mut Context<Self>) {
        let prompt = if id.files() {
            self.t(k::DIALOG_SELECT_EXIFTOOL)
        } else {
            self.t(k::DIALOG_SELECT_FOLDER)
        };
        self.prompt_single_path(id.files(), prompt, window, cx, move |this, text, cx| {
            let length = text.len();
            *field_value_mut(&mut this.config, id) = text;
            this.set_cursor(id, length, cx);
            this.mark_dirty(cx);
        });
    }

    fn add_excluded_directory(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let prompt = self.t(k::DIALOG_SELECT_EXCLUDED_DIR);
        self.prompt_single_path(false, prompt, window, cx, |this, text, cx| {
            this.config.watch.auto_excluded_directories.push(text);
            this.mark_dirty(cx);
        });
    }

    fn theme_switch(&self, mode: ThemeMode, theme: &Theme, cx: &mut Context<Self>) -> Div {
        let i18n = self.i18n.clone();
        let locale = self.config.ui.locale.clone();
        div()
            .flex()
            .items_center()
            .gap(px(2.))
            .p(px(2.))
            .bg(theme.surface_raised)
            .border_1()
            .border_color(theme.border)
            .rounded(px(9.))
            .children(ThemeMode::all().into_iter().map(move |candidate| {
                let label = theme_key(candidate);
                let active = candidate == mode;
                div()
                    .id(candidate.variant())
                    .px(px(10.))
                    .py(px(4.))
                    .rounded(px(7.))
                    .text_size(px(12.))
                    .when(active, |element| {
                        element
                            .bg(theme.surface)
                            .text_color(theme.text)
                            .font_weight(FontWeight::MEDIUM)
                    })
                    .when(!active, |element| element.text_color(theme.text_muted))
                    .hover(|element| element.bg(theme.surface))
                    .cursor_pointer()
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.set_theme_mode(candidate, cx);
                    }))
                    .child(i18n.get(&locale, label))
            }))
    }

    fn row_base(
        &self,
        label: String,
        description: String,
        control: AnyElement,
        theme: Theme,
    ) -> Div {
        div()
            .flex()
            .mirror_row(self.rtl())
            .items_center()
            .justify_between()
            .gap(px(24.))
            .px(px(16.))
            .py(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .flex_1()
                    .child(div().text_size(px(13.)).text_color(theme.text).child(label))
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(description),
                    ),
            )
            .child(div().flex_shrink_0().child(control))
    }

    fn settings_row(
        &self,
        label: String,
        description: String,
        control: AnyElement,
        theme: Theme,
    ) -> AnyElement {
        self.row_base(label, description, control, theme)
            .into_any_element()
    }

    #[allow(clippy::too_many_arguments)] // 与 config_service 同款：行渲染的固有形状参数。
    fn toggle_row(
        &self,
        id: &'static str,
        label: String,
        description: String,
        on: bool,
        theme: Theme,
        cx: &mut Context<Self>,
        toggle: impl Fn(&mut Self, &mut Context<Self>) + 'static,
    ) -> AnyElement {
        self.row_base(label, description, Self::toggle_switch(on, theme), theme)
            .id(id)
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, _, cx| toggle(this, cx)))
            .into_any_element()
    }

    fn toggle_switch(on: bool, theme: Theme) -> AnyElement {
        div()
            .flex()
            .items_center()
            .justify_start()
            .w(px(34.))
            .h(px(20.))
            .px(px(2.))
            .rounded_full()
            .bg(if on { theme.accent } else { theme.border })
            .when(on, |element| element.justify_end())
            .child(div().size(px(16.)).rounded_full().bg(theme.surface))
            .into_any_element()
    }

    fn choice_chip(
        &self,
        id: String,
        label: String,
        active: bool,
        theme: Theme,
        cx: &mut Context<Self>,
        select: impl Fn(&mut Self, &mut Window, &mut Context<Self>) + 'static,
    ) -> AnyElement {
        div()
            .id(id)
            .px(px(10.))
            .py(px(4.))
            .rounded(px(7.))
            .text_size(px(12.))
            .when(active, |element| {
                element
                    .bg(theme.accent)
                    .text_color(theme.bg)
                    .font_weight(FontWeight::MEDIUM)
            })
            .when(!active, |element| {
                element
                    .bg(theme.surface_raised)
                    .text_color(theme.text_muted)
            })
            .hover(|element| element.text_color(theme.text))
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, window, cx| select(this, window, cx)))
            .child(label)
            .into_any_element()
    }

    fn text_field(
        &self,
        id: FieldId,
        view: FieldView,
        theme: Theme,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let FieldView {
            value,
            focused,
            cursor,
            focus,
        } = view;
        let focus_for_click = focus.clone();
        let cursor_for_click = cursor;
        let content = if value.is_empty() && !focused {
            div()
                .text_size(px(12.))
                .text_color(theme.text_faint)
                .child(self.t(id.placeholder_key()))
                .into_any_element()
        } else {
            let cursor = floor_char_boundary(&value, cursor);
            let before = value[..cursor].to_string();
            let after = value[cursor..].to_string();
            div()
                .flex()
                .items_center()
                .min_w(px(0.))
                .overflow_hidden()
                .child(
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text)
                        .whitespace_nowrap()
                        .child(before),
                )
                .when(focused, |element| {
                    element.child(div().w(px(1.)).h(px(14.)).bg(theme.accent))
                })
                .child(
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text)
                        .whitespace_nowrap()
                        .child(after),
                )
                .into_any_element()
        };

        div()
            .id(id.element_id())
            .flex()
            .items_center()
            .flex_1()
            .min_w(px(0.))
            .h(px(30.))
            .px(px(10.))
            .bg(theme.surface)
            .border_1()
            .border_color(if focused { theme.accent } else { theme.border })
            .rounded(px(7.))
            .cursor(CursorStyle::IBeam)
            .track_focus(&focus)
            .on_mouse_down(
                MouseButton::Left,
                cx.listener(move |this, _, window, cx| {
                    window.focus(&focus_for_click, cx);
                    this.set_cursor(id, cursor_for_click, cx);
                }),
            )
            .on_key_down(cx.listener(move |this, event: &KeyDownEvent, _, cx| {
                this.edit_field(id, event, cx);
            }))
            .child(content)
            .into_any_element()
    }

    fn browse_button(&self, id: FieldId, theme: Theme, cx: &mut Context<Self>) -> AnyElement {
        div()
            .id(id.browse_id())
            .px(px(10.))
            .py(px(6.))
            .rounded(px(7.))
            .border_1()
            .border_color(theme.border)
            .text_size(px(12.))
            .text_color(theme.text_muted)
            .hover(|element| element.bg(theme.surface_raised))
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, window, cx| {
                this.pick_path(id, window, cx);
            }))
            .child(self.t(k::BTN_BROWSE))
            .into_any_element()
    }

    fn path_control(
        &self,
        id: FieldId,
        view: FieldView,
        theme: Theme,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        div()
            .flex()
            .items_center()
            .gap(px(8.))
            .w(px(CONTROL_WIDTH))
            .child(self.text_field(id, view, theme, cx))
            .child(self.browse_button(id, theme, cx))
            .into_any_element()
    }

    fn group(&self, title: String, rows: Vec<AnyElement>, theme: Theme) -> AnyElement {
        div()
            .flex()
            .flex_col()
            .gap(px(8.))
            .child(
                div()
                    .text_size(px(11.))
                    .font_weight(FontWeight::MEDIUM)
                    .text_color(theme.text_faint)
                    .child(title),
            )
            .child(
                div()
                    .flex()
                    .flex_col()
                    .bg(theme.surface)
                    .border_1()
                    .border_color(theme.border)
                    .rounded(px(12.))
                    .overflow_hidden()
                    .children(rows),
            )
            .into_any_element()
    }

    fn list_row(
        &self,
        label: String,
        description: String,
        list: AnyElement,
        theme: Theme,
    ) -> AnyElement {
        div()
            .flex()
            .flex_col()
            .gap(px(8.))
            .px(px(16.))
            .py(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .child(div().text_size(px(13.)).text_color(theme.text).child(label))
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(description),
                    ),
            )
            .child(list)
            .into_any_element()
    }

    fn config_page(&self, window: &Window, cx: &mut Context<Self>) -> AnyElement {
        let theme = Theme::resolve(self.theme_mode.get(), theme::is_dark(window.appearance()));
        let (status_text, status_color) = self.save_state_text(&theme);
        let mut groups: Vec<AnyElement> = Vec::new();

        // 路径组
        let mut path_rows = Vec::new();
        let exiftool_view = self.field_view(FieldId::ExifToolPath, window);
        let exiftool_control = {
            let control = self.path_control(FieldId::ExifToolPath, exiftool_view, theme, cx);
            if let Some(hint) = &self.exiftool_hint {
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .w(px(CONTROL_WIDTH))
                    .child(control)
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_faint)
                            .truncate()
                            .child(format!("{}: {hint}", self.t(k::STATUS_AUTO_DETECTED))),
                    )
                    .into_any_element()
            } else {
                control
            }
        };
        path_rows.push(self.settings_row(
            self.t(k::SETTINGS_EXIFTOOL_PATH),
            self.t(k::CONFIG_DESC_EXIFTOOL_PATH),
            exiftool_control,
            theme,
        ));
        let hot_view = self.field_view(FieldId::HotFolder, window);
        path_rows.push(self.settings_row(
            self.t(k::SETTINGS_HOT_FOLDER),
            self.t(k::CONFIG_DESC_HOT_FOLDER),
            self.path_control(FieldId::HotFolder, hot_view, theme, cx),
            theme,
        ));
        groups.push(self.group(self.t(k::CONFIG_SECTION_PATH), path_rows, theme));

        // 行为组
        let mut behavior_rows = Vec::new();
        behavior_rows.push(self.toggle_row(
            "toggle-backup",
            self.t(k::CONFIG_ROW_ENABLE_BACKUP),
            self.t(k::CONFIG_DESC_BACKUP_ENABLED),
            self.config.backup.enabled,
            theme,
            cx,
            |this, cx| {
                this.config.backup.enabled = !this.config.backup.enabled;
                this.mark_dirty(cx);
            },
        ));
        if self.config.backup.enabled {
            let view = self.field_view(FieldId::BackupDirectory, window);
            behavior_rows.push(self.settings_row(
                self.t(k::SETTINGS_BACKUP_DIR),
                self.t(k::CONFIG_DESC_BACKUP_DIR),
                self.path_control(FieldId::BackupDirectory, view, theme, cx),
                theme,
            ));
        }
        behavior_rows.push(self.toggle_row(
            "toggle-quarantine",
            self.t(k::CONFIG_ROW_ENABLE_QUARANTINE),
            self.t(k::CONFIG_DESC_QUARANTINE_ENABLED),
            self.config.quarantine.enabled,
            theme,
            cx,
            |this, cx| {
                this.config.quarantine.enabled = !this.config.quarantine.enabled;
                this.mark_dirty(cx);
            },
        ));
        if self.config.quarantine.enabled {
            let view = self.field_view(FieldId::QuarantineDirectory, window);
            behavior_rows.push(self.settings_row(
                self.t(k::SETTINGS_QUARANTINE_DIR),
                self.t(k::CONFIG_DESC_QUARANTINE_DIR),
                self.path_control(FieldId::QuarantineDirectory, view, theme, cx),
                theme,
            ));
        }
        behavior_rows.push(self.toggle_row(
            "toggle-hide-startup",
            self.t(k::SETTINGS_HIDE_ON_STARTUP),
            self.t(k::CONFIG_DESC_HIDE_ON_STARTUP),
            self.config.ui.hide_main_window_on_startup,
            theme,
            cx,
            |this, cx| {
                this.config.ui.hide_main_window_on_startup =
                    !this.config.ui.hide_main_window_on_startup;
                this.mark_dirty(cx);
            },
        ));
        behavior_rows.push(self.toggle_row(
            "toggle-hide-tray",
            self.t(k::SETTINGS_HIDE_TRAY),
            self.t(k::CONFIG_DESC_HIDE_TRAY),
            self.config.ui.hide_tray_icon,
            theme,
            cx,
            |this, cx| {
                this.config.ui.hide_tray_icon = !this.config.ui.hide_tray_icon;
                this.mark_dirty(cx);
            },
        ));

        let mut theme_chips = Vec::new();
        for candidate in ThemeMode::all() {
            theme_chips.push(self.choice_chip(
                format!("theme-{}", candidate.variant()),
                self.t(theme_key(candidate)),
                self.theme_mode.get() == candidate,
                theme,
                cx,
                move |this, _, cx| this.set_theme_mode(candidate, cx),
            ));
        }
        behavior_rows.push(
            self.settings_row(
                self.t(k::SETTINGS_THEME),
                self.t(k::CONFIG_DESC_THEME),
                div()
                    .flex()
                    .items_center()
                    .gap(px(6.))
                    .w(px(CONTROL_WIDTH))
                    .children(theme_chips)
                    .into_any_element(),
                theme,
            ),
        );

        let mut locale_chips = Vec::new();
        for &(code, label) in LOCALES {
            let active = self.config.ui.locale.eq_ignore_ascii_case(code);
            let code = code.to_string();
            locale_chips.push(self.choice_chip(
                format!("locale-{code}"),
                // 语言自称（endonym）不随界面语言翻译，与旧 UI 一致。
                label.to_string(),
                active,
                theme,
                cx,
                move |this, window, cx| {
                    this.config.ui.locale = code.clone();
                    this.mark_dirty(cx);
                    // 窗口标题取词于 app.title；仅当 exe/用户目录覆盖层使各语言标题不同时可见差异。
                    window.set_window_title(&this.t(k::APP_TITLE));
                },
            ));
        }
        behavior_rows.push(
            self.settings_row(
                self.t(k::SETTINGS_LANGUAGE),
                self.t(k::CONFIG_DESC_LANGUAGE),
                div()
                    .flex()
                    .flex_wrap()
                    .items_center()
                    .gap(px(6.))
                    .w(px(CONTROL_WIDTH))
                    .children(locale_chips)
                    .into_any_element(),
                theme,
            ),
        );
        groups.push(self.group(self.t(k::CONFIG_SECTION_BEHAVIOR), behavior_rows, theme));

        // 排除目录组
        let mut excluded_rows = Vec::new();
        let system_excluded = resolve_auto_excluded_subdirectories(&self.config);
        let system_list = if system_excluded.is_empty() {
            div()
                .text_size(px(12.))
                .text_color(theme.text_faint)
                .child(self.t(k::COMMON_NONE))
                .into_any_element()
        } else {
            div()
                .flex()
                .flex_col()
                .gap(px(2.))
                .children(system_excluded.into_iter().map(|path| {
                    div()
                        .text_size(px(12.))
                        .text_color(theme.text_muted)
                        .truncate()
                        .child(path)
                }))
                .into_any_element()
        };
        excluded_rows.push(self.list_row(
            self.t(k::CONFIG_LABEL_SYSTEM_EXCLUDED),
            self.t(k::CONFIG_DESC_SYSTEM_EXCLUDED),
            system_list,
            theme,
        ));

        let mut user_excluded = Vec::new();
        if self.config.watch.auto_excluded_directories.is_empty() {
            user_excluded.push(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_faint)
                    .child(self.t(k::CONFIG_EMPTY_USER_EXCLUDED))
                    .into_any_element(),
            );
        } else {
            for (index, path) in self
                .config
                .watch
                .auto_excluded_directories
                .iter()
                .enumerate()
            {
                let path_to_remove = path.clone();
                user_excluded.push(
                    div()
                        .id(format!("excluded-{index}"))
                        .flex()
                        .items_center()
                        .justify_between()
                        .gap(px(8.))
                        .px(px(8.))
                        .py(px(4.))
                        .rounded(px(6.))
                        .bg(theme.surface_raised)
                        .child(
                            div()
                                .text_size(px(12.))
                                .text_color(theme.text)
                                .truncate()
                                .child(path.clone()),
                        )
                        .child(
                            div()
                                .id(format!("remove-excluded-{index}"))
                                .px(px(6.))
                                .rounded(px(5.))
                                .text_size(px(12.))
                                .text_color(theme.text_muted)
                                .hover(|element| element.text_color(theme.danger))
                                .cursor_pointer()
                                .on_click(cx.listener(move |this, _, _, cx| {
                                    if let Some(position) = this
                                        .config
                                        .watch
                                        .auto_excluded_directories
                                        .iter()
                                        .position(|entry| entry == &path_to_remove)
                                    {
                                        this.config
                                            .watch
                                            .auto_excluded_directories
                                            .remove(position);
                                    }
                                    this.mark_dirty(cx);
                                }))
                                .child(self.t(k::CONFIG_BTN_REMOVE_DIR)),
                        )
                        .into_any_element(),
                );
            }
        }
        user_excluded.push(
            div()
                .id("add-excluded")
                .mt(px(2.))
                .px(px(10.))
                .py(px(5.))
                .rounded(px(7.))
                .border_1()
                .border_color(theme.border)
                .text_size(px(12.))
                .text_color(theme.text_muted)
                .hover(|element| element.bg(theme.surface_raised))
                .cursor_pointer()
                .on_click(cx.listener(|this, _, window, cx| {
                    this.add_excluded_directory(window, cx);
                }))
                .child(self.t(k::CONFIG_BTN_ADD_FOLDER))
                .into_any_element(),
        );
        excluded_rows.push(
            self.list_row(
                self.t(k::CONFIG_LABEL_USER_EXCLUDED),
                self.t(k::CONFIG_DESC_USER_EXCLUDED),
                div()
                    .flex()
                    .flex_col()
                    .gap(px(4.))
                    .children(user_excluded)
                    .into_any_element(),
                theme,
            ),
        );
        groups.push(self.group(self.t(k::CONFIG_SECTION_EXCLUDED), excluded_rows, theme));

        // 日志组
        let mut log_rows = Vec::new();
        let mut level_chips = Vec::new();
        // 旧 UI 的 LogLevelIndex 对未知级别回落 info 选中（MainWindowViewModel）。
        let configured_level = self.config.audit.log_level.to_ascii_lowercase();
        let selected_level = if LOG_LEVELS
            .iter()
            .any(|level| level.eq_ignore_ascii_case(&configured_level))
        {
            configured_level.as_str()
        } else {
            "info"
        };
        for &level in LOG_LEVELS {
            let active = selected_level == level;
            let level_for_config = level.to_string();
            level_chips.push(self.choice_chip(
                format!("loglevel-{level}"),
                self.t(level_chip_key(LevelFilter::parse(level))),
                active,
                theme,
                cx,
                move |this, _, cx| {
                    this.config.audit.log_level = level_for_config.clone();
                    this.mark_dirty(cx);
                },
            ));
        }
        log_rows.push(
            self.settings_row(
                self.t(k::SETTINGS_LOG_LEVEL),
                self.t(k::CONFIG_DESC_LOG_LEVEL),
                div()
                    .flex()
                    .items_center()
                    .gap(px(6.))
                    .w(px(CONTROL_WIDTH))
                    .children(level_chips)
                    .into_any_element(),
                theme,
            ),
        );
        let audit_view = self.field_view(FieldId::AuditLogDirectory, window);
        log_rows.push(self.settings_row(
            self.t(k::SETTINGS_AUDIT_LOG_DIR),
            self.t(k::CONFIG_DESC_AUDIT_LOG_DIR),
            self.path_control(FieldId::AuditLogDirectory, audit_view, theme, cx),
            theme,
        ));
        groups.push(self.group(self.t(k::CONFIG_SECTION_LOGS), log_rows, theme));

        div()
            .flex()
            .flex_col()
            .gap(px(12.))
            .child(
                div()
                    .flex()
                    .items_center()
                    .justify_between()
                    .gap(px(16.))
                    .child(
                        div()
                            .text_size(px(18.))
                            .font_weight(FontWeight::SEMIBOLD)
                            .child(self.t(k::NAV_CONFIG)),
                    )
                    .when(!status_text.is_empty(), |element| {
                        element.child(
                            div()
                                .text_size(px(11.))
                                .text_color(status_color)
                                .truncate()
                                .child(status_text),
                        )
                    }),
            )
            .children(groups)
            .into_any_element()
    }

    fn nav_tab(
        page: Page,
        current: Page,
        label: String,
        theme: &Theme,
        cx: &mut Context<Self>,
    ) -> Stateful<Div> {
        let active = page == current;
        div()
            .id(page.id())
            .px(px(11.))
            .py(px(4.))
            .rounded(px(7.))
            .text_size(px(12.))
            .when(active, |element| {
                element
                    .bg(theme.surface)
                    .text_color(theme.text)
                    .font_weight(FontWeight::MEDIUM)
            })
            .when(!active, |element| element.text_color(theme.text_muted))
            .hover(|element| element.bg(theme.surface))
            .cursor_pointer()
            .on_click(cx.listener(move |this, _, _, cx| {
                this.page = page;
                cx.notify();
            }))
            .child(label)
    }

    fn level_filter_group(
        &self,
        current_level: LevelFilter,
        theme: &Theme,
        cx: &mut Context<Self>,
    ) -> Div {
        let i18n = self.i18n.clone();
        let locale = self.config.ui.locale.clone();
        div()
            .flex()
            .items_center()
            .gap(px(2.))
            .p(px(2.))
            .bg(theme.surface_raised)
            .border_1()
            .border_color(theme.border)
            .rounded(px(9.))
            .children(LevelFilter::all().into_iter().map(move |candidate| {
                let active = candidate == current_level;
                div()
                    .id(candidate.label())
                    .px(px(9.))
                    .py(px(4.))
                    .rounded(px(7.))
                    .text_size(px(11.))
                    .when(active, |element| {
                        element
                            .bg(theme.surface)
                            .text_color(theme.text)
                            .font_weight(FontWeight::MEDIUM)
                    })
                    .when(!active, |element| element.text_color(theme.text_muted))
                    .hover(|element| element.bg(theme.surface))
                    .cursor_pointer()
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.log_state.borrow_mut().level_filter = candidate;
                        this.rebuild_visible();
                        cx.notify();
                    }))
                    .child(i18n.get(&locale, level_chip_key(candidate)))
            }))
    }

    fn detail_row(&self, label: String, value: String, theme: &Theme) -> Div {
        div()
            .flex()
            .mirror_row(self.rtl())
            .items_center()
            .justify_between()
            .gap(px(16.))
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(label),
            )
            .child(div().text_size(px(12.)).text_color(theme.text).child(value))
    }

    /// Overview 页：连接状态与 Worker 详情。
    fn overview_body(&self, theme: &Theme, cx: &mut Context<Self>) -> AnyElement {
        let state = self.snapshot.state;
        let dot = Self::state_color(state, theme);
        let endpoint = or_dash(self.snapshot.endpoint.clone());
        let mode = or_dash(self.snapshot.worker_mode.map(|mode| self.t(mode_key(mode))));
        let round_trip = match self.snapshot.last_round_trip {
            Some(elapsed) => self.tr(
                k::OVERVIEW_LAST_ROUND_TRIP,
                &[&elapsed.as_millis().to_string()],
            ),
            None => self.t(k::OVERVIEW_NO_ROUND_TRIP),
        };
        let state_label = match state {
            ConnectionState::Connected => self.t(k::STATUS_CONNECTED),
            ConnectionState::Connecting => self.t(k::STATUS_CONNECTING),
            ConnectionState::Reconnecting => self.t(k::STATUS_RECONNECTING),
            ConnectionState::Disconnected => self.t(k::STATUS_DISCONNECTED),
        };

        let status_card = div()
            .flex()
            .flex_col()
            .gap(px(8.))
            .p(px(16.))
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(8.))
                    .child(div().size(px(8.)).rounded_full().bg(dot))
                    .child(
                        div()
                            .text_size(px(14.))
                            .font_weight(FontWeight::MEDIUM)
                            .child(state_label),
                    )
                    .child(
                        div()
                            .text_size(px(12.))
                            .text_color(theme.text_muted)
                            .child(format!("· {endpoint} · {mode}")),
                    )
                    .child(div().flex_1())
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_faint)
                            .child(round_trip),
                    ),
            )
            .children(self.snapshot.last_error.clone().map(|error| {
                div()
                    .text_size(px(12.))
                    .text_color(theme.danger)
                    .child(error)
            }))
            .children(
                (state == ConnectionState::Connected
                    && self
                        .snapshot
                        .watch_directory
                        .as_deref()
                        .unwrap_or_default()
                        .trim()
                        .is_empty())
                .then(|| {
                    div()
                        .cursor_pointer()
                        .hover(|element| element.text_color(theme.text))
                        .on_click(cx.listener(|this, _, _, cx| {
                            this.page = Page::Config;
                            cx.notify();
                        }))
                        .text_size(px(12.))
                        .text_color(theme.warning)
                        .child(self.t(k::STATUS_CONFIGURE_WATCH_FOLDER))
                }),
            );

        let rows = [
            (self.t(k::OVERVIEW_WORKER_MODE), mode.clone()),
            (
                self.t(k::OVERVIEW_REPORTED_MODE),
                or_dash(self.snapshot.status_mode.clone()),
            ),
            (
                self.t(k::OVERVIEW_SERVICE_IPC),
                match self.snapshot.service_reachability {
                    ServiceReachability::Unknown => "—".to_string(),
                    ServiceReachability::Reachable => self.t(k::OVERVIEW_REACHABLE),
                    ServiceReachability::Unreachable => self.t(k::OVERVIEW_UNREACHABLE),
                },
            ),
            (
                self.t(k::OVERVIEW_PROTOCOL),
                or_dash(
                    self.snapshot
                        .protocol_version
                        .map(|version| format!("v{version}")),
                ),
            ),
            (
                self.t(k::OVERVIEW_EXIFTOOL),
                or_dash(self.snapshot.exif_tool_version.clone()),
            ),
            (
                self.t(k::OVERVIEW_ROUND_TRIPS),
                self.snapshot.round_trips.to_string(),
            ),
        ];

        let details = div()
            .flex()
            .flex_col()
            .gap(px(10.))
            .p(px(16.))
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child(self.t(k::OVERVIEW_WORKER_SECTION)),
            )
            .children(
                rows.into_iter()
                    .map(|(label, value)| self.detail_row(label, value, theme)),
            );

        div()
            .flex_1()
            .flex()
            .flex_col()
            .gap(px(12.))
            .px(px(24.))
            .pb(px(12.))
            .child(status_card)
            .child(details)
            .into_any_element()
    }

    /// Logs 页：级别过滤、清空与审计尾部列表。
    fn logs_body(&mut self, theme: &Theme, cx: &mut Context<Self>) -> AnyElement {
        let (level_filter, visible_len, logs_empty, filtered_empty) = {
            let state = self.log_state.borrow();
            (
                state.level_filter,
                state.visible_indices.len(),
                state.logs.is_empty(),
                !state.logs.is_empty() && state.visible_indices.is_empty(),
            )
        };
        let filter_group = self.level_filter_group(level_filter, theme, cx);
        let count_label = self.tr(k::LOG_ENTRIES_COUNT, &[&visible_len.to_string()]);
        let toolbar = div()
            .flex()
            .items_center()
            .gap(px(12.))
            .child(filter_group)
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child(count_label),
            )
            .child(div().flex_1())
            .child(
                div()
                    .id("clear-logs")
                    .px(px(12.))
                    .py(px(6.))
                    .rounded(px(8.))
                    .border_1()
                    .border_color(theme.border)
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .hover(|element| element.bg(theme.surface_raised).text_color(theme.text))
                    .cursor_pointer()
                    .on_click(cx.listener(|this, _, _, cx| {
                        this.clear_logs();
                        cx.notify();
                    }))
                    .child(self.t(k::LOG_BTN_CLEAR)),
            );

        let heading = div()
            .flex()
            .flex_col()
            .gap(px(3.))
            .child(
                div()
                    .text_size(px(18.))
                    .font_weight(FontWeight::SEMIBOLD)
                    .child(self.t(k::NAV_LOGS)),
            )
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(self.t(k::LOG_SUBTITLE)),
            );

        let card = if logs_empty {
            Self::log_card(
                theme,
                self.empty_state(self.t(k::LOG_EMPTY_TITLE), self.t(k::LOG_EMPTY_DESC), theme),
            )
        } else if filtered_empty {
            Self::log_card(
                theme,
                self.empty_state(
                    self.t(k::LOG_EMPTY_FILTERED_TITLE),
                    self.t(k::LOG_EMPTY_FILTERED_DESC),
                    theme,
                ),
            )
        } else {
            let row_theme = *theme;
            let row_rtl = self.rtl();
            let list = uniform_list(
                "audit-log",
                visible_len,
                cx.processor(move |this, range: std::ops::Range<usize>, _window, _cx| {
                    let state = this.log_state.borrow();
                    let mut rows = Vec::with_capacity(range.len());
                    for index in range {
                        let Some(entry_index) = state.visible_indices.get(index).copied() else {
                            continue;
                        };
                        let Some(entry) = state.logs.get(entry_index) else {
                            continue;
                        };
                        rows.push(Self::log_row(entry, &row_theme, row_rtl).into_any_element());
                    }
                    rows
                }),
            )
            .track_scroll(&self.log_scroll)
            .size_full();

            Self::log_card(
                theme,
                div()
                    .flex_1()
                    .flex()
                    .flex_col()
                    .overflow_hidden()
                    .child(self.log_header(theme))
                    .child(div().flex_1().overflow_hidden().child(list)),
            )
        };

        div()
            .flex_1()
            .flex()
            .flex_col()
            .gap(px(12.))
            .px(px(24.))
            .pb(px(12.))
            .child(heading)
            .child(toolbar)
            .child(card)
            .into_any_element()
    }

    fn log_card(theme: &Theme, content: impl IntoElement) -> Div {
        div()
            .flex_1()
            .flex()
            .flex_col()
            .overflow_hidden()
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(content)
    }

    fn log_header(&self, theme: &Theme) -> Div {
        div()
            .h(px(32.))
            .flex_none()
            .w_full()
            .flex()
            .mirror_row(self.rtl())
            .items_center()
            .gap(px(12.))
            .px(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .text_size(px(11.))
            .text_color(theme.text_faint)
            .child(div().w(px(64.)).flex_none().child(self.t(k::LOG_COL_TIME)))
            .child(
                div()
                    .w(px(150.))
                    .flex_none()
                    .child(self.t(k::LOG_COL_EVENT)),
            )
            .child(
                div()
                    .w(px(250.))
                    .flex_none()
                    .child(self.t(k::LOG_COL_SOURCE)),
            )
            .child(div().flex_1().child(self.t(k::LOG_COL_MESSAGE)))
    }

    fn log_row(entry: &AuditRecord, theme: &Theme, rtl: bool) -> Div {
        let tone = Self::tone_color(entry.tone, theme);
        div()
            .h(px(40.))
            .w_full()
            .flex()
            .mirror_row(rtl)
            .items_center()
            .gap(px(12.))
            .px(px(12.))
            .border_b_1()
            .border_color(theme.border)
            .hover(|element| element.bg(theme.surface_raised))
            .child(
                div()
                    .w(px(64.))
                    .flex_none()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(entry.time_text.clone()),
            )
            .child(
                div().w(px(150.)).flex_none().child(
                    div()
                        .px(px(8.))
                        .py(px(2.))
                        .rounded_full()
                        .bg(theme.surface_raised)
                        .text_size(px(11.))
                        .font_weight(FontWeight::MEDIUM)
                        .text_color(tone)
                        .truncate()
                        .child(entry.display_event.clone()),
                ),
            )
            .child(
                div()
                    .w(px(250.))
                    .flex_none()
                    .truncate()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(entry.source_path_masked.clone()),
            )
            .child(
                div()
                    .flex_1()
                    .truncate()
                    .text_size(px(12.))
                    .text_color(theme.text)
                    .child(entry.message.clone()),
            )
    }

    fn empty_state(&self, title: String, description: String, theme: &Theme) -> Div {
        div()
            .flex_1()
            .flex()
            .flex_col()
            .items_center()
            .justify_center()
            .gap(px(6.))
            .p(px(24.))
            .child(
                div()
                    .text_size(px(15.))
                    .font_weight(FontWeight::MEDIUM)
                    .text_color(theme.text)
                    .child(title),
            )
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text_muted)
                    .child(description),
            )
    }

    fn service_state_color(state: ServiceRuntimeState, theme: &Theme) -> Rgba {
        match state {
            ServiceRuntimeState::Running => theme.success,
            ServiceRuntimeState::StartPending | ServiceRuntimeState::ContinuePending => {
                theme.accent
            }
            ServiceRuntimeState::Paused
            | ServiceRuntimeState::PausePending
            | ServiceRuntimeState::StopPending
            | ServiceRuntimeState::Unknown => theme.warning,
            ServiceRuntimeState::Stopped => theme.text_muted,
            ServiceRuntimeState::NotInstalled => theme.text_faint,
        }
    }

    fn service_row(
        &self,
        label: String,
        description: String,
        action: impl IntoElement,
        theme: &Theme,
    ) -> Div {
        div()
            .flex()
            .mirror_row(self.rtl())
            .items_center()
            .justify_between()
            .gap(px(16.))
            .py(px(10.))
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(3.))
                    .child(div().text_size(px(13.)).text_color(theme.text).child(label))
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(description),
                    ),
            )
            .child(action)
    }

    #[allow(clippy::too_many_arguments)] // 与 config_service 同款：按钮渲染的固有形状参数。
    fn action_button(
        &self,
        id: &'static str,
        label: String,
        command: ServiceCommand,
        enabled: bool,
        kind: ButtonKind,
        theme: &Theme,
        commands: mpsc::UnboundedSender<ServiceCommand>,
        cx: &mut Context<Self>,
    ) -> impl IntoElement {
        let (background, foreground, border) = if !enabled {
            (theme.surface_raised, theme.text_faint, theme.border)
        } else {
            match kind {
                ButtonKind::Primary => (theme.accent, theme.on_accent, theme.accent),
                ButtonKind::Ghost => (theme.surface_raised, theme.text, theme.border),
                ButtonKind::Danger => (theme.danger, theme.on_accent, theme.danger),
            }
        };

        let button = div()
            .id(id)
            .px(px(12.))
            .py(px(6.))
            .rounded(px(7.))
            .border_1()
            .border_color(border)
            .bg(background)
            .text_size(px(12.))
            .text_color(foreground)
            .child(label);

        if !enabled {
            return button;
        }

        let on_click = cx.listener(move |_, _, _, _| {
            let _ = commands.send(command);
        });
        let button = button.cursor_pointer().on_click(on_click);
        if matches!(kind, ButtonKind::Ghost) {
            button.hover(|element| element.bg(theme.surface))
        } else {
            // 实心主按钮/危险按钮的 hover 改成 surface 会让 on_accent 前景失去对比度，只保留点击。
            button
        }
    }

    fn divider(theme: &Theme) -> Div {
        div().h(px(1.)).bg(theme.border)
    }

    fn feedback_banner(&self, feedback: &ServiceFeedback, theme: &Theme) -> Div {
        let (color, label) = match feedback.status {
            photoprivacy_service::ServiceCommandStatus::Success => {
                (theme.success, self.t(k::SERVICE_RESULT_SUCCESS))
            }
            photoprivacy_service::ServiceCommandStatus::Skipped => {
                (theme.text_muted, self.t(k::SERVICE_RESULT_SKIPPED))
            }
            photoprivacy_service::ServiceCommandStatus::ElevationCancelled => {
                (theme.warning, self.t(k::SERVICE_RESULT_CANCELLED))
            }
            photoprivacy_service::ServiceCommandStatus::Failed => {
                (theme.danger, self.t(k::SERVICE_RESULT_FAILED))
            }
        };

        // 服务 crate 返回英文消息；带退出码的失败用 service.exitcode.* 键翻译（#60），
        // 无退出码的失败（spawn/超时/提权）或无对应键的未知退出码保留 crate 的具体原因。
        // service.exitcode.* 是 Windows 退出码语义；Linux/macOS 的 systemctl/launchctl/pkexec
        // 退出码域不同，保留 crate 的平台适配消息，不做跨域误译。
        let exit_code = if cfg!(windows) {
            match feedback.status {
                photoprivacy_service::ServiceCommandStatus::Failed => {
                    feedback.exit_code.filter(|&code| code != 0)
                }
                _ => None,
            }
        } else {
            None
        };
        let exit_code_key = exit_code.map(|code| format!("service.exitcode.{code}"));
        let message = match &exit_code_key {
            Some(key) if self.i18n.contains(&self.config.ui.locale, key) => self.t(key),
            _ => feedback.message.clone(),
        };

        div()
            .flex()
            .items_center()
            .gap(px(8.))
            .p(px(10.))
            .rounded(px(8.))
            .border_1()
            .border_color(color)
            .bg(theme.surface)
            .child(
                div()
                    .text_size(px(11.))
                    .font_weight(FontWeight::MEDIUM)
                    .text_color(color)
                    .child(label),
            )
            .child(
                div()
                    .text_size(px(12.))
                    .text_color(theme.text)
                    .child(message),
            )
    }

    fn service_page(&self, theme: &Theme, cx: &mut Context<Self>) -> Div {
        let service = self.service.clone();
        let commands = self.service_commands.clone();
        let buttons = button_state(service.state);
        let busy = service.busy;
        let enabled = |available: bool| available && !busy;

        let status_dot = Self::service_state_color(service.state, theme);
        let status_text = match service.state {
            ServiceRuntimeState::NotInstalled => self.t(k::SERVICE_STATE_NOT_INSTALLED),
            ServiceRuntimeState::Running => self.t(k::SERVICE_STATE_RUNNING),
            ServiceRuntimeState::StartPending => self.t(k::SERVICE_STATE_START_PENDING),
            ServiceRuntimeState::PausePending => self.t(k::SERVICE_STATE_PAUSE_PENDING),
            ServiceRuntimeState::Paused => self.t(k::SERVICE_STATE_PAUSED),
            ServiceRuntimeState::ContinuePending => self.t(k::SERVICE_STATE_CONTINUE_PENDING),
            ServiceRuntimeState::Stopped => self.t(k::SERVICE_STATE_STOPPED),
            ServiceRuntimeState::StopPending => self.t(k::SERVICE_STATE_STOP_PENDING),
            ServiceRuntimeState::Unknown => self.t(k::SERVICE_STATE_UNKNOWN),
        };
        let status_row = self.service_row(
            self.t(k::SERVICE_STATUS),
            if busy {
                self.t(k::SERVICE_DESC_APPLYING)
            } else {
                self.t(k::SERVICE_DESC_REFRESH_3S)
            },
            div()
                .flex()
                .items_center()
                .gap(px(8.))
                .child(div().size(px(8.)).rounded_full().bg(status_dot))
                .child(
                    div()
                        .text_size(px(13.))
                        .font_weight(FontWeight::MEDIUM)
                        .text_color(theme.text)
                        .child(status_text),
                ),
            theme,
        );

        let install_row = self.service_row(
            self.t(k::SERVICE_BTN_INSTALL),
            self.t(k::SERVICE_DESC_INSTALL),
            self.action_button(
                "service-install",
                self.t(k::SERVICE_BTN_INSTALL_SHORT),
                ServiceCommand::Install,
                enabled(buttons.install),
                ButtonKind::Primary,
                theme,
                commands.clone(),
                cx,
            ),
            theme,
        );

        let start_stop_row = self.service_row(
            self.t(k::SERVICE_ROW_START_STOP),
            self.t(k::SERVICE_DESC_START_STOP),
            div()
                .flex()
                .items_center()
                .gap(px(6.))
                .child(self.action_button(
                    "service-start",
                    self.t(k::SERVICE_BTN_START),
                    ServiceCommand::Start,
                    enabled(buttons.start),
                    ButtonKind::Ghost,
                    theme,
                    commands.clone(),
                    cx,
                ))
                .child(self.action_button(
                    "service-stop",
                    self.t(k::SERVICE_BTN_STOP),
                    ServiceCommand::Stop,
                    enabled(buttons.stop),
                    ButtonKind::Ghost,
                    theme,
                    commands.clone(),
                    cx,
                ))
                .child(self.action_button(
                    "service-refresh",
                    self.t(k::SERVICE_BTN_REFRESH),
                    ServiceCommand::Refresh,
                    true,
                    ButtonKind::Ghost,
                    theme,
                    commands.clone(),
                    cx,
                )),
            theme,
        );

        let uninstall_row = self.service_row(
            self.t(k::SERVICE_BTN_UNINSTALL),
            self.t(k::SERVICE_DESC_UNINSTALL),
            self.action_button(
                "service-uninstall",
                self.t(k::SERVICE_BTN_UNINSTALL_SHORT),
                ServiceCommand::Uninstall,
                enabled(buttons.uninstall),
                ButtonKind::Danger,
                theme,
                commands,
                cx,
            ),
            theme,
        );

        let card = div()
            .flex()
            .flex_col()
            .px(px(16.))
            .bg(theme.surface)
            .border_1()
            .border_color(theme.border)
            .rounded(px(12.))
            .child(status_row)
            .child(Self::divider(theme))
            .child(install_row)
            .child(Self::divider(theme))
            .child(start_stop_row)
            .child(Self::divider(theme))
            .child(uninstall_row);

        div()
            .flex()
            .flex_col()
            .gap(px(10.))
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child(self.t(k::SERVICE_SECTION)),
            )
            .child(card)
            .children(
                service
                    .feedback
                    .as_ref()
                    .map(|feedback| self.feedback_banner(feedback, theme)),
            )
    }
}

fn field_value(config: &AppConfig, id: FieldId) -> &str {
    (id.spec().value)(config)
}

fn field_value_mut(config: &mut AppConfig, id: FieldId) -> &mut String {
    (id.spec().value_mut)(config)
}

fn floor_char_boundary(text: &str, mut index: usize) -> usize {
    index = index.min(text.len());
    while index > 0 && !text.is_char_boundary(index) {
        index -= 1;
    }
    index
}

fn prev_char_boundary(text: &str, cursor: usize) -> usize {
    text[..cursor]
        .char_indices()
        .next_back()
        .map(|(index, _)| index)
        .unwrap_or(0)
}

fn next_char_boundary(text: &str, cursor: usize) -> usize {
    text[cursor..]
        .char_indices()
        .nth(1)
        .map(|(index, _)| cursor + index)
        .unwrap_or(text.len())
}

fn or_dash(value: Option<String>) -> String {
    value
        .filter(|value| !value.is_empty())
        .unwrap_or_else(|| "—".to_string())
}

impl Render for AppView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = Theme::resolve(self.theme_mode.get(), theme::is_dark(window.appearance()));
        let rtl = self.rtl();
        let header = div()
            .flex()
            .mirror_row(rtl)
            .items_center()
            .gap(px(16.))
            .px(px(24.))
            .pt(px(20.))
            .pb(px(10.))
            .child(
                div()
                    .flex()
                    .flex_col()
                    .gap(px(2.))
                    .child(
                        div()
                            .text_size(px(18.))
                            .font_weight(FontWeight::SEMIBOLD)
                            .child(self.t(k::APP_TITLE)),
                    )
                    .child(
                        div()
                            .text_size(px(11.))
                            .text_color(theme.text_muted)
                            .child(self.t(k::APP_SUBTITLE)),
                    ),
            )
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap(px(2.))
                    .p(px(2.))
                    .bg(theme.surface_raised)
                    .border_1()
                    .border_color(theme.border)
                    .rounded(px(9.))
                    .children(Page::all().into_iter().map(|page| {
                        let label = self.t(page.key());
                        Self::nav_tab(page, self.page, label, &theme, cx)
                    })),
            )
            .child(div().flex_1())
            .child(self.theme_switch(self.theme_mode.get(), &theme, cx));

        let content = match self.page {
            Page::Overview => self.overview_body(&theme, cx),
            Page::Config => div()
                .flex_1()
                .min_h(px(0.))
                .id("config-scroll")
                .overflow_y_scroll()
                .px(px(24.))
                .pb(px(16.))
                .child(self.config_page(window, cx))
                .into_any_element(),
            Page::Service => self.service_page(&theme, cx).into_any_element(),
            Page::Logs => self.logs_body(&theme, cx),
            Page::Rules => rules_page::render(self, &theme, cx).into_any_element(),
        };

        let footer = div()
            .flex()
            .mirror_row(rtl)
            .items_center()
            .justify_between()
            .gap(px(16.))
            .px(px(24.))
            .py(px(14.))
            .child(
                div()
                    .flex_1()
                    .min_w(px(0.))
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .truncate()
                    .child(self.config_path.display().to_string()),
            )
            .child(
                div()
                    .text_size(px(11.))
                    .text_color(theme.text_faint)
                    .child(concat!("v", env!("CARGO_PKG_VERSION"))),
            );

        div()
            .size_full()
            .bg(theme.bg)
            .text_color(theme.text)
            .flex()
            .flex_col()
            .child(header)
            .child(content)
            .child(footer)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn field_ids_and_specs_stay_aligned() {
        // 防：FieldId 判别式与 FIELD_SPECS 顺序错位，编辑一个字段却写到另一个字段（#55）。
        assert_eq!(FIELD_SPECS.len(), FieldId::ALL.len());
        for (index, id) in FieldId::ALL.into_iter().enumerate() {
            assert_eq!(
                id as usize, index,
                "{id:?} 的判别式必须等于它在 ALL 中的位置"
            );
            assert_eq!(id.spec().element_id, id.element_id());
        }
        let mut element_ids: Vec<&str> = FIELD_SPECS.iter().map(|spec| spec.element_id).collect();
        element_ids.sort_unstable();
        element_ids.dedup();
        assert_eq!(element_ids.len(), FIELD_SPECS.len(), "element_id 不可重复");
    }
}
