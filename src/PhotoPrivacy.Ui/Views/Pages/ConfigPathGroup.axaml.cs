using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 04（ui-craft2）返工轮：ConfigPage 路径组子件。code-behind 仅承载初始化，
// 控件访问器由所属组按需暴露并经 ConfigPage.axaml.cs 转发（MainWindow 接线零改动）。
public partial class ConfigPathGroup : UserControl
{
    public ConfigPathGroup() => InitializeComponent();
}
