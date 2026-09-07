using System.Globalization;
using Avalonia.Data.Converters;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.ViewModels;

/// <summary>
/// 票 30（架构恢复第七轮）：主题色板选中态 MultiBinding 转换器。
/// values[0] = MainWindowViewModel（ItemsControl.DataContext），values[1] = 色板预设 ThemeId。
/// Convert：当前 VM.ThemeId 是否即本色板（驱动 RadioButton 选中态回填，替代原
/// SyncThemeSwatchSelection/_pending+Loaded 手动回填）；MultiBinding 为 OneWay ——
/// 用户点击色板的 ThemeId 回写仍走既有 Click 事件转发（票 25 语义保留）。
/// VM 为 null（设计时/容器未就绪）时按未选中处理，不抛绑定错误。
/// </summary>
public sealed class ThemeSwatchSelectionConverter : IMultiValueConverter
{
    public static readonly ThemeSwatchSelectionConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not MainWindowViewModel vm || values[1] is not string themeId)
        {
            return false;
        }

        return string.Equals(vm.ThemeId, themeId, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // OneWay 绑定不会走 ConvertBack；防呆返回 false 保持未选中语义。
        return false;
    }
}
