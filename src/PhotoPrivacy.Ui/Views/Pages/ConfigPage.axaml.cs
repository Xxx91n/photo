using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 24（ADR 0061）：页面 code-behind 仅暴露控件访问器供 MainWindow.axaml.cs 统一接线。
// Pages/ 目录规约：本文件不允许出现事件订阅（+=），长活订阅只允许在服务层 / MainWindow / VM。
public partial class ConfigPage : UserControl
{
    public ConfigPage() => InitializeComponent();

    internal ComboBox ThemeVariantComboBoxControl => ThemeVariantComboBox;
    internal ComboBox LogLevelComboBoxControl => LogLevelComboBox;
    // 票 30：LocaleVariantComboBoxControl 访问器已删除 —— 语言下拉唯一消费方是 SelectedIndex 绑定。
    internal ListBox UserExcludedDirectoriesListBoxControl => UserExcludedDirectoriesListBox;
    internal Button AddExcludedDirectoryButtonControl => AddExcludedDirectoryButton;
    internal Button RemoveExcludedDirectoryButtonControl => RemoveExcludedDirectoryButton;
    internal ItemsControl ThemeSwatchListControl => ThemeSwatchList;
}
