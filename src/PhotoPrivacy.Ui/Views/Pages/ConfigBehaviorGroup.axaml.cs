using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 04（ui-craft2）返工轮：ConfigPage 行为组子件。访问器签名与票 04 原 ConfigPage 同名，
// 由 ConfigPage.axaml.cs 转发给 MainWindow.axaml.cs（接线零改动）；无事件订阅（Pages 目录规约）。
public partial class ConfigBehaviorGroup : UserControl
{
    public ConfigBehaviorGroup() => InitializeComponent();

    internal ComboBox ThemeVariantComboBoxControl => ThemeVariantComboBox;
    internal ItemsControl ThemeSwatchListControl => ThemeSwatchList;
}
