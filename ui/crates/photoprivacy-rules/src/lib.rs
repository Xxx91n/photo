//! `rules.json` 契约：五格式族 × 六组开关的键值资产。
//!
//! 事实源分两处：键名形如 `{family}_{group}`、每族的编码默认值在 C# `FormatRulesStore.cs`；
//! 「缺键逐项回退」在旧 UI 的 `RulesPanelViewModel.BackfillFromStore`（`GetValueOrDefault`）
//! 与 Worker 侧 `WipeRuleEngine.Resolve`/`FamilyFallback`。本 crate 合并两者语义。
//! 新 UI 与旧 UI 读写同一份 `config/rules.json`；本 crate 只镜像 schema，不改键名、不增删键集。
//!
//! 旧 UI 在 `FormatRulesStore.Load()` 里对「文件缺失 / 读取失败 / JSON 非法」一律回退默认值，
//! 本 crate 的 [`load`] 保持同语义（第二个返回值只用于让 UI 提示“文件在但读不了”）。

use std::collections::BTreeMap;
use std::ffi::OsString;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

/// 规则面板的格式族。键前缀与 C# `WipeFormatFamily` 的小写名一致。
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Family {
    Jpeg,
    Raw,
    Video,
    Pdf,
    Eps,
}

impl Family {
    pub const ALL: [Family; 5] = [
        Family::Jpeg,
        Family::Raw,
        Family::Video,
        Family::Pdf,
        Family::Eps,
    ];

    /// `rules.json` 的族前缀（无斜杠——历史实现曾写出 `eps/ps` 键，属错配）。
    pub const fn key(self) -> &'static str {
        match self {
            Family::Jpeg => "jpeg",
            Family::Raw => "raw",
            Family::Video => "video",
            Family::Pdf => "pdf",
            Family::Eps => "eps",
        }
    }

    pub const fn label(self) -> &'static str {
        match self {
            Family::Jpeg => "JPEG",
            Family::Raw => "RAW",
            Family::Video => "Video",
            Family::Pdf => "PDF",
            Family::Eps => "EPS/PS",
        }
    }

    /// 面板上的扩展名展示串，与旧 UI `FormatRuleRow.Extensions` 一致。
    pub const fn extensions(self) -> &'static str {
        match self {
            Family::Jpeg => "jpg jpeg jpe",
            Family::Raw => "cr2 cr3 arw nef orf",
            Family::Video => "mov mp4 m4v",
            Family::Pdf => "pdf",
            Family::Eps => "eps ps ai",
        }
    }

    /// PDF 永不真正删除、EPS 原生标签不可删——面板需要警示。
    pub const fn requires_warning(self) -> bool {
        matches!(self, Family::Pdf | Family::Eps)
    }

    /// 与 C# `FormatRulesStore.FamilyDefaults` 逐项一致的安全默认值。
    pub const fn defaults(self) -> GroupSet {
        match self {
            Family::Jpeg => GroupSet::new(true, true, false, false, false, false),
            Family::Raw => GroupSet::new(false, false, true, true, true, false),
            Family::Video => GroupSet::new(true, false, false, false, false, true),
            Family::Pdf => GroupSet::new(true, false, false, false, false, false),
            Family::Eps => GroupSet::new(true, false, false, false, false, false),
        }
    }
}

/// 逐组开关。键名与 C# `FormatRulesStore.Key(family, group)` 的 `group` 参数一致。
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Group {
    StripAll,
    PreserveIcc,
    StripExif,
    StripXmp,
    StripIptc,
    StripTime,
}

impl Group {
    pub const ALL: [Group; 6] = [
        Group::StripAll,
        Group::PreserveIcc,
        Group::StripExif,
        Group::StripXmp,
        Group::StripIptc,
        Group::StripTime,
    ];

    pub const fn key(self) -> &'static str {
        match self {
            Group::StripAll => "strip_all",
            Group::PreserveIcc => "preserve_icc",
            Group::StripExif => "strip_exif",
            Group::StripXmp => "strip_xmp",
            Group::StripIptc => "strip_iptc",
            Group::StripTime => "strip_time",
        }
    }

    /// 表头用的短名（列头宽度有限）。
    pub const fn short_label(self) -> &'static str {
        match self {
            Group::StripAll => "Strip all",
            Group::PreserveIcc => "Keep ICC",
            Group::StripExif => "EXIF",
            Group::StripXmp => "XMP",
            Group::StripIptc => "IPTC",
            Group::StripTime => "Time",
        }
    }
}

/// 一个格式族的六个开关。
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GroupSet {
    pub strip_all: bool,
    pub preserve_icc: bool,
    pub strip_exif: bool,
    pub strip_xmp: bool,
    pub strip_iptc: bool,
    pub strip_time: bool,
}

impl GroupSet {
    pub const fn new(
        strip_all: bool,
        preserve_icc: bool,
        strip_exif: bool,
        strip_xmp: bool,
        strip_iptc: bool,
        strip_time: bool,
    ) -> Self {
        Self {
            strip_all,
            preserve_icc,
            strip_exif,
            strip_xmp,
            strip_iptc,
            strip_time,
        }
    }

    pub const fn get(self, group: Group) -> bool {
        match group {
            Group::StripAll => self.strip_all,
            Group::PreserveIcc => self.preserve_icc,
            Group::StripExif => self.strip_exif,
            Group::StripXmp => self.strip_xmp,
            Group::StripIptc => self.strip_iptc,
            Group::StripTime => self.strip_time,
        }
    }

    pub fn set(&mut self, group: Group, value: bool) {
        match group {
            Group::StripAll => self.strip_all = value,
            Group::PreserveIcc => self.preserve_icc = value,
            Group::StripExif => self.strip_exif = value,
            Group::StripXmp => self.strip_xmp = value,
            Group::StripIptc => self.strip_iptc = value,
            Group::StripTime => self.strip_time = value,
        }
    }
}

/// 完整的 `rules.json` 键值资产：五族 × 六组 = 30 个布尔键。
// Design(#49): rules.json 是新旧 UI 共用的键值资产——键名与键集由 C# FormatRulesStore 定义，本 crate 只镜像。
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FormatRules {
    values: BTreeMap<String, bool>,
}

impl Default for FormatRules {
    fn default() -> Self {
        Self::defaults()
    }
}

impl FormatRules {
    /// 全部 30 个键的编码默认值（与旧 UI「恢复默认」后读到的值一致）。
    pub fn defaults() -> Self {
        let mut values = BTreeMap::new();
        for family in Family::ALL {
            let defaults = family.defaults();
            for group in Group::ALL {
                values.insert(key(family, group), defaults.get(group));
            }
        }
        Self { values }
    }

    /// 用存储值覆盖默认值：只认已知键，缺键逐项回退行默认
    /// （旧 UI `RulesPanelViewModel.BackfillFromStore` / Worker `WipeRuleEngine` 同语义）。
    pub fn from_values(values: BTreeMap<String, bool>) -> Self {
        let mut merged = Self::defaults();
        for (stored_key, value) in values {
            if let Some(slot) = merged.values.get_mut(&stored_key) {
                *slot = value;
            }
        }
        merged
    }

    /// 解析 `rules.json`。语法或值类型非法时返回错误，由调用方决定回退。
    pub fn from_json(json: &str) -> Result<Self, serde_json::Error> {
        let values: BTreeMap<String, bool> = serde_json::from_str(json)?;
        Ok(Self::from_values(values))
    }

    /// 与旧 UI 相同的落盘形态：UTF-8 JSON 对象，键为 `{family}_{group}`，值为布尔。
    pub fn to_json(&self) -> String {
        serde_json::to_string(&self.values).expect("bool map is always serializable")
    }

    pub fn values(&self) -> &BTreeMap<String, bool> {
        &self.values
    }

    pub fn get(&self, family: Family, group: Group) -> bool {
        self.values
            .get(&key(family, group))
            .copied()
            .unwrap_or_else(|| family.defaults().get(group))
    }

    pub fn set(&mut self, family: Family, group: Group, value: bool) {
        self.values.insert(key(family, group), value);
    }

    pub fn family(&self, family: Family) -> GroupSet {
        let mut set = family.defaults();
        for group in Group::ALL {
            set.set(group, self.get(family, group));
        }
        set
    }
}

/// 规范键名：`{family}_{group}`（族前缀无斜杠）。
pub fn key(family: Family, group: Group) -> String {
    format!("{}_{}", family.key(), group.key())
}

/// 读取规则。文件缺失、读取失败、JSON 非法都回退默认值；第二个返回值只在
/// 「文件在但读不了」时非空，供 UI 提示。这是旧 UI `FormatRulesStore.Load()` 的语义。
pub fn load(path: &Path) -> (FormatRules, Option<String>) {
    match fs::read_to_string(path) {
        // 旧 UI 的 File.ReadAllText 会自动剥离 UTF-8 BOM；这里同样处理，避免 BOM 文件被当成非法 JSON。
        Ok(json) => match FormatRules::from_json(json.trim_start_matches('\u{feff}')) {
            Ok(rules) => (rules, None),
            Err(error) => (
                FormatRules::defaults(),
                Some(format!("rules.json is invalid; using defaults: {error}")),
            ),
        },
        Err(error) if error.kind() == io::ErrorKind::NotFound => (FormatRules::defaults(), None),
        Err(error) => (
            FormatRules::defaults(),
            Some(format!(
                "rules.json could not be read; using defaults: {error}"
            )),
        ),
    }
}

/// 原子写：临时文件 + 同目录 rename，避免 Worker/旧 UI 读到半截 JSON。
pub fn save(rules: &FormatRules, path: &Path) -> io::Result<()> {
    if let Some(parent) = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
    {
        fs::create_dir_all(parent)?;
    }

    let tmp = temp_path(path);
    if let Err(error) = fs::write(&tmp, rules.to_json()) {
        // 写失败可能留下半截临时文件；与 rename 失败路径一样清理后再返回。
        let _ = fs::remove_file(&tmp);
        return Err(error);
    }
    match fs::rename(&tmp, path) {
        Ok(()) => Ok(()),
        Err(error) => {
            let _ = fs::remove_file(&tmp);
            Err(error)
        }
    }
}

/// 恢复默认：删除文件即可（不存在视为成功），下一次读取回退编码默认值。
pub fn reset(path: &Path) -> io::Result<()> {
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(error),
    }
}

/// 与旧 UI 相同的默认位置：UI 可执行文件同目录下的 `config/rules.json`。
pub fn default_store_path() -> Option<PathBuf> {
    let exe = std::env::current_exe().ok()?;
    Some(exe.parent()?.join("config").join("rules.json"))
}

fn temp_path(path: &Path) -> PathBuf {
    // 迁移期旧 UI 会写同一个 rules.json；临时名带 pid + 序号，避免两个写端互抢同一临时文件。
    static SEQUENCE: AtomicU64 = AtomicU64::new(0);
    let mut tmp: OsString = path.as_os_str().to_owned();
    tmp.push(format!(
        ".{}.{}.tmp",
        std::process::id(),
        SEQUENCE.fetch_add(1, Ordering::Relaxed)
    ));
    PathBuf::from(tmp)
}

/// 规则页的可测试状态机：专家提示、脏标记、保存/重置结果。渲染层只把点击映射到这里。
#[derive(Debug)]
pub struct RulesEditor {
    store_path: PathBuf,
    rules: FormatRules,
    // 旧 UI 的专家开关切换提示条，并在加载/重置时决定未持久化 Enabled 列的初值；
    // 六开关始终可编辑。新页没有 Enabled 列，故这里只切换提示条。
    expert_mode: bool,
    dirty: bool,
    notice: Option<Notice>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Notice {
    LoadWarning(String),
    Saved,
    Reset,
    SaveFailed(String),
    ResetFailed(String),
}

impl Notice {
    pub fn text(&self) -> String {
        match self {
            Notice::LoadWarning(message) => message.clone(),
            Notice::Saved => "Rules saved to config/rules.json".to_string(),
            Notice::Reset => "Rules restored to defaults".to_string(),
            Notice::SaveFailed(error) => format!("Could not save rules: {error}"),
            Notice::ResetFailed(error) => format!("Could not reset rules: {error}"),
        }
    }
}

impl RulesEditor {
    pub fn open(store_path: PathBuf) -> Self {
        let (rules, warning) = load(&store_path);
        Self {
            store_path,
            rules,
            expert_mode: false,
            dirty: false,
            notice: warning.map(Notice::LoadWarning),
        }
    }

    /// 与旧 UI 相同的默认位置：UI 同目录 `config/rules.json`。
    pub fn open_default() -> Self {
        Self::open(default_store_path().unwrap_or_else(|| PathBuf::from("config/rules.json")))
    }

    pub fn store_path(&self) -> &Path {
        &self.store_path
    }

    pub fn rules(&self) -> &FormatRules {
        &self.rules
    }

    /// 旧 UI 的专家开关会切换提示条与未持久化的 Enabled 列，但不改变六开关的可编辑面；
    /// 新页没有 Enabled 列，故这里只切换提示条。
    pub fn expert_mode(&self) -> bool {
        self.expert_mode
    }

    pub fn set_expert_mode(&mut self, enabled: bool) {
        self.expert_mode = enabled;
    }

    pub fn dirty(&self) -> bool {
        self.dirty
    }

    pub fn notice(&self) -> Option<&Notice> {
        self.notice.as_ref()
    }

    /// 切换一个开关。
    pub fn toggle(&mut self, family: Family, group: Group) {
        let next = !self.rules.get(family, group);
        self.rules.set(family, group, next);
        self.dirty = true;
        self.notice = None;
    }

    pub fn save(&mut self) {
        self.notice = match save(&self.rules, &self.store_path) {
            Ok(()) => {
                self.dirty = false;
                Some(Notice::Saved)
            }
            Err(error) => Some(Notice::SaveFailed(error.to_string())),
        };
    }

    pub fn reset(&mut self) {
        self.notice = match reset(&self.store_path) {
            Ok(()) => {
                self.rules = FormatRules::defaults();
                self.dirty = false;
                Some(Notice::Reset)
            }
            Err(error) => Some(Notice::ResetFailed(error.to_string())),
        };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 与 C# `FormatRulesStore.FamilyDefaults` 逐项对照的表。
    const LEGACY_DEFAULTS: [(Family, GroupSet); 5] = [
        (
            Family::Jpeg,
            GroupSet::new(true, true, false, false, false, false),
        ),
        (
            Family::Raw,
            GroupSet::new(false, false, true, true, true, false),
        ),
        (
            Family::Video,
            GroupSet::new(true, false, false, false, false, true),
        ),
        (
            Family::Pdf,
            GroupSet::new(true, false, false, false, false, false),
        ),
        (
            Family::Eps,
            GroupSet::new(true, false, false, false, false, false),
        ),
    ];

    /// fixture 是「新 UI 会写出」的形态；.NET 侧 `FormatRulesStoreTests` 读同一份文件。
    const GOLDEN: &str = include_str!("../testdata/rules.golden.json");

    fn custom_rules() -> FormatRules {
        let mut rules = FormatRules::defaults();
        rules.set(Family::Jpeg, Group::StripAll, false);
        rules.set(Family::Jpeg, Group::StripExif, true);
        rules.set(Family::Raw, Group::PreserveIcc, true);
        rules.set(Family::Video, Group::StripAll, false);
        rules.set(Family::Video, Group::StripTime, false);
        rules.set(Family::Pdf, Group::StripAll, false);
        rules.set(Family::Eps, Group::StripTime, true);
        rules
    }

    fn temp_dir() -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "photoprivacy-rules-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn defaults_match_the_legacy_store() {
        // 防：默认值漂移让「恢复默认」后新旧 UI 得出不同的擦除命令（#56）。
        let rules = FormatRules::defaults();
        for (family, expected) in LEGACY_DEFAULTS {
            assert_eq!(rules.family(family), expected, "family {:?}", family);
        }
        assert_eq!(rules.values().len(), 30);
    }

    #[test]
    fn key_schema_matches_the_legacy_store() {
        // 防：键名大小写/分隔符漂移（如 camelCase 或 `eps/ps`），旧 UI 读不到而静默回退默认（#56）。
        assert_eq!(key(Family::Jpeg, Group::StripAll), "jpeg_strip_all");
        assert_eq!(key(Family::Eps, Group::PreserveIcc), "eps_preserve_icc");
        for value_key in FormatRules::defaults().values().keys() {
            let (family, group) = value_key
                .split_once('_')
                .unwrap_or_else(|| panic!("key {value_key} must be family_group"));
            assert!(Family::ALL.iter().any(|f| f.key() == family));
            assert!(Group::ALL.iter().any(|g| g.key() == group));
            assert!(!value_key.contains('/'));
        }
    }

    #[test]
    fn loads_the_shared_golden_fixture() {
        // 防：读端不能解析旧 UI / 新 UI 共用的落盘形态，规则页一开就显示默认值（#56）。
        let rules = FormatRules::from_json(GOLDEN).unwrap();
        assert_eq!(rules, custom_rules());
    }

    #[test]
    fn golden_fixture_covers_every_canonical_key() {
        // 防：fixture 漏掉新增族/组，互读测试只覆盖部分 schema 而假装通过（#56）。
        let fixture: BTreeMap<String, bool> = serde_json::from_str(GOLDEN).unwrap();
        let canonical: Vec<_> = FormatRules::defaults().values().keys().cloned().collect();
        let mut fixture_keys: Vec<_> = fixture.keys().cloned().collect();
        fixture_keys.sort();
        assert_eq!(fixture_keys, canonical);
    }

    #[test]
    fn golden_fixture_matches_writer_output() {
        // 防：fixture 与 Rust writer 各自漂移，.NET 互读测试读到的不是新 UI 真正写出的形状（#56）。
        let writer: BTreeMap<String, bool> =
            serde_json::from_str(&custom_rules().to_json()).unwrap();
        let fixture: BTreeMap<String, bool> = serde_json::from_str(GOLDEN).unwrap();
        assert_eq!(writer, fixture);
    }

    #[test]
    fn writer_output_reloads_to_the_same_rules() {
        // 防：写出的 JSON 含多余字段/错误类型，旧 UI 反序列化失败后静默回退默认（#56）。
        let written = custom_rules().to_json();
        assert_eq!(FormatRules::from_json(&written).unwrap(), custom_rules());
        let values: BTreeMap<String, bool> = serde_json::from_str(&written).unwrap();
        assert_eq!(values.len(), 30);
    }

    #[test]
    fn save_and_load_round_trip_through_disk() {
        // 防：原子写留下 .tmp、或写入后读不回来，导致重启 UI 丢失编辑（#56）。
        let dir = temp_dir();
        let path = dir.join("rules.json");
        let saved = custom_rules();

        save(&saved, &path).unwrap();
        // 目录里只应剩 rules.json：临时文件必须已被 rename 掉。
        assert_eq!(fs::read_dir(&dir).unwrap().count(), 1);

        let (loaded, warning) = load(&path);
        assert!(warning.is_none());
        assert_eq!(loaded, saved);

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn missing_file_is_silent_defaults() {
        // 防：首次启动把「没有 rules.json」当错误弹提示，而旧 UI 静默用默认（#56）。
        let dir = temp_dir();
        let (rules, warning) = load(&dir.join("rules.json"));
        assert!(warning.is_none());
        assert_eq!(rules, FormatRules::defaults());
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn bom_prefixed_file_loads_the_stored_rules() {
        // 防：手写/其他工具写出的 UTF-8 BOM 让新 UI 回退默认，而旧 UI 的 File.ReadAllText 能读（#56）。
        let dir = temp_dir();
        let path = dir.join("rules.json");
        fs::write(&path, "\u{feff}{\"jpeg_strip_all\":false}").unwrap();

        let (rules, warning) = load(&path);
        assert!(warning.is_none());
        assert!(!rules.get(Family::Jpeg, Group::StripAll));

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn invalid_json_falls_back_to_defaults_with_warning() {
        // 防：损坏的 rules.json 让规则页崩溃或空表，而旧 UI 回退默认继续工作（#56）。
        let dir = temp_dir();
        let path = dir.join("rules.json");
        fs::write(&path, "{ not json").unwrap();

        let (rules, warning) = load(&path);
        assert_eq!(rules, FormatRules::defaults());
        assert!(warning.is_some_and(|text| text.contains("invalid")));

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn partial_store_falls_back_per_key() {
        // 防：旧版只写 strip_all 的 rules.json 被整表重置——缺键必须逐项回退行默认（#56）。
        let rules =
            FormatRules::from_json(r#"{"jpeg_strip_all":false,"video_strip_time":false}"#).unwrap();
        assert!(!rules.get(Family::Jpeg, Group::StripAll));
        assert!(rules.get(Family::Jpeg, Group::PreserveIcc));
        assert!(!rules.get(Family::Video, Group::StripTime));
        assert!(rules.get(Family::Raw, Group::StripExif));
    }

    #[test]
    fn unknown_keys_are_ignored_and_not_written_back() {
        // 防：未知键（其他版本写入）被当成规则使用，或保存时把非法键原样传出（#56）。
        let rules =
            FormatRules::from_json(r#"{"jpeg_strip_all":false,"future_rule":true}"#).unwrap();
        assert!(!rules.get(Family::Jpeg, Group::StripAll));
        assert!(!rules.values().contains_key("future_rule"));
        let written: BTreeMap<String, bool> = serde_json::from_str(&rules.to_json()).unwrap();
        assert!(!written.contains_key("future_rule"));
    }

    #[test]
    fn reset_removes_the_store_and_falls_back_to_defaults() {
        // 防：「恢复默认」只改内存不删文件，重启后自定义规则复活（#56）。
        let dir = temp_dir();
        let path = dir.join("rules.json");
        save(&custom_rules(), &path).unwrap();

        reset(&path).unwrap();
        assert!(!path.exists());
        assert_eq!(load(&path).0, FormatRules::defaults());
        reset(&path).unwrap();

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn expert_mode_does_not_lock_any_toggle() {
        // 防：把旧 UI 的专家提示条误做成硬门，冻结版里本就允许编辑的 Video/PDF/EPS 开关在新 UI 点不动（#56）。
        let dir = temp_dir();
        let mut editor = RulesEditor::open(dir.join("rules.json"));

        let before = editor.rules().get(Family::Video, Group::StripAll);
        editor.toggle(Family::Video, Group::StripAll);
        assert_eq!(editor.rules().get(Family::Video, Group::StripAll), !before);
        assert!(editor.dirty());

        editor.set_expert_mode(true);
        assert!(editor.expert_mode());

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn save_persists_edits_and_clears_dirty() {
        // 防：点击保存只清脏标记没落盘，重启后编辑丢失（#56）。
        let dir = temp_dir();
        let path = dir.join("rules.json");
        let mut editor = RulesEditor::open(path.clone());

        editor.toggle(Family::Jpeg, Group::StripAll);
        editor.save();

        assert!(!editor.dirty());
        assert_eq!(editor.notice(), Some(&Notice::Saved));
        assert_eq!(load(&path).0, editor.rules().clone());
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn reset_restores_defaults_in_memory_and_disk() {
        // 防：重置只删文件不重载内存，页面仍显示旧开关值（#56）。
        let dir = temp_dir();
        let mut editor = RulesEditor::open(dir.join("rules.json"));
        editor.toggle(Family::Jpeg, Group::StripAll);
        editor.save();

        editor.reset();
        assert_eq!(editor.rules(), &FormatRules::defaults());
        assert!(!editor.dirty());
        assert_eq!(editor.notice(), Some(&Notice::Reset));

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn open_surfaces_a_load_warning_but_still_uses_defaults() {
        // 防：损坏的 rules.json 被静默吞掉，用户不知道当前显示的不是自己的规则（#56）。
        let dir = temp_dir();
        let path = dir.join("rules.json");
        fs::write(&path, "not json").unwrap();

        let editor = RulesEditor::open(path);
        assert_eq!(editor.rules(), &FormatRules::defaults());
        assert!(matches!(editor.notice(), Some(Notice::LoadWarning(_))));

        fs::remove_dir_all(dir).unwrap();
    }
}
