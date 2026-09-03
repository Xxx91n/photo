using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 24（ADR 0061）：页面 code-behind 仅暴露控件访问器供 MainWindow.axaml.cs 统一接线。
// Pages/ 目录规约：本文件不允许出现事件订阅（+=），长活订阅只允许在服务层 / MainWindow / VM。
public partial class ConfigPage : UserControl
{
    public ConfigPage() => InitializeComponent();

    internal ComboBox ThemeVariantComboBoxControl => ThemeVariantComboBox;
    internal ComboBox LogLevelComboBoxControl => LogLevelComboBox;
    internal ComboBox LocaleVariantComboBoxControl => LocaleVariantComboBox;
    internal ListBox UserExcludedDirectoriesListBoxControl => UserExcludedDirectoriesListBox;
    internal Button BrowseExifToolButtonControl => BrowseExifToolButton;
    internal Button BrowseHotFolderButtonControl => BrowseHotFolderButton;
    internal Button BrowseBackupDirectoryButtonControl => BrowseBackupDirectoryButton;
    internal Button BrowseQuarantineDirectoryButtonControl => BrowseQuarantineDirectoryButton;
    internal Button BrowseAuditLogDirectoryButtonControl => BrowseAuditLogDirectoryButton;
    internal Button AddExcludedDirectoryButtonControl => AddExcludedDirectoryButton;
    internal Button RemoveExcludedDirectoryButtonControl => RemoveExcludedDirectoryButton;
    internal RadioButton CatppuccinSwatchControl => CatppuccinSwatch;
    internal RadioButton DraculaSwatchControl => DraculaSwatch;
    internal RadioButton NordSwatchControl => NordSwatch;
    internal RadioButton OneDarkProSwatchControl => OneDarkProSwatch;
    internal RadioButton TokyoNightSwatchControl => TokyoNightSwatch;
}
