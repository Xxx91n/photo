using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using Material.Icons;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.Ui.ViewModels;

// 票 08（ui-craft2 / D-007 / 规范 §6）：toast 语义型别 —— sonner rich-colors 四型。
public enum ToastKind
{
    Success,
    Information,
    Warning,
    Error,
}

/// <summary>
/// 票 08（ui-craft2 / D-007 / 规范 §6 T3-T7）：单条 toast 卡片 VM。
/// 堆叠位移/卡宽/透明度由 ToastService.RecalculateStack 统一驱动（Margin → ThicknessTransition
/// + Width/Opacity → DoubleTransition 400ms 可中断过渡，sonner 形态）；自动消失用 DispatcherTimer
/// + Stopwatch 记剩余，hover 暂停/恢复。
/// API 核验：TransformOperationsTransition 在 Avalonia 12.1.1 不存在（dll 元数据实物核证），
/// 故 sonner 的 scale 收缩以卡宽收窄 0.05/层≈18px 近似（styled-property transition 通道）。
/// </summary>
public sealed class ToastItem : INotifyPropertyChanged
{
    private Thickness _cardMargin;
    private double _cardWidth = ToastService.CardWidth;
    private double _cardOpacity;
    private bool _isCardVisible;
    private DispatcherTimer? _timer;
    private readonly Stopwatch _watch = new();
    private TimeSpan _remaining;
    private Action<ToastItem>? _onExpired;

    public ToastItem(string key, ToastKind kind, string title, string? detail)
    {
        Key = key;
        Kind = kind;
        Title = title;
        Detail = detail;
        Icon = kind switch
        {
            ToastKind.Success => MaterialIconKind.CheckCircleOutline,
            ToastKind.Information => MaterialIconKind.InfoCircleOutline,
            ToastKind.Warning => MaterialIconKind.AlertCircleOutline,
            _ => MaterialIconKind.AlertOctagonOutline,
        };

        // 入场初态：自底部 SlotHeight 下移起步 + 透明；容器物化读取本初态后，
        // service 置目标值触发 400ms 过渡（滑入+淡入）。
        _cardMargin = new Thickness(0, ToastService.SlotHeight, 0, 0);
        _cardOpacity = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public ToastKind Kind { get; }

    public string Title { get; }

    public string? Detail { get; }

    public MaterialIconKind Icon { get; }

    public string ToolTipText => Detail is { Length: > 0 } detail ? $"{Title} — {detail}" : Title;

    public bool IsSuccess => Kind == ToastKind.Success;

    public bool IsInformation => Kind == ToastKind.Information;

    public bool IsWarning => Kind == ToastKind.Warning;

    public bool IsError => Kind == ToastKind.Error;

    public Thickness CardMargin
    {
        get => _cardMargin;
        set => SetField(ref _cardMargin, value);
    }

    public double CardWidth
    {
        get => _cardWidth;
        set => SetField(ref _cardWidth, value);
    }

    public double CardOpacity
    {
        get => _cardOpacity;
        set => SetField(ref _cardOpacity, value);
    }

    public bool IsCardVisible
    {
        get => _isCardVisible;
        set => SetField(ref _isCardVisible, value);
    }

    public void StartAutoDismiss(TimeSpan duration, Action<ToastItem> onExpired)
    {
        _onExpired = onExpired;
        _remaining = duration;
        _timer = new DispatcherTimer { Interval = _remaining };
        _timer.Tick += (_, _) =>
        {
            _timer!.Stop();
            var callback = _onExpired;
            _onExpired = null;
            callback?.Invoke(this);
        };
        _watch.Restart();
        _timer.Start();
    }

    public void PauseAutoDismiss()
    {
        if (_timer is not { IsEnabled: true })
        {
            return;
        }

        _timer.Stop();
        _watch.Stop();
        _remaining -= _watch.Elapsed;
        if (_remaining < TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
        }
    }

    public void ResumeAutoDismiss()
    {
        if (_timer is null || _onExpired is null)
        {
            return;
        }

        _timer.Interval = _remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : _remaining;
        _watch.Restart();
        _timer.Start();
    }

    public void CancelAutoDismiss()
    {
        _timer?.Stop();
        _timer = null;
        _onExpired = null;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
