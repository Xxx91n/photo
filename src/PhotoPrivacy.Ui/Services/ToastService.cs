using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using PhotoPrivacy.Ui.ViewModels;

namespace PhotoPrivacy.Ui.Services;

/// <summary>
/// 票 08（ui-craft2 / D-007 / A-003 / 规范 §6 升现役）：应用级 toast 服务 —— 自绘
/// ItemsControl overlay 路线（atomcode 调研裁决：Ursa WindowNotificationManager/WindowToastManager
/// 仅覆盖 5/10 需求维，sonner 核心交互 hover 展开/暂停/堆叠位移缩放/可中断过渡/同 key 去重
/// 全缺，fork 补齐成本高于自绘；零新 NuGet 依赖）。
/// 形态对标 vue-sonner（规范 §6 T2/T6/T7 定值）：右下堆叠、同屏上限 4 条（折叠态）、
/// 堆叠露出 14px/层 + 卡宽收窄 0.05/层、默认 4s 自动消失、同 key 去重刷新、hover 展开露出全部。
/// API 核验：Avalonia 12.1.1 无 TransformOperationsTransition——位移/收窄用 Margin(负值重叠)
/// + Width 的 styled-property transitions（ThicknessTransition / DoubleTransition）实现可中断过渡。
/// </summary>
public sealed class ToastService : INotifyPropertyChanged
{
    // 规范 §6 T6：同屏堆叠上限 4 条（sonner visible-toasts=4）。
    public const int MaxVisible = 4;

    // 规范 §6 T2：默认 4s 自动消失（sonner 默认 duration）。
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    // 规范 §6 T7：堆叠露出 14px/层 + 缩放 0.05/层（缩放以卡宽收窄近似：364×0.05≈18px/层）。
    internal const double StackOffsetPerLayer = 14;
    internal const double StackWidthStep = 18;
    internal const double CardWidth = 364;

    // 卡高 = Padding 8×2 + 32px icon 关闭钮行（无对应 Layout token，实物定值）。
    internal const double SlotHeight = 48;
    internal const double SlotGap = 8; // SpaceSm 档（§5.2 P4 同组元素间距）。

    private readonly ObservableCollection<ToastItem> _items = new();
    private readonly Dictionary<string, ToastItem> _byKey = new();
    private readonly ReadOnlyObservableCollection<ToastItem> _itemsView;
    private bool _expanded;
    private double _hostHeight;

    public ToastService()
    {
        _itemsView = new ReadOnlyObservableCollection<ToastItem>(_items);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<ToastItem> Items => _itemsView;

    /// <summary>toast 宿主容器高：折叠态 = 卡高 + 可见堆叠层偏移（覆盖视觉堆叠 hit 区），
    /// 展开态 = 全卡整列高；ItemsControl.Height 绑定本值 + DoubleTransition 过渡。</summary>
    public double HostHeight
    {
        get => _hostHeight;
        private set
        {
            if (_hostHeight != value)
            {
                _hostHeight = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HostHeight)));
            }
        }
    }

    /// <summary>规范 §6 T6：同 key 连续触发合并刷新 —— 移除旧条、新条置顶（列表末位 =
    /// 堆叠视觉最底层/最新位），计时重置。</summary>
    public void Show(string key, ToastKind kind, string title, string? detail = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(key, kind, title, detail));
            return;
        }

        if (_byKey.Remove(key, out var stale))
        {
            stale.CancelAutoDismiss();
            _items.Remove(stale);
        }

        var item = new ToastItem(key, kind, title, detail);
        _byKey[key] = item;
        _items.Add(item);
        item.StartAutoDismiss(DefaultDuration, Dismiss);

        // 目标值延后到容器物化后设置（Loaded 优先级）：让卡片先读入场初态再过渡，保证滑入动画。
        Dispatcher.UIThread.Post(RecalculateStack, DispatcherPriority.Loaded);
    }

    public void Dismiss(ToastItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Dismiss(item));
            return;
        }

        item.CancelAutoDismiss();
        if (_items.Remove(item) && _byKey.TryGetValue(item.Key, out var current)
            && ReferenceEquals(current, item))
        {
            _byKey.Remove(item.Key);
        }

        Dispatcher.UIThread.Post(RecalculateStack, DispatcherPriority.Loaded);
    }

    /// <summary>规范 §6 T6：hover 展开露出全部 + 暂停全部计时；leave 收叠并恢复剩余计时。</summary>
    public void SetExpanded(bool expanded)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetExpanded(expanded));
            return;
        }

        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        foreach (var item in _items)
        {
            if (expanded)
            {
                item.PauseAutoDismiss();
            }
            else
            {
                item.ResumeAutoDismiss();
            }
        }

        RecalculateStack();
    }

    private void RecalculateStack()
    {
        var count = _items.Count;
        for (var index = 0; index < count; index++)
        {
            var item = _items[index];
            var depth = count - 1 - index; // 自底数层深：最新（末位）= 0
            // 折叠态：非底卡负底 margin -(卡高+间距-14) 使后继卡上移重叠，仅露上缘 14px/层
            //（StackPanel Spacing 在 margin 之上叠加，步进 = 48 + 8 + margin = 14px 严格等值）；
            // 卡宽收窄 18px/层近似 sonner 0.05/层缩放。展开态：margin/宽归零，整列铺排。
            double bottomMargin;
            double width;
            double opacity;
            bool visible;
            if (_expanded)
            {
                bottomMargin = 0;
                width = CardWidth;
                opacity = 1;
                visible = true;
            }
            else
            {
                bottomMargin = depth == 0 ? 0 : -(SlotHeight + SlotGap - StackOffsetPerLayer);
                width = CardWidth - depth * StackWidthStep;
                visible = depth < MaxVisible;
                opacity = visible ? 1 : 0;
            }

            item.CardMargin = new Thickness(0, 0, 0, bottomMargin);
            item.CardWidth = width;
            item.CardOpacity = opacity;
            item.IsCardVisible = visible;
        }

        var visibleLayers = Math.Min(count, MaxVisible);
        HostHeight = count == 0
            ? 0
            : _expanded
                ? count * (SlotHeight + SlotGap) - SlotGap
                : SlotHeight + (visibleLayers - 1) * StackOffsetPerLayer;
    }
}
