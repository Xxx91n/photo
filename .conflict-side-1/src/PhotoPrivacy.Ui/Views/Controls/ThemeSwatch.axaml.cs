using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotoPrivacy.Ui.Localization;

namespace PhotoPrivacy.Ui.Views.Controls;

// ADR 0052 A3 数据化色板目录 — 5 个社区主题预设，与 Themes/{Catppuccin,Dracula,Nord,OneDarkPro,TokyoNight}.axaml 一一对应。
public sealed record ThemeSwatchPreset(string ThemeId, string ColorHex, string LabelKey);

public static class ThemeSwatchCatalog
{
    public static IReadOnlyList<ThemeSwatchPreset> Presets { get; } = new ThemeSwatchPreset[]
    {
        new("catppuccin", "#89B4FA", "preset.catppuccin"),
        new("dracula", "#BD93F9", "preset.dracula"),
        new("nord", "#88C0D0", "preset.nord"),
        new("onedarkpro", "#61AFEF", "preset.onedarkpro"),
        new("tokyonight", "#7AA2F7", "preset.tokyonight"),
    };
}

// 票 25（架构恢复第六轮）：单个主题色板 — RadioButton 语义完整保留（Click 路由 + Tag/GroupName）。
// Label 由 LabelKey 经 LocalizationService 解析，CultureChanged 时自刷新（等价 ex:Localize 的数据化键版本）。
public partial class ThemeSwatch : UserControl
{
    public ThemeSwatch()
    {
        InitializeComponent();
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    public static readonly StyledProperty<string> ThemeIdProperty =
        AvaloniaProperty.Register<ThemeSwatch, string>(nameof(ThemeId), string.Empty);

    public static readonly StyledProperty<string?> ColorHexProperty =
        AvaloniaProperty.Register<ThemeSwatch, string?>(nameof(ColorHex));

    public static readonly StyledProperty<string?> LabelKeyProperty =
        AvaloniaProperty.Register<ThemeSwatch, string?>(nameof(LabelKey));

    public static readonly StyledProperty<string> GroupNameProperty =
        AvaloniaProperty.Register<ThemeSwatch, string>(nameof(GroupName), "ThemePreset");

    public string ThemeId
    {
        get => GetValue(ThemeIdProperty);
        set => SetValue(ThemeIdProperty, value);
    }

    public string? ColorHex
    {
        get => GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public string? LabelKey
    {
        get => GetValue(LabelKeyProperty);
        set => SetValue(LabelKeyProperty, value);
    }

    public string GroupName
    {
        get => GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelTextProperty);
        private set => SetValue(LabelTextProperty, value);
    }

    public static readonly StyledProperty<string?> LabelTextProperty =
        AvaloniaProperty.Register<ThemeSwatch, string?>(nameof(Label));

    internal RadioButton ThemePresetRadioControl => PART_Radio;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelKeyProperty)
        {
            RefreshLabel();
        }
        if (change.Property == ColorHexProperty)
        {
            PART_Swatch.Background = ParseColorHex(change.GetNewValue<string?>());
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshLabel();
        PART_Swatch.Background = ParseColorHex(ColorHex);
    }

    private static IBrush? ParseColorHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            return null; // malformed preset hex must not crash theme picker
        }
    }

    private void OnCultureChanged(object? sender, string e) => RefreshLabel();

    private void RefreshLabel()
    {
        var key = LabelKey;
        Label = string.IsNullOrEmpty(key) ? null : LocalizationService.Instance.Get(key);
    }
}
