using Avalonia.Controls;
using Avalonia.Input;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.Views.Controls;

// 票 08（ui-craft2 / D-007 / 规范 §6）：toast 宿主 code-behind —— 只承载控件级事件路由
// （hover 展开/收叠驱动 SetExpanded、关闭钮路由 Dismiss），无长活订阅（ADR 0061 规约同构）。
public partial class ToastHost : UserControl
{
    public ToastHost() => InitializeComponent();

    private void OnItemsPointerEntered(object? sender, PointerEventArgs e)
        => ResolveService()?.SetExpanded(true);

    private void OnItemsPointerExited(object? sender, PointerEventArgs e)
        => ResolveService()?.SetExpanded(false);

    private void OnDismissClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToastItem item })
        {
            ResolveService()?.Dismiss(item);
        }
    }

    private Services.ToastService? ResolveService()
        => (DataContext as MainWindowViewModel)?.Toast;
}
