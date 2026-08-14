using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace PhotoPrivacy.Ui.Localization;

/// <summary>
/// Avalonia 11 MarkupExtension for i18n string binding.
/// Usage in AXAML: Text="{ex:Localize nav.config}"
///
/// Returns an IBinding to LocalizationService indexer "Item[key]" so Avalonia's
/// binding engine owns lifetime and refresh: when SwitchLocale raises
/// PropertyChanged("Item[]") every bound TextBlock.Text / Button.Content
/// re-evaluates automatically. This is the industry-standard production pattern
/// per Avalonia discussions #16686, #20537 and the code4ward lifecycle article.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    private string _key = string.Empty;

    public LocalizeExtension() { }

    public LocalizeExtension(string key)
    {
        _key = key;
    }

    public string Key
    {
        get => _key;
        set => _key = value;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Source = LocalizationService.Instance,
            Path = $"[\"{_key}\"]",
            Mode = BindingMode.OneWay,
            FallbackValue = _key,
        };
        return binding;
    }
}
