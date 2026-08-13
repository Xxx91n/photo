using Avalonia;
using Avalonia.Markup.Xaml;

namespace PhotoPrivacy.Ui.Localization;

/// <summary>
/// Avalonia MarkupExtension for i18n string binding.
/// Usage in AXAML: Text="{ex:Localize nav.config}"
/// Subscribes to LocalizationService.CultureChanged; auto-refreshes target via WeakReference.
/// ponytail: live-refresh uses Cached<AvaloniaObject, AvaloniaProperty> not reflection; upgrade to IBinding if perf matters.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    private static readonly List<WeakReference<LocalizeExtension>> _instances = new();
    private static bool _hooked;

    private string _key = string.Empty;
    private WeakReference<AvaloniaObject>? _targetRef;
    private AvaloniaProperty? _targetProperty;

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
        lock (_instances)
        {
            for (var i = _instances.Count - 1; i >= 0; i--)
            {
                if (!_instances[i].TryGetTarget(out var ext))
                {
                    _instances.RemoveAt(i);
                    continue;
                }
                ext.RefreshTarget();
            }
        }
    }

    private void RefreshTarget()
    {
        if (_targetRef is null || _targetProperty is null) return;
        if (!_targetRef.TryGetTarget(out var target)) return;
        target.SetValue(_targetProperty, ResolveValue());
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
            _targetRef = new WeakReference<AvaloniaObject>(ao);
            _targetProperty = ap;
            lock (_instances)
            {
                _instances.Add(new WeakReference<LocalizeExtension>(this));
            }
        }

        return value;
    }
}
