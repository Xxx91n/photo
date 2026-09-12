using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 24（ADR 0061）：页面 code-behind 仅暴露控件访问器供 MainWindow.axaml.cs 统一接线（无订阅）。
public partial class RulesPage : UserControl
{
    public RulesPage() => InitializeComponent();

    internal Button SaveRulesButtonControl => SaveRulesButton;
    internal Button ResetRulesButtonControl => ResetRulesButton;
}
