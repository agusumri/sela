using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sela.Views;

/// <summary>
/// Where the notch hangs (top or bottom edge of the primary work area, any horizontal position)
/// and dragging it there. Everything is event driven: no timers, animations are one-shot.
/// </summary>
public partial class NotchWindow
{
    private const double DragThreshold = 6;   // DIP of travel before a press becomes a drag
    private const double CenterSnap = 24;     // DIP: release this close to center and it locks to center

    private NotchEdge _edge = NotchEdge.Top;
    private double _x = 0.5;

    private bool _pressed;
    private bool _dragging;
    private bool _swallowClick;
    private Point _pressScreen;   // screen DIP
    private double _grabDx;       // cursor X minus notch center X at drag start

    /// <summary>Hangs the notch from <paramref name="edge"/> with its center at <paramref name="x"/> (0..1 of the work-area width).</summary>
    public void ApplyPlacement(NotchEdge edge, double x)
    {
        _edge = edge;
        _x = double.IsFinite(x) ? Math.Clamp(x, 0, 1) : 0.5;
        PositionWindow();
    }

    /// <summary>Called once from the constructor: hook up dragging.</summary>
    private void InitPlacement()
    {
        Island.PreviewMouseLeftButtonDown += Island_PreviewDown;
        Island.PreviewMouseMove += Island_PreviewMove;
        Island.PreviewMouseLeftButtonUp += Island_PreviewUp; // runs before Island_MouseLeftButtonUp
        Island.LostMouseCapture += (_, _) => { _pressed = false; if (_dragging) EndDrag(); };
        // Registered after the XAML handler, so this runs after it and cancels any hover-intent it started.
        Shape.MouseEnter += (_, _) => { if (_dragging) _hoverIntent.Stop(); };
        ApplyEdgeLayout();
    }

    /// <summary>Moves the window to the current edge/position inside the primary work area.</summary>
    private void PositionWindow()
    {
        ApplyEdgeLayout();
        var area = SystemParameters.WorkArea;
        SetWindowPos(LeftProperty, LeftFor(area.Left + _x * area.Width, area));
        SetWindowPos(TopProperty, TopFor(_edge, area));
    }

    /// <summary>Bottom corners for a top edge, top corners for a bottom edge.</summary>
    private void SetIslandCorners(double r) =>
        Island.CornerRadius = _edge == NotchEdge.Bottom ? new CornerRadius(r, r, 0, 0) : new CornerRadius(0, 0, r, r);

    /// <summary>True (once) when the last press turned into a drag, so the click is swallowed.</summary>
    private bool ConsumeDragClick()
    {
        if (!_swallowClick) return false;
        _swallowClick = false;
        return true;
    }

    // ---------------- geometry ----------------

    /// <summary>Half the widest notch state (panel 344 + ears 20) plus a small margin.</summary>
    private const double EdgeReach = 190;

    private double LeftFor(double centerX, Rect area)
    {
        // Clamp the notch CENTER, not the window: the transparent window may hang past the screen edge,
        // so the notch can sit close to the left/right edge while even the full panel stays on screen.
        var minC = area.Left + EdgeReach;
        var maxC = Math.Max(minC, area.Right - EdgeReach);
        return Math.Round(Math.Clamp(centerX, minC, maxC) - Width / 2);
    }

    private double TopFor(NotchEdge edge, Rect area) =>
        edge == NotchEdge.Bottom ? Math.Round(area.Bottom - Height) : area.Top;

    private void SetWindowPos(DependencyProperty p, double value)
    {
        BeginAnimation(p, null);
        SetValue(p, value);
    }

    /// <summary>Anchors the shape to the active edge and mirrors the ears; the content itself is never flipped.</summary>
    private void ApplyEdgeLayout()
    {
        var bottom = _edge == NotchEdge.Bottom;
        var v = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Shape.VerticalAlignment = v;
        Island.VerticalAlignment = v;
        foreach (var ear in new FrameworkElement[] { EarLeft, EarRight })
        {
            ear.VerticalAlignment = v;
            ear.RenderTransformOrigin = new Point(0.5, 0.5);
            ear.RenderTransform = bottom ? FlipY : Transform.Identity;
        }
        ShapeShadow.Direction = bottom ? 90 : 270; // shadow falls away from the edge
        // The panel sheet's rounded edge and hairline face away from the screen edge too.
        PanelSheet.CornerRadius = bottom ? new CornerRadius(30, 30, 0, 0) : new CornerRadius(0, 0, 30, 30);
        PanelSheet.BorderThickness = bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1);
        SyncShape();
    }

    private static readonly ScaleTransform FlipY = CreateFlip();
    private static ScaleTransform CreateFlip()
    {
        var t = new ScaleTransform(1, -1);
        t.Freeze();
        return t;
    }

    // ---------------- dragging ----------------

    private Point ScreenDip(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(Left + p.X, Top + p.Y);
    }

    private void Island_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        _swallowClick = false;
        _pressed = false;
        // Drag from the pill, glance or reminder card, never from a button inside it.
        if (_mode is not (Mode.Idle or Mode.Glance or Mode.Reminder) || IsInsideButton(e.OriginalSource as DependencyObject)) return;
        _pressed = true;
        _pressScreen = ScreenDip(e);
        // Capture now: a quick flick can leave the 30 DIP pill before the drag threshold is crossed.
        Island.CaptureMouse();
    }

    private void Island_PreviewMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelPress(); return; }
        var p = ScreenDip(e);
        if (!_dragging)
        {
            if ((p - _pressScreen).Length < DragThreshold) return;
            BeginDrag();
        }
        DragTo(p);
        e.Handled = true;
    }

    private void Island_PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) { CancelPress(); return; } // a plain click: Island_MouseLeftButtonUp still fires
        _pressed = false;
        _swallowClick = true; // Island_MouseLeftButtonUp runs next and asks ConsumeDragClick
        EndDrag();
    }

    private void CancelPress()
    {
        _pressed = false;
        if (!_dragging && Island.IsMouseCaptured) Island.ReleaseMouseCapture();
    }

    private void BeginDrag()
    {
        _dragging = true;
        _hoverIntent.Stop();
        _leaveGrace.Stop();
        if (_mode == Mode.Reminder) DismissReminder();
        else if (_mode != Mode.Idle) Rest();

        // Finish any slide/snap in flight so Left/Top read the real position.
        SetWindowPos(LeftProperty, Left);
        SetWindowPos(TopProperty, Top);
        _grabDx = _pressScreen.X - (Left + Width / 2);

        if (!Island.IsMouseCaptured) Island.CaptureMouse();
        SetLiftCue(true);
    }

    private void DragTo(Point cursor)
    {
        var area = SystemParameters.WorkArea;
        Left = LeftFor(cursor.X - _grabDx, area);
        _x = Math.Clamp((Left + Width / 2 - area.Left) / area.Width, 0, 1);

        var edge = cursor.Y < area.Top + area.Height / 2 ? NotchEdge.Top : NotchEdge.Bottom;
        if (edge != _edge) SwitchEdge(edge, area);
    }

    /// <summary>Re-anchors to the other edge and slides the window there, starting where the notch is now.</summary>
    private void SwitchEdge(NotchEdge edge, Rect area)
    {
        var h = Shape.ActualHeight;
        var oldShapeTop = Top + (_edge == NotchEdge.Bottom ? Height - h : 0);
        _edge = edge;
        ApplyEdgeLayout();

        var target = TopFor(edge, area);
        if (!Motion) { SetWindowPos(TopProperty, target); return; }
        var from = oldShapeTop - (edge == NotchEdge.Bottom ? Height - h : 0);
        SlideTo(TopProperty, from, target, 280);
    }

    private void EndDrag()
    {
        _dragging = false;
        if (Island.IsMouseCaptured) Island.ReleaseMouseCapture();
        SetLiftCue(false);

        var area = SystemParameters.WorkArea;
        var center = Left + Width / 2;
        if (Math.Abs(center - (area.Left + area.Width / 2)) <= CenterSnap)
        {
            _x = 0.5;
            var target = LeftFor(area.Left + area.Width / 2, area);
            if (Motion) SlideTo(LeftProperty, Left, target, 160);
            else SetWindowPos(LeftProperty, target);
        }
        Commands.SaveNotchPlacement(_edge, _x);
    }

    /// <summary>One-shot slide: the base value is the target, so nothing is held once the clock ends.</summary>
    private void SlideTo(DependencyProperty p, double from, double to, int ms)
    {
        BeginAnimation(p, null);
        SetValue(p, to);
        BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    /// <summary>While held, the notch looks lifted: slightly translucent with a deeper shadow.</summary>
    private void SetLiftCue(bool on)
    {
        var o = on ? 0.92 : 1.0;
        Island.Opacity = EarLeft.Opacity = EarRight.Opacity = o;
        ShapeShadow.BlurRadius = on ? 36 : 28;
        ShapeShadow.ShadowDepth = on ? 10 : 6;
        ShapeShadow.Opacity = on ? 0.6 : 0.5;
    }

    private bool IsInsideButton(DependencyObject? d)
    {
        while (d is not null && !ReferenceEquals(d, Island))
        {
            if (d is ButtonBase) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}
