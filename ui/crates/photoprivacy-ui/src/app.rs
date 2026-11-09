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
    AnyElement, App, Bounds, BoxShadow, ClipboardItem, Context, CursorStyle, Div, FocusHandle,
    FontWeight, IntoElement, KeyBinding, KeyDownEvent, MouseButton, PathPromptOptions, Pixels,
    Render, Rgba, ScrollHandle, ScrollStrategy, Stateful, StyleRefinement, Styled, Subscription,
    UniformListScrollHandle, Window, actions, div, px, uniform_list,
};
use photoprivacy_ipc::{
    ConnectFailureCause, ConnectionSnapshot, ConnectionState, ServiceReachability, WorkerMode,
};
use photoprivacy_logs::{AuditRecord, EventTone, LevelFilter, LogBuffer};
use photoprivacy_rules::RulesEditor;
use photoprivacy_service::{ServiceRuntimeState, button_state};
use std::cell::{Cell, RefCell};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::rc::Rc;
use std::time::{Duration, Instant};
use tokio::sync::{mpsc, watch};

const SNAPSHOT_POLL_INTERVAL: Duration = Duration::from_millis(200);
const CONTROL_WIDTH: f32 = 430.0;
/// 旧 UI 的 SaveStatus 3 秒后自动清空（MainWindow.ScheduleSaveStatusClear）。
const SAVE_STATUS_TTL: Duration = Duration::from_secs(3);
/// 复制反馈显示时长。
const COPY_FEEDBACK_TTL: Duration = Duration::from_secs(2);

// Tab/Shift+Tab 绑定到焦点遍历动作，动作回调只调 window.focus_next/prev；
// Enter/Space 激活沿用 gpui 内建的合成点击（focusable + on_click 控件），不再自管按键。
actions!(photoprivacy_ui, [FocusNext, FocusPrev]);

/// 登记焦点遍历按键。必须只在 app 启动时调用一次：重复登记会让同一按键重复派发动作，
/// 一次 Tab 跳过多个控件。
pub(crate) fn bind_focus_keys(cx: &mut App) {
    cx.bind_keys([
        KeyBinding::new("tab", FocusNext, None),
        KeyBinding::new("shift-tab", FocusPrev, None),
    ]);
}

/// 焦点轮廓：元素内侧边缘 2px 实边环（inset 阴影）。不能用 border——宽度变化撑动布局，
/// Tab 遍历时内容抖动；也不能用外扩阴影——无底色控件（未激活导航/开关行/复选格）
/// 的内区会被整块填充而非环形。inset 环不参与布局，透明/实心底上都只出环。
/// 默认传 `theme.accent`；实心强调色底控件（主按钮、选中 chip、保存按钮）传
/// `theme.on_accent`，避免强调色环淹没在同色底里看不清。
pub(crate) fn focus_ring(ring: Rgba) -> impl FnOnce(StyleRefinement) -> StyleRefinement {
    move |style| {
        style.shadow(vec![
            BoxShadow::new(px(0.), px(0.), ring.into())
                .spread_radius(px(2.))
                .inset(),
        ])
    }
}

/// 配置页可聚焦控件登记：稳定句柄（跨渲染身份不变）+ 布局探针（供滚动揭示取坐标）。
// Tab/Shift+Tab 移焦后必须把聚焦控件滚入 config-scroll 视口，否则焦点环在
// 视口外不可见、键盘用户失位；探针不设 overflow，滚轮 delta 恒为 0 且不吞事件，
// 只借 track_scroll 记录控件窗口坐标。
#[derive(Default)]
pub(crate) struct FocusRegistry {
    handles: HashMap<String, FocusHandle>,
    probes: HashMap<String, ScrollHandle>,
}

impl FocusRegistry {
    /// 取指定 id 的焦点句柄；不存在时按 tab 停靠创建并登记。
    fn focus_handle(&mut self, id: &str, cx: &mut App) -> FocusHandle {
        self.handles
            .entry(id.to_string())
            .or_insert_with(|| cx.focus_handle().tab_stop(true))
            .clone()
    }

    /// 取（或登记）指定探针句柄，用于回读控件最近一次布局坐标。
    fn probe(&mut self, id: &str) -> ScrollHandle {
        self.probes.entry(id.to_string()).or_default().clone()
    }

    /// 聚焦控件最近一次布局的窗口坐标；未登记或未绘制过则 None。
    fn bounds_of(&self, handle: &FocusHandle) -> Option<Bounds<Pixels>> {
        let id = self
            .handles
            .iter()
            .find(|(_, candidate)| *candidate == handle)
            .map(|(id, _)| id)?;
        self.probes.get(id).map(|probe| probe.bounds())
    }
}

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
    /// 用初始配置建立配置页状态：保存态空闲、无状态提示、编辑代际归零。
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
                let reload_failed = worker_error.is_some();
                self.save_state = match worker_error {
                    Some(error) => SaveState::SavedWorkerDown(error),
                    None => SaveState::Saved,
                };
                // 只有成功态按 TTL 淡出。失败态（配置已落盘但 Worker 未重载）
                // 常驻到下一次保存成功——否则阻断原因随 3 s 消失，用户被绕回「请前往配置页」，
                // 看不到「ExifTool 缺失 / 目录不可写」这类真正原因。
                self.save_status_deadline = if reload_failed {
                    None
                } else {
                    Some(Instant::now() + SAVE_STATUS_TTL)
                };
                theme
            }
            ConfigEvent::SaveFailed(error) => {
                self.save_state = SaveState::Failed(error);
                // 同上：保存失败原因常驻，不随 3 s 过期。
                self.save_status_deadline = None;
                None
            }
        }
    }

    /// 替换当前配置并重算派生状态（脏标记与字段文本同步）。
    fn set_config(&mut self, mut config: AppConfig, auto_detect_exiftool: bool) -> ThemeMode {
        config.ui.theme_variant = normalize_theme_variant(&config.ui.theme_variant).to_string();
        // 旧 UI 的 ResolveExifToolPath：首次加载时当前路径不存在则探测并显示提示；
        // 外部回灌/保存回读不清掉已有提示（旧 UI 只在启动时设置 hint）。
        if auto_detect_exiftool {
            // 空路径 = 按运行时探测，只探测给提示，绝不写回 config.exiftool.path——
            // UI 是 config.json 的写入方，写回会把「探测」冻结成绝对路径，包换个目录就指向旧位置。
            if config.exiftool.path.trim().is_empty() {
                self.exiftool_hint = detect_exiftool_path(&config.exiftool.path);
            } else {
                // #33: 非空用户输入先规整首尾空白再判定/保存——检测与 Worker 都不接受带空白的路径
                // （IsPathFullyQualified / File.Exists 判否），否则 UI 提示成功、保存后 Worker 拒绝。
                config.exiftool.path = config.exiftool.path.trim().to_string();
                if let Some(detected) = detect_exiftool_path(&config.exiftool.path) {
                    // #33: 目录式覆盖保留用户填的目录本身，不把解析出的 exe 路径写回
                    // config.json（否则一次保存就把目录冻结成文件绝对路径，发布包换目录即失效）。
                    // hint 仍显示解析结果，让用户确认目录内确实有可用的 exiftool。
                    if std::path::Path::new(&config.exiftool.path).is_dir() {
                        self.exiftool_hint = Some(detected);
                    } else {
                        config.exiftool.path = detected.clone();
                        self.exiftool_hint = Some(detected);
                    }
                } else {
                    self.exiftool_hint = None;
                }
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

    /// 该字段的静态规格（元素 id、值访问器、是否文件型）。
    fn spec(self) -> &'static FieldSpec {
        &FIELD_SPECS[self as usize]
    }

    /// 该字段输入框的稳定元素 id。
    fn element_id(self) -> &'static str {
        self.spec().element_id
    }

    /// 该字段「浏览」按钮的稳定元素 id。
    fn browse_id(self) -> &'static str {
        self.spec().browse_id
    }

    /// 该字段占位文案的 i18n key。
    fn placeholder_key(self) -> &'static str {
        self.spec().placeholder_key
    }

    /// 该字段是否以文件路径方式配置（决定是否出现浏览按钮）。
    fn files(self) -> bool {
        self.spec().files
    }
}

/// 读取 ExifTool 可执行路径。
fn exiftool_path(config: &AppConfig) -> &str {
    &config.exiftool.path
}

/// 以可写方式拿到 ExifTool 可执行路径。
fn exiftool_path_mut(config: &mut AppConfig) -> &mut String {
    &mut config.exiftool.path
}

/// 读取监控目录。
fn hot_folder(config: &AppConfig) -> &str {
    &config.watch.hot_folder
}

/// 以可写方式拿到监控目录。
fn hot_folder_mut(config: &mut AppConfig) -> &mut String {
    &mut config.watch.hot_folder
}

/// 读取备份目录。
fn backup_directory(config: &AppConfig) -> &str {
    &config.backup.directory
}

/// 以可写方式拿到备份目录。
fn backup_directory_mut(config: &mut AppConfig) -> &mut String {
    &mut config.backup.directory
}

/// 读取隔离目录。
fn quarantine_directory(config: &AppConfig) -> &str {
    &config.quarantine.directory
}

/// 以可写方式拿到隔离目录。
fn quarantine_directory_mut(config: &mut AppConfig) -> &mut String {
    &mut config.quarantine.directory
}

/// 读取审计日志目录。
fn audit_log_directory(config: &AppConfig) -> &str {
    &config.audit.log_directory
}

/// 以可写方式拿到审计日志目录。
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
pub enum Page {
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

    /// 该取值的 i18n 文案 key。
    fn key(self) -> &'static str {
        match self {
            Self::Overview => k::NAV_OVERVIEW,
            Self::Config => k::NAV_CONFIG,
            Self::Service => k::NAV_SERVICE,
            Self::Logs => k::NAV_LOGS,
            Self::Rules => k::NAV_RULES,
        }
    }

    /// `--open-page` 取值解析：与 `id()` 互逆；未知 id 返回 None（调用方按参数错误处理）。
    pub fn from_id(id: &str) -> Option<Self> {
        match id {
            "overview" => Some(Self::Overview),
            "config" => Some(Self::Config),
            "service" => Some(Self::Service),
            "logs" => Some(Self::Logs),
            "rules" => Some(Self::Rules),
            _ => None,
        }
    }

    /// 枚举的全部取值（渲染筛选/下拉时遍历用）。
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
    /// `--open-page` 指定的启动页；None 时默认总览（虚拟环境测试截图用）。
    pub open_page: Option<Page>,
}

/// RTL 语言下主轴行反向，行内「标签 + 控件」的左右顺序整体镜像。
/// gpui 0.3.10 没有 `flex_direction` 通用设置器，只有 row/row_reverse 便捷方法，
/// 用扩展 trait 保持调用方的链式写法。
pub(crate) trait MirrorRow: Styled + Sized {
    /// 按 RTL 需求翻转行方向。
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

/// Windows 退出码 1053（ERROR_SERVICE_REQUEST_TIMEOUT）的两种成因（#202 AC1）。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum StartTimeoutCause {
    /// #162 单写者准入门让位：服务 Worker 撞上已被占用的审计目录后干净退出，
    /// SCM 等不到 RUNNING，只能回 1053。
    SingleWriterYield,
    /// ExifTool/配置/提权等真超时，沿用 service.exitcode.1053 原文案。
    TrueTimeout,
}

/// 1053 归因判断规则：UI 此刻确实连着后台 Worker，且两边的审计目录相同，
/// 才能断定服务 Worker 是撞上同一份目录的准入门而让位，1053 是确定成因而非猜测；
/// 不连后台 Worker 或目录不同都保持真超时归因。
///
/// 连接态必须一起判（CodeRabbit 返修）：`worker_mode` 是上一次成功握手留下的残留值，
/// 连接失败后仍可能是 `Background`；只看它会把真超时误标成单写者让位，威胁 AC3。
///
/// 目录也必须一起判（#218）：服务进程读的是安装那一刻的 `config.json`，UI 这边可能已经
/// 换成另一份审计目录；此时两个 Worker 并不争同一份目录，服务端 ExifTool/配置类真超时
/// 会被硬贴上让位文案，用户照文案去关后台 Worker 也修不好。
fn start_timeout_cause(
    exit_code: i32,
    connection: ConnectionState,
    worker_mode: Option<WorkerMode>,
    audit_dirs_match: bool,
) -> StartTimeoutCause {
    if exit_code == 1053
        && connection == ConnectionState::Connected
        && worker_mode == Some(WorkerMode::Background)
        && audit_dirs_match
    {
        StartTimeoutCause::SingleWriterYield
    } else {
        StartTimeoutCause::TrueTimeout
    }
}

/// 审计目录等值判定（#218）：同一目录可能被写成 `C:\a\_audit`、`C:\a\_audit\` 两种形态，
/// Windows 下还大小写不敏感、`/` 与 `\` 等价（文件夹选择器给 `\`，手改 `config.json` 可能写
/// `/`）；不做归一化会把"其实同目录"误判成真超时，让文案回归（CodeRabbit 返修）。
fn audit_directory_key(directory: &str) -> String {
    let trimmed = directory.trim();
    if cfg!(windows) {
        let normalized = trimmed.replace('/', "\\").to_lowercase();
        // 盘根（`C:\`）的尾分隔符属于根本身：剥掉就变成盘相对路径 `C:`，两者含义不同
        // （CodeRabbit 返修）。只对盘根保留尾分隔符，其余目录照旧剥掉。
        // 判定内联而不抽成独立函数：非 Windows 目标下没有调用点，抽出去会被 clippy
        // 的 `-D warnings` 判成 dead_code。
        let bytes = normalized.as_bytes();
        let is_drive_root = bytes.len() == 3
            && bytes[0].is_ascii_alphabetic()
            && bytes[1] == b':'
            && bytes[2] == b'\\';
        if is_drive_root {
            return normalized;
        }
        normalized.trim_end_matches('\\').to_string()
    } else {
        trimmed.trim_end_matches('/').to_string()
    }
}

/// 服务侧审计目录（#218）：服务进程启动时读的是安装那一刻的 `config.json`。
/// 读不到（文件被移走/解析失败）返回 `None`——调用方按"无法证明目录相同"处理，
/// 宁可归真超时也不误标成单写者让位。
fn service_audit_directory(config_path: &Path) -> Option<String> {
    if !config_path.is_file() {
        return None;
    }
    crate::config_file::ConfigFile::new(config_path)
        .load_or_default()
        .ok()
        .map(|config| config.audit.log_directory)
}

/// 1053 归因判断的三态结论：`Unknown` 表示服务侧配置读不到/解析失败，
/// 无法证明两个目录是否相同。它与"确定不同"必须分开——读盘失败若被当成 `Mismatched`
/// 缓存下来，配置事后修好也不会重算，1053 会被长期误判成真超时。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum AuditDirsMatch {
    Matched,
    Mismatched,
    Unknown,
}

impl AuditDirsMatch {
    /// 调用方只关心"能否确定目录相同"（`Unknown` 一律保守当不成立）。
    fn is_matched(self) -> bool {
        matches!(self, Self::Matched)
    }

    /// 只有确定结论才值得缓存；读盘/解析失败是暂态，不缓存以免钉死成 `false`。
    fn is_cacheable(self) -> bool {
        !matches!(self, Self::Unknown)
    }
}

/// 1053 归因所需的"两边审计目录是否相同"事实来源（#218）：后台 Worker 用的是 UI 内存
/// 里这份 `config`，服务用的是磁盘上安装时留下的那份，二者的 `audit.log_directory`
/// 归一化后比对。
fn audit_dirs_match_verdict(config: &AppConfig, config_path: &Path) -> AuditDirsMatch {
    let worker_key = audit_directory_key(&config.audit.log_directory);
    if worker_key.is_empty() {
        // 两边都没写目录时各自落到自己的默认目录，不能凭"都是空"就当成同一份。
        return AuditDirsMatch::Mismatched;
    }
    match service_audit_directory(config_path) {
        None => AuditDirsMatch::Unknown,
        Some(service_dir) => {
            if audit_directory_key(&service_dir) == worker_key {
                AuditDirsMatch::Matched
            } else {
                AuditDirsMatch::Mismatched
            }
        }
    }
}

/// 布尔视图（`Unknown` 按"不成立"处理）——供不关心三态区分的调用方与回归断言使用。
fn connected_audit_dirs_match(config: &AppConfig, config_path: &Path) -> bool {
    audit_dirs_match_verdict(config, config_path).is_matched()
}

/// 1053 归因判断的缓存键：反馈身份 + UI 侧配置 + 配置路径。任一变化
/// 都需要重算——反馈换了就是新的 1053 事件，配置目录或配置路径换了则服务侧读的目录可能
/// 已经不是同一个。
#[derive(Clone, PartialEq, Eq)]
struct AuditDirsMatchKey {
    exit_code: Option<i32>,
    message: String,
    config_path: PathBuf,
    worker_audit_directory: String,
}

/// 连接失败三态 → 分类文案（#169）：授权拒绝给行动指引——指引按平台分键（CodeRabbit 返修）：
/// Windows 是安装脚本的 `-AuthorizedUsers` 集合，Unix 是服务 socket 的 0660+共享组边界
/// （`install-systemd-service.sh --gui-user` / launchd 同构）；后台未就绪附启动/退出细节；
/// 未安装引导重装。WorkerStartupFailed 的 `{0}` 是编排侧生成的自描述细节
/// （spawn 错误 / 退出码 / 5s 未就绪）。
fn connect_failure_text(catalog: &Catalog, locale: &str, cause: &ConnectFailureCause) -> String {
    match cause {
        ConnectFailureCause::ServiceAccessDenied => {
            let key = if cfg!(windows) {
                k::STATUS_CONNECT_DENIED
            } else {
                k::STATUS_CONNECT_DENIED_UNIX
            };
            catalog.get(locale, key)
        }
        ConnectFailureCause::WorkerStartupFailed(detail) => {
            catalog.get_format(locale, k::STATUS_WORKER_STARTUP_FAILED, &[detail])
        }
        ConnectFailureCause::WorkerNotInstalled => {
            catalog.get(locale, k::STATUS_WORKER_NOT_INSTALLED)
        }
    }
}

/// 连接失败三态 → 呈现色（#169）：授权拒绝是「找安装者」的引导（warning，同配置页引导色）；
/// Worker 缺失/未就绪维持错误红。
fn connect_failure_color(cause: &ConnectFailureCause, theme: &Theme) -> Rgba {
    match cause {
        ConnectFailureCause::ServiceAccessDenied => theme.warning,
        ConnectFailureCause::WorkerStartupFailed(_) | ConnectFailureCause::WorkerNotInstalled => {
            theme.danger
        }
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
    /// 根聚焦句柄：启动时占住焦点，Tab/Shift+Tab 从这里进入控件链；自身不是 tab stop。
    focus_root: FocusHandle,
    /// 配置页滚动容器句柄：Tab 移焦后据此把聚焦控件滚入视口。
    config_scroll: ScrollHandle,
    /// 配置页控件的稳定焦点句柄与布局探针登记。
    config_registry: Rc<RefCell<FocusRegistry>>,
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
    /// 日志行复制按钮的稳定焦点句柄（entry_index → 句柄）：虚拟列表 Tab 边界滚动跟进
    /// 需要按行反查/直取相邻行句柄（见 `Self::step_log_copy_focus`）。
    log_copy_focus: RefCell<HashMap<usize, FocusHandle>>,
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
    /// 复制反馈：记录触发反馈的按钮 ID 与截止时间，仅该按钮显示"已复制"。
    copy_feedback: Option<(String, Instant)>,
    /// 1053 归因判断缓存：`feedback_banner` 处在渲染路径上，若每帧都调
    /// `connected_audit_dirs_match` 就会每帧 `is_file()` + 读 `config.json`，慢盘/网络配置文件
    /// 会把渲染拖住。这里按"反馈身份 + UI 侧配置 + 配置路径"缓存结果，只在键变化时重算一次。
    audit_dirs_match_cache: RefCell<Option<(AuditDirsMatchKey, bool)>>,
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
            open_page,
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
        let config_registry = Rc::new(RefCell::new(FocusRegistry::default()));
        let mut fields = HashMap::new();
        for id in FieldId::ALL {
            // 输入框句柄经登记表创建（tab_stop 已置），Tab/Shift+Tab 依序经过全部字段。
            let focus = config_registry
                .borrow_mut()
                .focus_handle(id.element_id(), cx);
            fields.insert(
                id,
                FieldState {
                    focus,
                    cursor: field_value(&config, id).len(),
                },
            );
        }
        let focus_root = cx.focus_handle();
        // 启动即聚焦根句柄：无焦点时按键无人认领，Tab 进不了控件链（焦点可见示例同款做法）。
        window.focus(&focus_root, cx);
        let view = Self {
            page: open_page.unwrap_or(Page::Overview),
            focus_root,
            config_scroll: ScrollHandle::new(),
            config_registry,
            snapshot,
            snapshots,
            theme_mode,
            log_state,
            service,
            service_snapshots,
            service_commands,
            log_service,
            log_scroll: UniformListScrollHandle::new(),
            log_copy_focus: RefCell::new(HashMap::new()),
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
            copy_feedback: None,
            audit_dirs_match_cache: RefCell::new(None),
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

    /// Tab：日志列表边界滚动跟进优先（见 `Self::step_log_copy_focus`），其余走焦点遍历，
    /// 随后把聚焦控件滚入配置页视口。
    fn on_focus_next(&mut self, _action: &FocusNext, window: &mut Window, cx: &mut Context<Self>) {
        if !self.step_log_copy_focus(true, window, cx) {
            window.focus_next(cx);
            self.reveal_focused(window, cx);
        }
    }

    /// Shift+Tab：日志列表边界对称回退（见 `Self::step_log_copy_focus`），其余走焦点遍历。
    fn on_focus_prev(&mut self, _action: &FocusPrev, window: &mut Window, cx: &mut Context<Self>) {
        if !self.step_log_copy_focus(false, window, cx) {
            window.focus_prev(cx);
            self.reveal_focused(window, cx);
        }
    }

    /// 日志行复制按钮的稳定焦点句柄：按 entry_index 登记创建（tab_stop 已置），跨渲染身份不变。
    fn log_copy_handle(&self, entry_index: usize, cx: &mut Context<Self>) -> FocusHandle {
        self.log_copy_focus
            .borrow_mut()
            .entry(entry_index)
            .or_insert_with(|| cx.focus_handle().tab_stop(true))
            .clone()
    }

    /// 反查句柄归属：是日志行复制按钮时返回其 entry_index。
    fn log_copy_entry_of(&self, handle: &FocusHandle) -> Option<usize> {
        self.log_copy_focus
            .borrow()
            .iter()
            .find(|(_, candidate)| *candidate == handle)
            .map(|(entry, _)| *entry)
    }

    // 虚拟列表只把可见行的复制按钮登记进 tab 链，末个可见行再按 Tab 会在
    // 到达后续行之前回绕——移焦后检测回绕（焦点不再是日志行按钮），其后（前）仍有行就
    // 滚动揭示相邻行并聚焦其复制按钮；已是过滤列表首/末行则不接管，Tab 照常离开列表。
    // 返回 true 表示本次 Tab 已完全处置（列表内步进或边界跟进），调用方不再 focus_next/prev。
    /// 日志页 Tab 键在可复制文本之间前后移动焦点，成功返回 true。
    fn step_log_copy_focus(
        &self,
        forward: bool,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> bool {
        let Some(before) = window.focused(cx) else {
            return false;
        };
        let Some(entry_index) = self.log_copy_entry_of(&before) else {
            return false;
        };
        let state = self.log_state.borrow();
        let visible = &state.visible_indices;
        let position = match visible.iter().position(|entry| *entry == entry_index) {
            Some(position) => position,
            // 过滤变化后残留的陈旧焦点（行已不在列表）按默认移焦路径走。
            None => return false,
        };
        let next_position = if forward {
            position.checked_add(1)
        } else {
            position.checked_sub(1)
        };
        let next_position = match next_position {
            Some(next_position) if next_position < visible.len() => next_position,
            // 已是过滤列表首/末行：不接管，焦点遍历自然回绕离开列表。
            _ => return false,
        };
        let next_entry = visible[next_position];
        drop(state);
        if forward {
            window.focus_next(cx);
        } else {
            window.focus_prev(cx);
        }
        if let Some(after) = window.focused(cx)
            && self.log_copy_entry_of(&after).is_some()
        {
            // 下一行已渲染：列表内正常步进，处置完毕。
            return true;
        }
        // 回绕离表且仍有相邻行：滚动揭示并聚焦其复制按钮（deferred scroll 随下一帧生效）。
        let strategy = if forward {
            ScrollStrategy::Bottom
        } else {
            ScrollStrategy::Top
        };
        self.log_scroll.scroll_to_item(next_position, strategy);
        let next_focus = self.log_copy_handle(next_entry, cx);
        window.focus(&next_focus, cx);
        cx.notify();
        true
    }

    /// 配置页控件的焦点接线：从登记表取稳定句柄 + 布局探针（跨渲染身份不变）。
    fn config_focus(&self, id: &str, cx: &mut Context<Self>) -> (FocusHandle, ScrollHandle) {
        let mut registry = self.config_registry.borrow_mut();
        (registry.focus_handle(id, cx), registry.probe(id))
    }

    /// 移焦后把 config-scroll 视口外的聚焦控件以最小位移滚入视口。
    /// 只对配置页正文控件生效（登记表外的导航/页头/其他页控件不滚；非配置页视口高为 0 直接跳过）。
    fn reveal_focused(&self, window: &mut Window, cx: &mut Context<Self>) {
        let Some(focused) = window.focused(cx) else {
            return;
        };
        let Some(target) = self.config_registry.borrow().bounds_of(&focused) else {
            return;
        };
        let viewport = self.config_scroll.bounds();
        if viewport.size.height <= px(0.) {
            return;
        }
        let shift = if target.top() < viewport.top() {
            viewport.top() - target.top()
        } else if target.bottom() > viewport.bottom() {
            viewport.bottom() - target.bottom()
        } else {
            return;
        };
        let mut offset = self.config_scroll.offset();
        offset.y += shift;
        self.config_scroll.set_offset(offset);
        cx.notify();
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
                            // 1053 判断缓存跟随快照换新：新 feedback 是新的
                            // 1053 事件，可能对应期间被外部改过的配置，不能复用上一条反馈的结论。
                            *this.audit_dirs_match_cache.borrow_mut() = None;
                            changed = true;
                        }
                        // 配置事件由 app 级事件泵写入共享状态；这里同步镜像供渲染与编辑使用。
                        {
                            let shared = this.config_state.borrow();
                            if shared.config != this.config {
                                this.config = shared.config.clone();
                                // 配置变更同样作废 1053 判断缓存：
                                // 服务侧读的目录可能已被改写，旧结论不再可信。
                                *this.audit_dirs_match_cache.borrow_mut() = None;
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
                        if let Some((_, deadline)) = &this.copy_feedback
                            && Instant::now() >= *deadline
                        {
                            this.copy_feedback = None;
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
        self.log_copy_focus.borrow_mut().clear();
        self.log_service.clear(generation);
    }

    /// 连接状态对应的状态色。
    fn state_color(state: ConnectionState, theme: &Theme) -> Rgba {
        match state {
            ConnectionState::Connected => theme.success,
            ConnectionState::Connecting => theme.accent,
            ConnectionState::Reconnecting => theme.warning,
            ConnectionState::Disconnected => theme.danger,
        }
    }

    /// 事件基调（成功/中性/弱化/信息/警告）对应的文字色。
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

    /// 复制文本到系统剪贴板，并触发 2 秒的"已复制"反馈（绑定到指定按钮 ID）。
    fn copy_to_clipboard(&mut self, button_id: &str, text: &str, cx: &mut Context<Self>) {
        cx.write_to_clipboard(ClipboardItem::new_string(text.to_string()));
        self.copy_feedback = Some((button_id.to_string(), Instant::now() + COPY_FEEDBACK_TTL));
        cx.notify();
    }

    /// 返回指定按钮的复制反馈文案（"已复制"）；仅当该按钮是最近触发的且反馈未过期时返回。
    fn copy_feedback_for(&self, button_id: &str) -> Option<String> {
        self.copy_feedback
            .as_ref()
            .filter(|(id, deadline)| id == button_id && Instant::now() < *deadline)
            .map(|_| self.t(k::TOAST_COPIED))
    }

    /// 标记配置已改动，驱动脏提示与保存按钮状态。
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

    /// 切换主题模式并落盘用户选择。
    fn set_theme_mode(&mut self, mode: ThemeMode, cx: &mut Context<Self>) {
        if self.theme_mode.get() == mode {
            return;
        }
        self.theme_mode.set(mode);
        self.config.ui.theme_variant = mode.variant().to_string();
        self.mark_dirty(cx);
    }

    /// 上次保存/校验结果的展示文案。
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

    /// 统一字段编辑入口：执行编辑动作后收尾（脏标记、光标夹取）。
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

    /// 在光标处插入文本，并按字符边界维护光标。
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

    /// 删除光标前一个字符。
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

    /// 删除光标所在位置的字符。
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

    /// 按方向把光标移动一个字符。
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

    /// 读取字段当前光标位置。
    fn field_cursor(&self, id: FieldId) -> usize {
        self.fields
            .get(&id)
            .map(|state| state.cursor)
            .unwrap_or_default()
    }

    /// 写入字段光标位置，夹到合法字符边界。
    fn set_cursor(&mut self, id: FieldId, cursor: usize, cx: &mut Context<Self>) {
        if let Some(state) = self.fields.get_mut(&id) {
            state.cursor = cursor;
        }
        cx.notify();
    }

    /// 渲染字段输入框（含光标、占位与禁用态样式）。
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

    /// 打开系统文件/目录选择器并把结果写回字段。
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

    /// 把新的排除目录追加进规则列表。
    fn add_excluded_directory(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        let prompt = self.t(k::DIALOG_SELECT_EXCLUDED_DIR);
        self.prompt_single_path(false, prompt, window, cx, |this, text, cx| {
            this.config.watch.auto_excluded_directories.push(text);
            this.mark_dirty(cx);
        });
    }

    /// 渲染主题模式切换控件。
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
                    .focusable()
                    .tab_stop(true)
                    .focus(focus_ring(theme.accent))
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.set_theme_mode(candidate, cx);
                    }))
                    .child(i18n.get(&locale, label))
            }))
    }

    /// 设置行基础布局：两端对齐、纵向居中。
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

    /// 设置项行：左侧标题与说明，右侧控件。
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

    /// 开关型设置行。
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
        let (focus, probe) = self.config_focus(id, cx);
        self.row_base(label, description, Self::toggle_switch(on, theme), theme)
            .id(id)
            .cursor_pointer()
            .track_focus(&focus)
            .track_scroll(&probe)
            .focus(focus_ring(theme.accent))
            .on_click(cx.listener(move |this, _, _, cx| toggle(this, cx)))
            .into_any_element()
    }

    /// 渲染开关控件。
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

    /// 渲染可选项胶囊（单选/多选共用）。
    fn choice_chip(
        &self,
        id: String,
        label: String,
        active: bool,
        theme: Theme,
        cx: &mut Context<Self>,
        select: impl Fn(&mut Self, &mut Window, &mut Context<Self>) + 'static,
    ) -> AnyElement {
        let (focus, probe) = self.config_focus(&id, cx);
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
            .track_focus(&focus)
            .track_scroll(&probe)
            // 选中 chip 是实心强调色底，描边取 on_accent 反色才看得清。
            .focus(focus_ring(if active {
                theme.on_accent
            } else {
                theme.accent
            }))
            .on_click(cx.listener(move |this, _, window, cx| select(this, window, cx)))
            .child(label)
            .into_any_element()
    }

    /// 渲染通用单行输入框。
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
        let probe = self.config_registry.borrow_mut().probe(id.element_id());
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
            .border_color(theme.border)
            .rounded(px(7.))
            .cursor(CursorStyle::IBeam)
            .track_focus(&focus)
            .track_scroll(&probe)
            .focus(focus_ring(theme.accent))
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

    /// 渲染「浏览…」按钮。
    fn browse_button(&self, id: FieldId, theme: Theme, cx: &mut Context<Self>) -> AnyElement {
        let (focus, probe) = self.config_focus(id.browse_id(), cx);
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
            .track_focus(&focus)
            .track_scroll(&probe)
            .focus(focus_ring(theme.accent))
            .on_click(cx.listener(move |this, _, window, cx| {
                this.pick_path(id, window, cx);
            }))
            .child(self.t(k::BTN_BROWSE))
            .into_any_element()
    }

    /// 渲染路径字段（输入框 + 浏览按钮）。
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

    /// 渲染设置分组容器（分组标题 + 卡片）。
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

    /// 渲染列表行容器。
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

    /// 渲染配置页整体布局。
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
                let (remove_focus, remove_probe) =
                    self.config_focus(&format!("remove-excluded-{index}"), cx);
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
                                .track_focus(&remove_focus)
                                .track_scroll(&remove_probe)
                                .focus(focus_ring(theme.accent))
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
        let (add_focus, add_probe) = self.config_focus("add-excluded", cx);
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
                .track_focus(&add_focus)
                .track_scroll(&add_probe)
                .focus(focus_ring(theme.accent))
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

    /// 渲染左侧导航标签。
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
            .focusable()
            .tab_stop(true)
            .focus(focus_ring(theme.accent))
            .on_click(cx.listener(move |this, _, _, cx| {
                this.page = page;
                cx.notify();
            }))
            .child(label)
    }

    /// 渲染日志级别筛选按钮组。
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
                    .focusable()
                    .tab_stop(true)
                    .focus(focus_ring(theme.accent))
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.log_state.borrow_mut().level_filter = candidate;
                        this.rebuild_visible();
                        cx.notify();
                    }))
                    .child(i18n.get(&locale, level_chip_key(candidate)))
            }))
    }

    /// 渲染详情面板的键值行。
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

        // 保存已落盘但未生效（或保存本身失败）时，原因必须在概览页常驻可见，
        // 并可直接点回配置页。以前它只是配置页里一条 3 s 淡出的状态文本，用户在概览页只看到
        // 「请前往配置页设置监控目录」，被绕回同一步。
        let reload_block = match &self.save_state {
            SaveState::SavedWorkerDown(error) => Some((
                self.tr(k::STATUS_SAVED_WORKER_DOWN, &[error]),
                theme.warning,
            )),
            SaveState::Failed(error) => Some((
                self.tr(k::MSG_SAVE_FAILED_EXCEPTION, &[error]),
                theme.danger,
            )),
            _ => None,
        };

        // 连接失败行优先呈现连接结果的分类文案——服务授权拒绝与 Worker 缺失
        // 是两种截然不同的处境（#169 落地后关票，决策归档），统一归到心跳的 os error 2 会让未授权用户无从采取正确动作。
        // 无分类时维持 last_error 原样（未分类错误的保底处理规则不变）。
        let failure_line = match &self.snapshot.connect_failure {
            Some(cause) => Some((
                connect_failure_text(&self.i18n, &self.config.ui.locale, cause),
                connect_failure_color(cause, theme),
            )),
            None => self
                .snapshot
                .last_error
                .clone()
                .map(|error| (error, theme.danger)),
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
            .children(failure_line.map(|(error, color)| {
                div()
                    .flex()
                    .items_center()
                    .gap(px(8.))
                    .child(
                        div()
                            .text_size(px(12.))
                            .text_color(color)
                            .child(error.clone()),
                    )
                    .child(self.copy_button(
                        "copy-error".to_string(),
                        &error,
                        k::STATUS_COPY_ERROR,
                        None,
                        theme,
                        cx,
                    ))
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
                        .id("configure-watch-folder")
                        .cursor_pointer()
                        .hover(|element| element.text_color(theme.text))
                        .focusable()
                        .tab_stop(true)
                        .focus(focus_ring(theme.accent))
                        .on_click(cx.listener(|this, _, _, cx| {
                            this.page = Page::Config;
                            cx.notify();
                        }))
                        .text_size(px(12.))
                        .text_color(theme.warning)
                        .child(self.t(k::STATUS_CONFIGURE_WATCH_FOLDER))
                }),
            )
            .children(reload_block.map(|(text, color)| {
                div()
                    .id("reload-blocked")
                    .cursor_pointer()
                    .hover(|element| element.text_color(theme.text))
                    .focusable()
                    .tab_stop(true)
                    .focus(focus_ring(theme.accent))
                    .on_click(cx.listener(|this, _, _, cx| {
                        this.page = Page::Config;
                        cx.notify();
                    }))
                    .text_size(px(12.))
                    .text_color(color)
                    .child(text)
            }));

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
                    .focusable()
                    .tab_stop(true)
                    .focus(focus_ring(theme.accent))
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
                cx.processor(move |this, range: std::ops::Range<usize>, _window, cx| {
                    let entries: Vec<(usize, AuditRecord)> = {
                        let state = this.log_state.borrow();
                        let mut entries = Vec::with_capacity(range.len());
                        for index in range {
                            let Some(entry_index) = state.visible_indices.get(index).copied()
                            else {
                                continue;
                            };
                            let Some(entry) = state.logs.get(entry_index) else {
                                continue;
                            };
                            // 用 entry_index（日志缓冲中的稳定位置）而非可见列表位置，
                            // 避免过滤/清空后同一按钮 ID 映射到不同条目。
                            entries.push((entry_index, entry.clone()));
                        }
                        entries
                    };
                    let mut rows = Vec::with_capacity(entries.len());
                    for (entry_index, entry) in &entries {
                        rows.push(
                            this.log_row(*entry_index, entry, &row_theme, row_rtl, cx)
                                .into_any_element(),
                        );
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

    /// 渲染单条日志卡片。
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

    /// 渲染日志页头部（级别筛选与操作按钮）。
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

    /// 渲染日志列表行。
    fn log_row(
        &self,
        entry_index: usize,
        entry: &AuditRecord,
        theme: &Theme,
        rtl: bool,
        cx: &mut Context<Self>,
    ) -> Div {
        let tone = Self::tone_color(entry.tone, theme);
        let copy_text = format!(
            "[{}] {} {} {}",
            entry.time_text, entry.display_event, entry.source_path_masked, entry.message
        );
        let button_id = format!("copy-log-{entry_index}");
        let row_focus = self.log_copy_handle(entry_index, cx);
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
            .child(self.copy_button(
                button_id,
                &copy_text,
                k::LOG_BTN_COPY,
                Some(row_focus),
                theme,
                cx,
            ))
    }

    /// 构造统一风格的复制按钮：点击后复制 `text` 到剪贴板，反馈期间按钮文字变为"已复制"。
    /// `id` 同时用作 gpui 元素 ID 和反馈归属判定，须保证同帧内唯一。
    /// `focus` 传 `Some` 时用登记句柄（虚拟列表行的边界滚动跟进需要按行反查/直取），
    /// 传 `None` 时用元素自带句柄，两者焦点链行为一致。
    fn copy_button(
        &self,
        id: String,
        text: &str,
        label_key: &str,
        focus: Option<FocusHandle>,
        theme: &Theme,
        cx: &mut Context<Self>,
    ) -> Stateful<Div> {
        let copy_text = text.to_string();
        let feedback = self.copy_feedback_for(&id);
        let button_id = id.clone();
        let button = div()
            .id(id)
            .px(px(8.))
            .py(px(2.))
            .rounded(px(6.))
            .border_1()
            .border_color(theme.border)
            .text_size(px(11.))
            .text_color(theme.text_muted)
            .hover(|element| element.bg(theme.surface_raised).text_color(theme.text))
            .cursor_pointer();
        let button = match focus {
            Some(focus) => button.track_focus(&focus),
            None => button.focusable().tab_stop(true),
        };
        // 复制按钮是可交互控件，必须入焦点链（track_focus/focusable + tab_stop
        // + 焦点环）；Enter/Space 经 gpui 合成点击与鼠标同效。无底色描边取默认强调色。
        button
            .focus(focus_ring(theme.accent))
            .on_click(cx.listener(move |this, _, _, cx| {
                this.copy_to_clipboard(&button_id, &copy_text, cx);
            }))
            .child(feedback.unwrap_or_else(|| self.t(label_key)))
    }

    /// 渲染空列表占位。
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

    /// 服务状态对应的状态色。
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

    /// 渲染服务页的键值行。
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

    /// 渲染服务页操作按钮（启动/停止/安装/卸载）。
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
        // 实心底（主/危险）描边用 on_accent 反色；幽灵按钮底是 surface，用默认强调色轮廓。
        let ring = if matches!(kind, ButtonKind::Ghost) {
            theme.accent
        } else {
            theme.on_accent
        };
        let button = button
            .cursor_pointer()
            .focusable()
            .tab_stop(true)
            .focus(focus_ring(ring))
            .on_click(on_click);
        if matches!(kind, ButtonKind::Ghost) {
            button.hover(|element| element.bg(theme.surface))
        } else {
            // 实心主按钮/危险按钮的 hover 改成 surface 会让 on_accent 前景失去对比度，只保留点击。
            button
        }
    }

    /// 渲染水平分隔线。
    fn divider(theme: &Theme) -> Div {
        div().h(px(1.)).bg(theme.border)
    }

    /// 1053 归因判断（带缓存）：`feedback_banner` 每帧渲染都会问一次"两边审计目录是否相同"，
    /// 直接调 `connected_audit_dirs_match` 等于每帧 `is_file()` + 读 `config.json`（CodeRabbit
    /// 返修：UI 线程同步读盘会拖住渲染）。缓存键变化才重算，命中缓存时零磁盘 IO。
    ///
    /// 两处例外（CodeRabbit 返修）：① 服务快照变更（含新 feedback 到达）时缓存被清空，
    /// 新反馈绝不复用上一条反馈的结论；② 服务侧配置读不到/解析失败（`Unknown`）时不写缓存，
    /// 避免把暂态读失败钉成 `false`，配置修好后仍不复核。
    fn audit_dirs_match(&self, feedback: &ServiceFeedback) -> bool {
        let key = AuditDirsMatchKey {
            exit_code: feedback.exit_code,
            message: feedback.message.clone(),
            config_path: self.config_path.clone(),
            worker_audit_directory: self.config.audit.log_directory.clone(),
        };
        if let Some((cached_key, cached)) = self.audit_dirs_match_cache.borrow().as_ref() {
            if *cached_key == key {
                return *cached;
            }
        }
        let verdict = audit_dirs_match_verdict(&self.config, &self.config_path);
        let mut cache = self.audit_dirs_match_cache.borrow_mut();
        *cache = if verdict.is_cacheable() {
            Some((key, verdict.is_matched()))
        } else {
            None
        };
        verdict.is_matched()
    }

    /// 渲染操作结果反馈条（成功/失败/进行中）。
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
        // #202：1053 且正连着后台 Worker 时改走让位文案，不再把单写者冲突指向 ExifTool/配置；
        // 其余退出码与真超时（未连后台 Worker）保持原有 service.exitcode.* 映射。
        let connection = self.snapshot.state;
        let worker_mode = self.snapshot.worker_mode;
        // #218：连后台 Worker 还不够，两边审计目录相同才成立——服务读的是安装时的
        // config.json，UI 这份可能已被改成别的目录，此时 1053 只能算真超时。
        // 只有 1053 才会用到这个判断：先短路掉 exit_code 判断，渲染路径上不做无谓的读盘；
        // 1053 这一支走 `audit_dirs_match` 的缓存，同一份反馈重复渲染不再碰磁盘
        // （CodeRabbit 返修：UI 线程每帧读盘会阻塞渲染）。
        let audit_dirs_match = exit_code == Some(1053) && self.audit_dirs_match(feedback);
        let exit_code_key = exit_code.map(|code| {
            if start_timeout_cause(code, connection, worker_mode, audit_dirs_match)
                == StartTimeoutCause::SingleWriterYield
            {
                k::SERVICE_EXITCODE_1053_SINGLE_WRITER.to_string()
            } else {
                format!("service.exitcode.{code}")
            }
        });
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

    /// 渲染服务页整体布局。
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

/// 按字段规格读取配置中的值。
fn field_value(config: &AppConfig, id: FieldId) -> &str {
    (id.spec().value)(config)
}

/// 按字段规格取得配置值的可写引用。
fn field_value_mut(config: &mut AppConfig, id: FieldId) -> &mut String {
    (id.spec().value_mut)(config)
}

/// 把字节下标回退到最近的合法 UTF-8 字符边界。
fn floor_char_boundary(text: &str, mut index: usize) -> usize {
    index = index.min(text.len());
    while index > 0 && !text.is_char_boundary(index) {
        index -= 1;
    }
    index
}

/// 光标左侧相邻字符的边界位置。
fn prev_char_boundary(text: &str, cursor: usize) -> usize {
    text[..cursor]
        .char_indices()
        .next_back()
        .map(|(index, _)| index)
        .unwrap_or(0)
}

/// 光标右侧相邻字符的边界位置。
fn next_char_boundary(text: &str, cursor: usize) -> usize {
    text[cursor..]
        .char_indices()
        .nth(1)
        .map(|(index, _)| cursor + index)
        .unwrap_or(text.len())
}

/// 空值统一展示为破折号。
fn or_dash(value: Option<String>) -> String {
    value
        .filter(|value| !value.is_empty())
        .unwrap_or_else(|| "—".to_string())
}

impl Render for AppView {
    /// AppView 渲染入口：按当前页面分发。
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
                .track_scroll(&self.config_scroll)
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
            .track_focus(&self.focus_root)
            .on_action(cx.listener(Self::on_focus_next))
            .on_action(cx.listener(Self::on_focus_prev))
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

    use crate::config::AuditConfig;

    use gpui::{
        AnyWindowHandle, AppContext, InputEvent, KeyUpEvent, Keystroke, TestAppContext,
        WindowHandle, size,
    };

    /// #202 AC1/AC3：1053 只有在「此刻连着」后台 Worker 且两边审计目录相同时才归因为
    /// 单写者让位；真超时（直连服务/未连接/连接失败后残留的 Background）与其余退出码
    /// 必须保持原归因，不让真超时文案回归。
    #[test]
    fn start_timeout_cause_distinguishes_yield_from_true_timeout() {
        assert_eq!(
            start_timeout_cause(
                1053,
                ConnectionState::Connected,
                Some(WorkerMode::Background),
                true
            ),
            StartTimeoutCause::SingleWriterYield
        );
        assert_eq!(
            start_timeout_cause(
                1053,
                ConnectionState::Connected,
                Some(WorkerMode::Service),
                true
            ),
            StartTimeoutCause::TrueTimeout
        );
        assert_eq!(
            start_timeout_cause(1053, ConnectionState::Connected, None, true),
            StartTimeoutCause::TrueTimeout
        );
        assert_eq!(
            start_timeout_cause(
                5,
                ConnectionState::Connected,
                Some(WorkerMode::Background),
                true
            ),
            StartTimeoutCause::TrueTimeout
        );
    }

    /// #218 AC2 回归锁：连后台 Worker 但审计目录不同时，1053 只能是真超时——
    /// 两个 Worker 没争同一份目录，贴让位文案会让用户照着关后台 Worker 却修不好。
    #[test]
    fn start_timeout_cause_requires_matching_audit_directories() {
        assert_eq!(
            start_timeout_cause(
                1053,
                ConnectionState::Connected,
                Some(WorkerMode::Background),
                false
            ),
            StartTimeoutCause::TrueTimeout,
            "目录不同时必须回落真超时归因"
        );
    }

    /// #218：同一目录的 `C:\a\_audit` 与 `C:\a\_audit\` 不能算两个目录，否则让位文案会被
    /// 归一化差异吃掉。
    #[test]
    fn audit_directory_key_ignores_trailing_separators_and_blank() {
        assert_eq!(
            audit_directory_key("  /data/_audit/  "),
            audit_directory_key("/data/_audit")
        );
        assert_ne!(
            audit_directory_key("/data/_audit"),
            audit_directory_key("/data/_audit2")
        );
    }

    /// Windows 路径大小写不敏感，只有该平台才做 lower 归一（Linux CI 上保留原样）。
    #[cfg(windows)]
    #[test]
    fn audit_directory_key_is_case_insensitive_on_windows() {
        assert_eq!(
            audit_directory_key("C:\\Photos\\_audit"),
            audit_directory_key("c:\\photos\\_AUDIT")
        );
    }

    /// Windows 上 `/` 与 `\` 是等价分隔符：文件夹选择器给 `\`，手改 config.json 可能写 `/`，
    /// 只做大小写归一仍会把同一目录算成两个（CodeRabbit 返修）。
    #[cfg(windows)]
    #[test]
    fn audit_directory_key_normalizes_separators_on_windows() {
        assert_eq!(
            audit_directory_key("C:/Photos/_audit"),
            audit_directory_key("c:\\photos\\_audit\\")
        );
    }

    /// 盘根（`C:\`）的尾分隔符属于根本身：剥掉会变成盘相对路径 `C:`，两者是不同位置，
    /// 归一化不得压成同一个键（CodeRabbit 返修）。
    #[cfg(windows)]
    #[test]
    fn audit_directory_key_keeps_drive_root_separator() {
        assert_eq!(audit_directory_key("C:\\"), "c:\\");
        assert_eq!(audit_directory_key("C:/"), "c:\\");
        assert_eq!(audit_directory_key("C:"), "c:");
        assert_ne!(audit_directory_key("C:\\"), audit_directory_key("C:"));
    }

    /// #218：服务侧目录取自磁盘上安装时的 config.json；文件缺失/被移走时不得假定
    /// "目录相同"，否则真超时又会被标成让位。
    #[test]
    fn connected_audit_dirs_match_reads_service_config_from_disk() {
        let mut dir = std::env::temp_dir();
        dir.push(format!("photoprivacy-218-audit-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let config_path = dir.join("config.json");

        let worker_audit = dir.join("worker_audit").display().to_string();
        let service_audit = dir.join("service_audit").display().to_string();
        let config = AppConfig {
            audit: AuditConfig {
                log_directory: worker_audit.clone(),
                ..AuditConfig::default()
            },
            ..AppConfig::default()
        };

        assert!(
            !connected_audit_dirs_match(&config, &config_path),
            "配置缺失时必须按目录不同处理"
        );

        let write_service_audit = |path: &str| {
            let service_config = AppConfig {
                audit: AuditConfig {
                    log_directory: path.to_string(),
                    ..AuditConfig::default()
                },
                ..config.clone()
            };
            std::fs::write(&config_path, service_config.to_json()).unwrap();
        };

        write_service_audit(&worker_audit);
        assert!(connected_audit_dirs_match(&config, &config_path));

        write_service_audit(&service_audit);
        assert!(
            !connected_audit_dirs_match(&config, &config_path),
            "服务改用另一份审计目录后不得再判为同一目录"
        );

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// CodeRabbit 返修：#202 AC3 回归锁——连接失败时快照里的 `worker_mode` 可能仍是上次
    /// 握手的 `Background`，此时真超时不能被误标成单写者让位。
    #[test]
    fn start_timeout_cause_ignores_stale_background_mode_without_connection() {
        for state in [
            ConnectionState::Disconnected,
            ConnectionState::Connecting,
            ConnectionState::Reconnecting,
        ] {
            assert_eq!(
                start_timeout_cause(1053, state, Some(WorkerMode::Background), true),
                StartTimeoutCause::TrueTimeout,
                "{state:?} 下残留 Background 不能归因为单写者让位"
            );
        }
    }

    /// 测试：字段枚举与规格表的数量、id 必须一一对齐。
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

    /// 避免出现未授权用户最终只看到统一的「连接 Worker 失败: os error 2」，
    /// 三态文案映射错位会把这层混同原样带回（未授权被引导去重装、缺 Worker 被引导去找安装者）。
    #[test]
    fn connect_failure_texts_map_to_distinct_guidance() {
        let catalog = Catalog::embedded();

        let denied =
            connect_failure_text(&catalog, "zh-CN", &ConnectFailureCause::ServiceAccessDenied);
        assert!(denied.contains("未被授权"), "denied: {denied}");
        // 授权拒绝的行动指引按平台分键（CodeRabbit 返修）：Windows 指向安装脚本的
        // -AuthorizedUsers 集合，Unix 指向 0660+共享组边界——两平台都不混入 os error 说法。
        if cfg!(windows) {
            assert!(denied.contains("-AuthorizedUsers"), "denied: {denied}");
        } else {
            assert!(denied.contains("授权组"), "denied: {denied}");
        }
        assert!(
            !denied.contains("os error"),
            "授权拒绝文案不得混入端点缺失的 os error 口径: {denied}"
        );

        let startup = connect_failure_text(
            &catalog,
            "zh-CN",
            &ConnectFailureCause::WorkerStartupFailed("worker exited with code 1".to_string()),
        );
        assert!(startup.contains("未就绪"), "startup: {startup}");
        assert!(
            startup.contains("worker exited with code 1"),
            "startup: {startup}"
        );

        let not_installed =
            connect_failure_text(&catalog, "zh-CN", &ConnectFailureCause::WorkerNotInstalled);
        assert!(
            not_installed.contains("未找到 Worker 程序"),
            "{not_installed}"
        );

        // 三态文案互异：全部落到同一键等于没有分类。
        assert_ne!(denied, startup);
        assert_ne!(denied, not_installed);
        assert_ne!(startup, not_installed);

        // 占位符替换在非中文语言下同样成立。
        let en_startup = connect_failure_text(
            &catalog,
            "en",
            &ConnectFailureCause::WorkerStartupFailed("spawn: boom".to_string()),
        );
        assert!(en_startup.contains("spawn: boom"), "en: {en_startup}");
    }

    /// 避免重载失败原因随 `SAVE_STATUS_TTL` 一起消失，导致用户被绕回「请前往配置页」。
    // 失败态不挂 3 s TTL，只有成功态按 TTL 淡出。
    #[test]
    fn save_failure_status_stays_visible_instead_of_expiring() {
        let mut state = ConfigUiState::new(Some(AppConfig::default()));

        let _ = state.apply_event(ConfigEvent::Saved {
            config: Box::new(AppConfig::default()),
            revision: 0,
            worker_error: Some("exiftool.path not found".to_string()),
        });
        assert!(matches!(&state.save_state, SaveState::SavedWorkerDown(_)));
        assert_eq!(state.save_status_deadline, None);

        let _ = state.apply_event(ConfigEvent::Saved {
            config: Box::new(AppConfig::default()),
            revision: 0,
            worker_error: None,
        });
        assert!(matches!(&state.save_state, SaveState::Saved));
        assert!(state.save_status_deadline.is_some());

        let _ = state.apply_event(ConfigEvent::SaveFailed("磁盘只读".to_string()));
        assert!(matches!(&state.save_state, SaveState::Failed(_)));
        assert_eq!(state.save_status_deadline, None);
    }

    /// 避免加载时把空的 `exiftool.path` 填成探测到的绝对路径，
    /// UI 一保存就写进 config.json——发布包换个目录解压后 Worker 仍指向旧位置。
    #[test]
    fn blank_exiftool_path_is_not_written_back_on_load() {
        let mut state = ConfigUiState::new(None);
        let mut config = AppConfig::default();
        config.exiftool.path = String::new();

        let _ = state.set_config(config, true);

        assert_eq!(state.config.exiftool.path, "");
        // 探测结果只作为提示展示，不进配置值。
        if let Some(hint) = &state.exiftool_hint {
            assert!(!hint.trim().is_empty());
        }
    }

    /// #33: 目录式覆盖在首次加载时保留用户填的目录，不把解析出的 exe 路径写回
    /// config.json（否则一次保存就把目录冻结成文件绝对路径）。提示仍显示解析结果。
    #[test]
    fn directory_exiftool_path_is_not_rewritten_on_load() {
        let unique = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let dir = std::env::temp_dir().join(format!("pp-dir-override-{}", unique));
        let _ = std::fs::create_dir_all(&dir);
        let exe = dir.join(if cfg!(windows) {
            "exiftool.exe"
        } else {
            "exiftool"
        });
        std::fs::write(&exe, b"stub").unwrap();
        let expected_hint = exe.to_string_lossy().into_owned();

        let mut state = ConfigUiState::new(None);
        let mut config = AppConfig::default();
        config.exiftool.path = dir.to_string_lossy().into_owned();

        let _ = state.set_config(config, true);

        assert_eq!(
            state.config.exiftool.path,
            dir.to_string_lossy().into_owned()
        );
        assert_eq!(state.exiftool_hint.as_deref(), Some(expected_hint.as_str()));

        // 前后带空白的目录同样不能触发写回：detect_exiftool_path 先 trim 再解析，
        // set_config 必须用同一规则判断 is_dir，否则目录值会被替换成 exe 绝对路径。
        let padded = format!("  {}  ", dir.display());
        let mut padded_config = AppConfig::default();
        padded_config.exiftool.path = padded.clone();
        let _ = state.set_config(padded_config, true);
        // 保存值规整为 trim 后的目录（Worker 侧 File.Exists/Directory.Exists 不接受带空白路径），
        // 目录形态仍保留、不冻结成 exe 绝对路径——即结果能被 Worker 的目录式覆盖解析接受。
        assert_eq!(
            state.config.exiftool.path,
            dir.to_string_lossy().into_owned()
        );
        assert!(std::path::Path::new(&state.config.exiftool.path).is_dir());
        assert_eq!(state.exiftool_hint.as_deref(), Some(expected_hint.as_str()));

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// 最小 AppDeps：不接 Worker/服务/配置后台，只支撑渲染与焦点链。
    fn test_deps() -> AppDeps {
        let (_, snapshots) = watch::channel(ConnectionSnapshot::connecting());
        let (_, service_snapshots) = watch::channel(ServiceSnapshot::default());
        let (service_commands, _) = mpsc::unbounded_channel();
        let (config_commands, _) = mpsc::unbounded_channel();
        AppDeps {
            snapshots,
            theme_mode: Rc::new(Cell::new(ThemeMode::System)),
            log_state: Rc::new(RefCell::new(LogState::new(LevelFilter::Info))),
            log_service: LogService::detached(),
            service_snapshots,
            service_commands,
            config_state: Rc::new(RefCell::new(ConfigUiState::new(None))),
            config_commands,
            config_path: PathBuf::from("config.json"),
            rules: Rc::new(RefCell::new(RulesEditor::open(
                std::env::temp_dir().join("photoprivacy-ui-focus-tests-rules.json"),
            ))),
            i18n: Rc::new(Catalog::embedded()),
            open_page: None,
        }
    }

    /// 测试环境：建单窗口 AppView 并完成首帧绘制。
    fn setup() -> (TestAppContext, WindowHandle<AppView>) {
        let mut cx = TestAppContext::single();
        cx.update(bind_focus_keys);
        let window = cx.add_window(move |window, cx| AppView::new(window, cx, test_deps()));
        cx.run_until_parked();
        draw(&mut cx, window.into());
        (cx, window)
    }

    /// 指定窗口尺寸建窗（add_window 用最大化边界；最小高度遍历用例要精确尺寸）。
    fn setup_sized(width: f32, height: f32) -> (TestAppContext, WindowHandle<AppView>) {
        let mut cx = TestAppContext::single();
        cx.update(bind_focus_keys);
        let window = cx.open_window(size(px(width), px(height)), move |window, cx| {
            AppView::new(window, cx, test_deps())
        });
        cx.run_until_parked();
        draw(&mut cx, window.into());
        (cx, window)
    }

    /// #174 AC1 后半：`--open-page` 指定的页必须是建窗首帧页，而不是先落 Overview 再靠导航跳。
    #[test]
    fn open_page_decides_the_first_frame_page() {
        let mut cx = TestAppContext::single();
        cx.update(bind_focus_keys);
        let mut deps = test_deps();
        deps.open_page = Some(Page::Service);
        let window = cx.add_window(move |window, cx| AppView::new(window, cx, deps));
        cx.run_until_parked();
        draw(&mut cx, window.into());
        assert_eq!(
            window.update(&mut cx, |view, _, _| view.page).unwrap(),
            Page::Service,
            "--open-page service 应直接停在 Service 页"
        );
    }

    /// 目标控件是否完整落在 config-scroll 视口内（按登记表探针的布局坐标判定）。
    fn control_in_view(
        cx: &mut TestAppContext,
        window: WindowHandle<AppView>,
        target: &FocusHandle,
    ) -> bool {
        window
            .update(cx, |view, _, _| {
                let Some(bounds) = view.config_registry.borrow().bounds_of(target) else {
                    return false;
                };
                let viewport = view.config_scroll.bounds();
                bounds.top() >= viewport.top() && bounds.bottom() <= viewport.bottom()
            })
            .unwrap()
    }

    /// 测试辅助：在给定上下文里推进一帧绘制。
    fn draw(cx: &mut TestAppContext, window: AnyWindowHandle) {
        cx.update_window(window, |_, window, cx| {
            window.draw(cx).clear(cx);
        })
        .unwrap();
    }

    /// 测试辅助：取窗口当前焦点句柄。
    fn focused(cx: &mut TestAppContext, window: AnyWindowHandle) -> Option<FocusHandle> {
        cx.update_window(window, |_, window, cx| window.focused(cx))
            .unwrap()
    }

    /// 按一次 Tab（forward）或 Shift+Tab（reverse）：焦点动作在按键派发时命中绑定，
    /// 随后重画让新焦点控件登记按键监听（Enter/Space 合成点击依赖此帧）。
    fn press_tab(cx: &mut TestAppContext, window: AnyWindowHandle, forward: bool) {
        let os = cx.read(|cx| cx.platform_info().os);
        let keystroke =
            Keystroke::parse_with_os(if forward { "tab" } else { "shift-tab" }, os).unwrap();
        cx.dispatch_keystroke(window, keystroke);
        cx.run_until_parked();
        draw(cx, window);
    }

    /// 按下并松开 Enter 或 Space：gpui 的合成点击要求 keydown 记账 + keyup 触发，
    /// 两次派发之间焦点不得移动（focus_generation 不变）。
    fn press_activation_key(cx: &mut TestAppContext, window: AnyWindowHandle, key: &str) {
        let os = cx.read(|cx| cx.platform_info().os);
        let keystroke = Keystroke::parse_with_os(key, os).unwrap();
        cx.update_window(window, |_, window, cx| {
            window.dispatch_event(
                KeyDownEvent {
                    keystroke: keystroke.clone(),
                    is_held: false,
                    prefer_character_input: false,
                }
                .to_platform_input(),
                cx,
            );
        })
        .unwrap();
        cx.run_until_parked();
        cx.update_window(window, |_, window, cx| {
            window.dispatch_event(KeyUpEvent { keystroke }.to_platform_input(), cx);
        })
        .unwrap();
        cx.run_until_parked();
    }

    /// Tab 两跳到「配置」导航项并按 Enter 进入，断言落地配置页。
    fn open_config_page(cx: &mut TestAppContext, window: WindowHandle<AppView>) {
        let any_window: AnyWindowHandle = window.into();
        press_tab(cx, any_window, true);
        press_tab(cx, any_window, true);
        press_activation_key(cx, any_window, "enter");
        draw(cx, any_window);
        assert_eq!(
            window.update(cx, |view, _, _| view.page).unwrap(),
            Page::Config,
            "应先进入配置页"
        );
    }

    /// Tab 依渲染顺序循环遍历可聚焦控件，走完一圈后回绕到第一个（AC1）。
    /// Overview 页的 tab stop：5 个导航项 + 3 个主题平铺选项。
    #[test]
    fn tab_cycles_focusable_controls_in_render_order_and_wraps() {
        let (mut cx, window) = setup();
        let window: AnyWindowHandle = window.into();
        let mut seen = Vec::new();
        for _ in 0..8 {
            press_tab(&mut cx, window, true);
            seen.push(focused(&mut cx, window).expect("Tab 之后应有控件获得焦点"));
        }
        for (i, first) in seen.iter().enumerate() {
            for (j, second) in seen.iter().enumerate().skip(i + 1) {
                assert_ne!(
                    first,
                    second,
                    "第 {} 次与第 {} 次 Tab 落在同一个句柄上",
                    i + 1,
                    j + 1
                );
            }
        }
        press_tab(&mut cx, window, true);
        assert_eq!(
            focused(&mut cx, window),
            Some(seen[0].clone()),
            "走完一圈后 Tab 应回绕到第一个控件"
        );
    }

    /// Shift+Tab 反向遍历，并从第一个控件反向回绕到最后一个（AC1）。
    #[test]
    fn shift_tab_traverses_focus_in_reverse() {
        let (mut cx, window) = setup();
        let window: AnyWindowHandle = window.into();
        press_tab(&mut cx, window, true);
        press_tab(&mut cx, window, true);
        press_tab(&mut cx, window, true);
        let third = focused(&mut cx, window).expect("第三个控件应获得焦点");

        press_tab(&mut cx, window, false);
        let second = focused(&mut cx, window).expect("反向应退回第二个控件");
        press_tab(&mut cx, window, false);
        let first = focused(&mut cx, window).expect("反向应退回第一个控件");
        assert_ne!(first, second);
        assert_ne!(second, third);

        // 第一个再反向 → 回绕到最后一个（第 8 个 tab stop）
        press_tab(&mut cx, window, false);
        let last = focused(&mut cx, window).expect("反向回绕应落到末位控件");
        assert_ne!(last, first);
        press_tab(&mut cx, window, true);
        assert_eq!(
            focused(&mut cx, window),
            Some(first),
            "从末位正向 Tab 应回到第一个控件"
        );
    }

    /// 聚焦在导航按钮上按 Enter 触发点击：切到配置页（AC3）。
    #[test]
    fn enter_activates_focused_nav_button() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        press_tab(&mut cx, any_window, true);
        press_tab(&mut cx, any_window, true);
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        let page = window.update(&mut cx, |view, _, _| view.page).unwrap();
        assert_eq!(page, Page::Config, "Enter 应点击第二个导航项进入配置页");
    }

    /// 聚焦在平铺选项上按 Space 触发切换：主题切到 Light（AC3）。
    #[test]
    fn space_toggles_focused_tile_option() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        // 5 个导航项之后是主题选项组：System → Light → Dark，第 7 个 tab stop 是 Light。
        for _ in 0..7 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "space");
        draw(&mut cx, any_window);
        let mode = window
            .update(&mut cx, |view, _, _| view.theme_mode.get())
            .unwrap();
        assert_eq!(mode, ThemeMode::Light, "Space 应切换选中的主题平铺选项");
    }

    /// 聚焦在开关型平铺选项行上，Space 与 Enter 都能触发切换（AC3 的两键矩阵补全）。
    #[test]
    fn enter_and_space_toggle_switch_row() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        open_config_page(&mut cx, window);
        let before = window
            .update(&mut cx, |view, _, _| view.config.backup.enabled)
            .unwrap();
        // 配置页 tab stop：5 导航 + 3 主题选项 + 4 个路径控件（字段/浏览 ×2），
        // 第 13 个是「启用备份」开关行；当前焦点在第 2 个（配置导航），再走 11 跳。
        for _ in 0..11 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "space");
        draw(&mut cx, any_window);
        let after_space = window
            .update(&mut cx, |view, _, _| view.config.backup.enabled)
            .unwrap();
        assert_ne!(before, after_space, "Space 应切换聚焦的开关行");
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        let after_enter = window
            .update(&mut cx, |view, _, _| view.config.backup.enabled)
            .unwrap();
        assert_eq!(after_enter, before, "Enter 应把开关行切回原状态");
    }

    /// 聚焦在按钮上按 Enter 触发点击：日志页清空按钮（AC3）。
    #[test]
    fn enter_activates_focused_clear_logs_button() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        // 切到日志页（第 4 个导航项）
        for _ in 0..4 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        let generation_before = window
            .update(&mut cx, |view, _, _| view.log_state.borrow().generation)
            .unwrap();
        // 日志页 tab stop：5 导航 + 3 主题选项 + 5 级别过滤选项 + 清空按钮 = 14 个。
        // 当前焦点停在第 4 个（日志导航），再走 10 跳到第 14 个（清空按钮）。
        for _ in 0..10 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        let generation_after = window
            .update(&mut cx, |view, _, _| view.log_state.borrow().generation)
            .unwrap();
        assert_ne!(
            generation_before, generation_after,
            "Enter 应触发清空按钮的点击处理"
        );
    }

    /// 往日志页共享状态注入 `count` 条 Info 级审计记录并重建可见索引（测试装配，生产走 LogService）。
    fn inject_log_records(cx: &mut TestAppContext, window: WindowHandle<AppView>, count: usize) {
        window
            .update(cx, |view, _, _| {
                let records = (0..count)
                    .map(|seq| {
                        photoprivacy_logs::parse_line(&format!(
                            r#"{{"event_type":"file_detected","level":"INFO","timestamp_utc":"2026-11-06T00:00:{seq:02}Z","source_path_masked":"C:/p.jpg","message":"行{seq}"}}"#,
                        ))
                        .expect("样例审计行应可解析")
                    })
                    .collect::<Vec<_>>();
                view.log_state.borrow_mut().logs.push_batch(records);
                view.rebuild_visible();
            })
            .unwrap();
    }

    /// 当前焦点对应的日志行 entry_index；焦点不在日志行复制按钮上时 `None`。
    fn focused_log_entry(cx: &mut TestAppContext, window: WindowHandle<AppView>) -> Option<usize> {
        let any_window: AnyWindowHandle = window.into();
        let focused = focused(cx, any_window)?;
        window
            .update(cx, |view, _, _| {
                view.log_copy_focus
                    .borrow()
                    .iter()
                    .find(|(_, handle)| *handle == &focused)
                    .map(|(entry, _)| *entry)
            })
            .unwrap()
    }

    /// Tab 走到日志行复制按钮并按 Enter 复制（#119 AC1/AC2）：
    /// 清空按钮（第 14 站）之后的下一站即首行复制按钮——焦点次序跟随渲染次序；
    /// Enter 激活与鼠标点击同效（剪贴板 + 已复制反馈），且不切换页面、不动日志数据（AC4）。
    #[test]
    fn tab_reaches_log_copy_button_and_enter_copies() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        inject_log_records(&mut cx, window, 1);

        // 切到日志页（第 4 个导航项）
        for _ in 0..4 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);

        // 日志页 tab stop：5 导航 + 3 主题选项 + 5 级别过滤 + 清空按钮 + 首行复制按钮 = 15 个。
        // 当前焦点停在第 4 个（日志导航），再走 11 跳到第 15 个（首行复制按钮）。
        for _ in 0..11 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);

        let feedback = window
            .update(&mut cx, |view, _, _| {
                view.copy_feedback.as_ref().map(|(id, _)| id.clone())
            })
            .unwrap();
        assert_eq!(
            feedback.as_deref(),
            Some("copy-log-0"),
            "Enter 应激活首行复制按钮并触发已复制反馈"
        );
        let clipboard = cx.read(|cx| cx.read_from_clipboard().and_then(|item| item.text()));
        assert!(
            clipboard.as_ref().is_some_and(|text| text.contains("行0")),
            "剪贴板应包含日志行文本，实际 {clipboard:?}"
        );
        let (page, log_len) = window
            .update(&mut cx, |view, _, _| {
                (view.page, view.log_state.borrow().logs.len())
            })
            .unwrap();
        assert_eq!(page, Page::Logs, "复制按钮的 Enter 不应切换页面");
        assert_eq!(log_len, 1, "复制不应改动日志缓冲");
    }

    /// 输入框的 FocusHandle 接管焦点态，且作为 tab stop 进出不陷获（AC2/AC4）。
    #[test]
    fn text_field_focus_handle_drives_focus_state() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        open_config_page(&mut cx, window);

        let field_focus = window
            .update(&mut cx, |view, _, _| {
                view.fields[&FieldId::ExifToolPath].focus.clone()
            })
            .unwrap();

        cx.update_window(any_window, |_, window, cx| window.focus(&field_focus, cx))
            .unwrap();
        draw(&mut cx, any_window);
        let focused_flag = cx
            .update_window(any_window, |_, window, _| field_focus.is_focused(window))
            .unwrap();
        assert!(focused_flag, "字段句柄应报告已聚焦（驱动焦点轮廓）");

        // Tab 离开字段（下一跳是同排的浏览按钮），Shift+Tab 再回到字段：字段在 tab stop 链上。
        press_tab(&mut cx, any_window, true);
        assert_ne!(
            focused(&mut cx, any_window),
            Some(field_focus.clone()),
            "Tab 应离开输入框而不是陷获在字段内"
        );
        press_tab(&mut cx, any_window, false);
        assert_eq!(
            focused(&mut cx, any_window),
            Some(field_focus),
            "Shift+Tab 应回到输入框"
        );
    }

    /// 最小窗口高度（820×600，对齐主窗口 window_min_size）下双向 Tab 遍历：
    /// 焦点移进 config-scroll 视口外的控件后必须被滚入视口（CodeRabbit 审计项）。
    #[test]
    fn tab_reveals_focused_config_control_at_min_window_height() {
        let (mut cx, window) = setup_sized(820., 600.);
        let any_window: AnyWindowHandle = window.into();
        open_config_page(&mut cx, window);

        // 向下走到页面深处的「审计日志目录」字段（有界循环，控件增减时不钉死跳数）。
        let deep = window
            .update(&mut cx, |view, _, _| {
                view.fields[&FieldId::AuditLogDirectory].focus.clone()
            })
            .unwrap();
        let mut reached = false;
        for _ in 0..60 {
            press_tab(&mut cx, any_window, true);
            if focused(&mut cx, any_window).as_ref() == Some(&deep) {
                reached = true;
                break;
            }
        }
        assert!(reached, "Tab 应能走到审计日志目录字段");
        assert!(
            control_in_view(&mut cx, window, &deep),
            "深部字段聚焦后应被滚入 config-scroll 视口"
        );

        // 反向回到顶部「ExifToolPath」字段，同样必须在视口内。
        let top = window
            .update(&mut cx, |view, _, _| {
                view.fields[&FieldId::ExifToolPath].focus.clone()
            })
            .unwrap();
        let mut reached_top = false;
        for _ in 0..60 {
            press_tab(&mut cx, any_window, false);
            if focused(&mut cx, any_window).as_ref() == Some(&top) {
                reached_top = true;
                break;
            }
        }
        assert!(reached_top, "Shift+Tab 应能回到 ExifToolPath 字段");
        assert!(
            control_in_view(&mut cx, window, &top),
            "反向回顶后顶部字段也应在视口内"
        );
    }

    /// 日志超过一屏时连续 Tab 逐行下探到屏幕外的复制按钮，末行之后回绕离开列表
    /// （#119 CodeRabbit 审计项：虚拟列表只登记可见行，边界必须滚动跟进而非回绕）。
    #[test]
    fn tab_walks_off_screen_log_copy_buttons_without_wrapping() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        // 60 行 × 40px ≈ 2400px，远超任何测试窗口高度，必然跨屏。
        inject_log_records(&mut cx, window, 60);

        // 切到日志页（第 4 个导航项），再走 11 跳到首行复制按钮（第 15 站）。
        for _ in 0..4 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        for _ in 0..11 {
            press_tab(&mut cx, any_window, true);
        }
        assert_eq!(
            focused_log_entry(&mut cx, window),
            Some(0),
            "11 跳后应落在首行复制按钮"
        );

        // 逐行下探：每次 Tab 焦点都落在下一行（跨屏行由边界滚动跟进揭示），不回绕。
        for expected in 1..60 {
            press_tab(&mut cx, any_window, true);
            assert_eq!(
                focused_log_entry(&mut cx, window),
                Some(expected),
                "第 {expected} 次 Tab 应落到 entry {expected} 的复制按钮而不是回绕"
            );
        }

        // 末行（entry 59）之后 Tab 离开日志列表，不再有下一行。
        press_tab(&mut cx, any_window, true);
        assert_eq!(
            focused_log_entry(&mut cx, window),
            None,
            "末行之后 Tab 应回绕离开日志列表"
        );
        let page = window.update(&mut cx, |view, _, _| view.page).unwrap();
        assert_eq!(page, Page::Logs, "逐行下探全程不应切换页面");
    }

    /// Shift+Tab 对称回退：从屏幕外逐行回退到首行，首行再往上回绕离表落在清空按钮上
    /// （#119 CodeRabbit 审计项的反向面）。
    #[test]
    fn shift_tab_walks_back_off_screen_rows_and_leaves_list_at_first_row() {
        let (mut cx, window) = setup();
        let any_window: AnyWindowHandle = window.into();
        inject_log_records(&mut cx, window, 60);

        // 切到日志页，11 跳到首行后向下跨过可见边界走到 entry 20。
        for _ in 0..4 {
            press_tab(&mut cx, any_window, true);
        }
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        for _ in 0..11 {
            press_tab(&mut cx, any_window, true);
        }
        for expected in 1..=20 {
            press_tab(&mut cx, any_window, true);
            assert_eq!(
                focused_log_entry(&mut cx, window),
                Some(expected),
                "下探第 {expected} 行失败"
            );
        }

        // Shift+Tab 逐行回退到首行（含屏幕外行由边界滚动跟进揭示）。
        for expected in (0..20).rev() {
            press_tab(&mut cx, any_window, false);
            assert_eq!(
                focused_log_entry(&mut cx, window),
                Some(expected),
                "回退应落到 entry {expected} 的复制按钮而不是回绕"
            );
        }

        // 首行再 Shift+Tab 离开列表向上：落点是清空按钮（Enter 应触发清空）。
        press_tab(&mut cx, any_window, false);
        assert_eq!(
            focused_log_entry(&mut cx, window),
            None,
            "首行之后 Shift+Tab 应回绕离开日志列表"
        );
        let generation_before = window
            .update(&mut cx, |view, _, _| view.log_state.borrow().generation)
            .unwrap();
        press_activation_key(&mut cx, any_window, "enter");
        draw(&mut cx, any_window);
        let generation_after = window
            .update(&mut cx, |view, _, _| view.log_state.borrow().generation)
            .unwrap();
        assert_ne!(
            generation_before, generation_after,
            "离表向上应落在清空按钮上且 Enter 触发清空"
        );
    }
}
