using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 24（ADR 0061）：页面 code-behind 仅暴露控件访问器供 MainWindow.axaml.cs 统一接线。
// Pages/ 目录规约：本文件不允许出现事件订阅（+=），长活订阅只允许在服务层 / MainWindow / VM。
// 票 04（ui-craft2）返工轮：控件本体迁入 Config*Group 子件，访问器签名原样保留、转发到子件
// —— MainWindow.axaml.cs 全部消费点零改动（视觉/行为零变化硬约束）。
public partial class ConfigPage : UserControl
{
    public ConfigPage() => InitializeComponent();

    internal ComboBox ThemeVariantComboBoxControl => BehaviorGroup.ThemeVariantComboBoxControl;
    internal ComboBox LogLevelComboBoxControl => LogsGroup.LogLevelComboBoxControl;
    // 票 30：LocaleVariantComboBoxControl 访问器已删除 —— 语言下拉唯一消费方是 SelectedIndex 绑定。
    internal ListBox UserExcludedDirectoriesListBoxControl => ExcludedGroup.UserExcludedDirectoriesListBoxControl;
    internal Button AddExcludedDirectoryButtonControl => ExcludedGroup.AddExcludedDirectoryButtonControl;
    internal Button RemoveExcludedDirectoryButtonControl => ExcludedGroup.RemoveExcludedDirectoryButtonControl;
    internal ItemsControl ThemeSwatchListControl => BehaviorGroup.ThemeSwatchListControl;
}
