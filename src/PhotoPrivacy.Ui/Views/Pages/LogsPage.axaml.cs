using Avalonia.Controls;

namespace PhotoPrivacy.Ui.Views.Pages;

// 票 24（ADR 0061）：页面 code-behind 仅暴露控件访问器供 MainWindow.axaml.cs 统一接线（无订阅）。
public partial class LogsPage : UserControl
{
    public LogsPage() => InitializeComponent();

    internal Button ClearLogsButtonControl => ClearLogsButton;

    // 票 05（ui-craft2）：级别过滤下拉访问器 —— MainWindow.RefreshI18nComboBoxItems 刷新接线用。
    internal ComboBox LogLevelComboBoxControl => LogLevelComboBox;
}
