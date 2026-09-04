using Avalonia;
using Avalonia.Controls;
using Material.Icons;

namespace PhotoPrivacy.Ui.Views.Controls;

// 票 25（架构恢复第六轮）：侧栏导航按钮 UserControl。
// PageTag = 页面键（config/log/rules/service），Click 经 PART_Button 点击路由到 MainWindow.OnNavigateClick；
// IsActive 为 active 态唯一驱动（MainWindow 同步自 VM CurrentPage），内部仅映射为 AppTheme Button.nav.active class。
public partial class NavButton : UserControl
{
    public NavButton() => InitializeComponent();

    public static readonly StyledProperty<MaterialIconKind> IconKindProperty =
        AvaloniaProperty.Register<NavButton, MaterialIconKind>(nameof(IconKind));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<NavButton, string?>(nameof(Label));

    public static readonly StyledProperty<string> PageTagProperty =
        AvaloniaProperty.Register<NavButton, string>(nameof(PageTag), string.Empty);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NavButton, bool>(nameof(IsActive));

    public MaterialIconKind IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string PageTag
    {
        get => GetValue(PageTagProperty);
        set => SetValue(PageTagProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    internal Button NavButtonControl => PART_Button;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            SetActiveClass(change.GetNewValue<bool>());
        }
    }

    private void SetActiveClass(bool active)
    {
        if (active)
        {
            if (!PART_Button.Classes.Contains("active"))
            {
                PART_Button.Classes.Add("active");
            }
            return;
        }
        PART_Button.Classes.Remove("active");
    }
}
