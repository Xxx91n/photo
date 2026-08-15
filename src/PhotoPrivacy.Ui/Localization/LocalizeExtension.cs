using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace PhotoPrivacy.Ui.Localization;

/// <summary>
/// Avalonia 11 MarkupExtension for i18n string binding.
/// Usage in AXAML: Text="{ex:Localize nav.config}"
///
/// Returns the translated string directly. On language switch, CultureChanged
/// fires and OnCultureChanged walks a ConditionalWeakTable that strongly
/// associates each target AvaloniaObject (Control) with its LocalizeExtension
/// instance preventing GC from collecting the extension before refresh.
/// This is the code4ward production pattern, adapted to pure BCL + Avalonia API.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    private sealed class BindingEntry
    {
        public LocalizeExtension Ext { get; }
        public AvaloniaProperty Prop { get; }
        public BindingEntry(LocalizeExtension ext, AvaloniaProperty prop)
        {
            Ext = ext;
            Prop = prop;
        }
    }

    private static readonly ConditionalWeakTable<AvaloniaObject, BindingEntry> _bindings = new();
    private static readonly object _lock = new();
    private static bool _hooked;

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

    /// <summary>Optional format args for string.Format(template, args).</summary>
    public object[]? Args { get; set; }

    private static void EnsureHook()
    {
        if (_hooked) return;
        _hooked = true;
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    private static void OnCultureChanged(object? sender, string e)
    {
        lock (_lock)
        {
            foreach (var kvp in _bindings)
            {
                try
                {
                    kvp.Value.Ext.RefreshTarget(kvp.Key, kvp.Value.Prop);
                }
                catch
                {
                    // ponytail: a single stale/disposed target must not abort
                    // refresh of all other localized controls.
                }
            }
        }
    }

    private void RefreshTarget(AvaloniaObject target, AvaloniaProperty prop)
    {
        target.SetValue(prop, ResolveValue());
    }

    private string ResolveValue()
    {
        var svc = LocalizationService.Instance;
        return Args is { Length: > 0 } ? svc.Get(_key, Args) : svc.Get(_key);
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        EnsureHook();
        var value = ResolveValue();

        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is AvaloniaObject ao
            && pvt.TargetProperty is AvaloniaProperty ap)
        {
            _bindings.AddOrUpdate(ao, new BindingEntry(this, ap));
        }

        return value;
    }
}
