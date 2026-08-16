using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PhotoPrivacy.Ui.Behaviors;

// Ported from Files.App MIT-licensed ScrollViewerMiddleClickExtensions.cs
// (https://github.com/files-community/Files/blob/main/src/Files.App/Extensions/ScrollViewerMiddleClickExtensions.cs).
// Provides middle-click-to-pan auto-scroll behavior for ScrollViewer controls.
// ADR 0050 A3: Avalonia has no built-in middle-click autoscroll — this behavior is the canonical
// implementation. Attach via `behaviors:MiddleClickScrollBehavior.IsEnabled="True"` on any ScrollViewer.
//
// API notes (verified against Avalonia 12.1 source):
//   - ScrollViewer.Offset is a Vector (x, y); clamped automatically when set.
//   - Scrollable area is computed from Extent - Viewport (Avalonia has no ScrollableHeight/Width).
//   - PointerPressedEventArgs exposes .Timestamp (PointerPoint has no Timestamp here, only events do).
//   - AddHandler takes Avalonia.Interactivity.RoutingStrategies + bool handledEventsToo.
public sealed class MiddleClickScrollBehavior : AvaloniaObject
{
    static MiddleClickScrollBehavior()
    {
        // IObservable<T>.Subscribe(IObserver<T>) is the only BCL signature — no System.Reactive here.
        // Hand-implement an observer adapter so we add zero new dependencies (Ponytail full).
        IsEnabledProperty.Changed.Subscribe(new EnabledChangedObserver(OnIsEnabledChanged));
    }

    private sealed class EnabledChangedObserver : IObserver<AvaloniaPropertyChangedEventArgs<bool>>
    {
        private readonly Action<AvaloniaPropertyChangedEventArgs<bool>> _onNext;
        public EnabledChangedObserver(Action<AvaloniaPropertyChangedEventArgs<bool>> onNext) => _onNext = onNext;
        public void OnNext(AvaloniaPropertyChangedEventArgs<bool> value) => _onNext(value);
        public void OnError(Exception error) { /* swallow; static ctor must never throw */ }
        public void OnCompleted() { }
    }

    private static void OnIsEnabledChanged(AvaloniaPropertyChangedEventArgs<bool> e) => HandleIsEnabledChanged(e);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<MiddleClickScrollBehavior, ScrollViewer, bool>(
            "IsEnabled", defaultValue: false);

    public static bool GetIsEnabled(ScrollViewer element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void HandleIsEnabledChanged(AvaloniaPropertyChangedEventArgs<bool> e)
    {
        if (e.Sender is not ScrollViewer sv) return;
        // Attach/detach pointer handlers based on IsEnabled state. AddHandler with handledEventsToo
        // so middle-button presses that child controls mark Handled still bubble.
        if (e.NewValue.Value)
        {
            sv.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            sv.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            sv.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            sv.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        }
        else
        {
            sv.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            sv.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            sv.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            sv.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        }
    }

    private const double DeadZone = 12.0;
    private const double SpeedFactor = 0.12;
    private const double MaxSpeedPerTick = 32.0;

    private static readonly Cursor ScrollCursorAll = new(StandardCursorType.SizeAll);
    private static readonly Cursor ScrollCursorVertical = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor ScrollCursorHorizontal = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor DefaultCursor = Cursor.Default;

    private static double ScrollableVertical(ScrollViewer sv) => Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
    private static double ScrollableHorizontal(ScrollViewer sv) => Math.Max(0, sv.Extent.Width - sv.Viewport.Width);

    private sealed class AutoScrollState
    {
        public Point Anchor = default;
        public Point Current = default;
        public bool IsActive;
        public ulong ActivationTimestamp;
        public bool IgnoreActivationMiddleRelease;
        public bool HoldDetected;
        public DispatcherTimer? Timer;
        public ScrollViewer? ScrollViewer;
        public TopLevel? TopLevel;
        public bool RafRequested;
    }

    private static AutoScrollState? _state;

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var point = e.GetCurrentPoint(sv);

        // If a session is active, any press cancels it (ignore the activation click itself).
        if (_state?.IsActive == true)
        {
            if (e.Timestamp == _state.ActivationTimestamp)
                return;
            Stop();
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsMiddleButtonPressed) return;

        if (ScrollableVertical(sv) <= 0 && ScrollableHorizontal(sv) <= 0) return;

        _state = new AutoScrollState
        {
            ScrollViewer = sv,
            Anchor = point.Position,
            Current = point.Position,
            IsActive = true,
            ActivationTimestamp = e.Timestamp,
            IgnoreActivationMiddleRelease = true,
            HoldDetected = false
        };
        // ADR 0051 A3: Use TopLevel.RequestAnimationFrame (vsync-aligned) instead of DispatcherTimer.
        // Fallback to DispatcherTimer if TopLevel is null (ScrollViewer not attached to tree yet).
        var topLevel = TopLevel.GetTopLevel(sv);
        if (topLevel is not null)
        {
            _state.TopLevel = topLevel;
            _state.RafRequested = true;
            topLevel.RequestAnimationFrame(ScrollFrame);
        }
        else
        {
            _state.Timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, ScrollTimer_Tick);
            _state.Timer.Start();
        }

        sv.Cursor = GetAutoScrollCursor(sv);
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_state?.IsActive != true || _state.ScrollViewer is null) return;
        var point = e.GetCurrentPoint(_state.ScrollViewer);
        _state.Current = point.Position;

        if (point.Properties.IsMiddleButtonPressed)
        {
            var deltaY = _state.Current.Y - _state.Anchor.Y;
            var deltaX = _state.Current.X - _state.Anchor.X;
            if (Math.Abs(deltaY) > DeadZone || Math.Abs(deltaX) > DeadZone)
                _state.HoldDetected = true;
        }
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_state?.IsActive != true || _state.ScrollViewer is null) return;
        var point = e.GetCurrentPoint(_state.ScrollViewer);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonReleased) return;

        // Ignore the release of the original activation click: continue scrolling when the user
        // released after an explicit hold drag (Files.App's "hold-to-scroll" mode).
        if (_state.IgnoreActivationMiddleRelease)
        {
            _state.IgnoreActivationMiddleRelease = false;
            if (_state.HoldDetected)
            {
                Stop();
                e.Handled = true;
            }
            return;
        }

        Stop();
        e.Handled = true;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_state?.IsActive != true) return;
        if (e.Key == Key.Escape)
        {
            Stop();
            e.Handled = true;
        }
    }

    private static void ScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_state?.IsActive != true || _state.ScrollViewer is null) return;
        var sv = _state.ScrollViewer;

        var deltaY = _state.Current.Y - _state.Anchor.Y;
        var deltaX = _state.Current.X - _state.Anchor.X;

        double? newHorizontalOffset = null;
        double? newVerticalOffset = null;
        var offset = sv.Offset;

        if (ScrollableVertical(sv) > 0)
        {
            var velocityY = CalculateVelocity(deltaY);
            if (Math.Abs(velocityY) > 0)
            {
                var targetY = Math.Clamp(offset.Y + velocityY, 0, ScrollableVertical(sv));
                if (!targetY.Equals(offset.Y))
                    newVerticalOffset = targetY;
            }
        }

        if (ScrollableHorizontal(sv) > 0)
        {
            var velocityX = CalculateVelocity(deltaX);
            if (Math.Abs(velocityX) > 0)
            {
                var targetX = Math.Clamp(offset.X + velocityX, 0, ScrollableHorizontal(sv));
                if (!targetX.Equals(offset.X))
                    newHorizontalOffset = targetX;
            }
        }

        if (newVerticalOffset.HasValue || newHorizontalOffset.HasValue)
        {
            sv.Offset = new Vector(
                newHorizontalOffset ?? offset.X,
                newVerticalOffset ?? offset.Y);
        }
    }



    // ADR 0051 A3: RequestAnimationFrame callback — vsync-aligned, frame-rate independent.
    // delta * (frameTime.TotalMilliseconds / 16.67) scales velocity per frame for high-refresh displays.
    private static void ScrollFrame(TimeSpan frameTime)
    {
        if (_state?.IsActive != true || _state.ScrollViewer is null)
        {
            _state = null;
            return;
        }

        var sv = _state.ScrollViewer;
        var frameScale = frameTime.TotalMilliseconds / 16.67;

        var deltaY = _state.Current.Y - _state.Anchor.Y;
        var deltaX = _state.Current.X - _state.Anchor.X;

        double? newHorizontalOffset = null;
        double? newVerticalOffset = null;
        var offset = sv.Offset;

        if (ScrollableVertical(sv) > 0)
        {
            var velocityY = CalculateVelocity(deltaY) * frameScale;
            if (Math.Abs(velocityY) > 0)
            {
                var targetY = Math.Clamp(offset.Y + velocityY, 0, ScrollableVertical(sv));
                if (!targetY.Equals(offset.Y))
                    newVerticalOffset = targetY;
            }
        }

        if (ScrollableHorizontal(sv) > 0)
        {
            var velocityX = CalculateVelocity(deltaX) * frameScale;
            if (Math.Abs(velocityX) > 0)
            {
                var targetX = Math.Clamp(offset.X + velocityX, 0, ScrollableHorizontal(sv));
                if (!targetX.Equals(offset.X))
                    newHorizontalOffset = targetX;
            }
        }

        if (newVerticalOffset.HasValue || newHorizontalOffset.HasValue)
        {
            sv.Offset = new Vector(
                newHorizontalOffset ?? offset.X,
                newVerticalOffset ?? offset.Y);
        }

        // Request next frame (RAF is one-shot, must re-request for continuous loop)
        if (_state is { IsActive: true, TopLevel: { } tl, RafRequested: true })
        {
            tl.RequestAnimationFrame(ScrollFrame);
        }
    }

    private static void Stop()
    {
        if (_state is null) return;
        _state.RafRequested = false;
        if (_state.Timer is { } t)
        {
            t.Stop();
        }
        if (_state.ScrollViewer is { } sv)
        {
            sv.Cursor = DefaultCursor;
        }
        _state = null;
    }

    private static Cursor GetAutoScrollCursor(ScrollViewer? sv)
    {
        if (sv is null) return ScrollCursorAll;
        bool canV = ScrollableVertical(sv) > 0;
        bool canH = ScrollableHorizontal(sv) > 0;
        if (canV && canH) return ScrollCursorAll;
        if (canH) return ScrollCursorHorizontal;
        return ScrollCursorVertical;
    }

    private static double CalculateVelocity(double delta)
    {
        if (Math.Abs(delta) <= DeadZone) return 0;
        var adjustedDelta = delta - Math.Sign(delta) * DeadZone;
        return Math.Clamp(adjustedDelta * SpeedFactor, -MaxSpeedPerTick, MaxSpeedPerTick);
    }
}
