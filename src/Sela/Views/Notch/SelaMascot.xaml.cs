using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sela.Core;

namespace Sela.Views.Notch;

/// <summary>Tiny animated crescent/sun that lives in the idle pill. Designed for 30x30 DIP.</summary>
public partial class SelaMascot : UserControl
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(SkyPhase), typeof(SelaMascot),
        new PropertyMetadata(SkyPhase.Night, (d, _) => ((SelaMascot)d).ApplyPhase()));

    public static readonly DependencyProperty IsExcitedProperty = DependencyProperty.Register(
        nameof(IsExcited), typeof(bool), typeof(SelaMascot),
        new PropertyMetadata(false, (d, _) => ((SelaMascot)d).Refresh()));

    public static readonly DependencyProperty LookXProperty = DependencyProperty.Register(
        nameof(LookX), typeof(double), typeof(SelaMascot),
        new PropertyMetadata(0.0, (d, _) => ((SelaMascot)d).ApplyLook()));

    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(
        nameof(IsAnimating), typeof(bool), typeof(SelaMascot),
        new PropertyMetadata(false, (d, _) => ((SelaMascot)d).Refresh()));

    /// <summary>Sky mood: Dawn/Night = crescent moon, Day = small sun, Dusk = warm crescent.</summary>
    public SkyPhase Phase { get => (SkyPhase)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    /// <summary>True when the next prayer is close: bounce, brighter glow, pulse ring.</summary>
    public bool IsExcited { get => (bool)GetValue(IsExcitedProperty); set => SetValue(IsExcitedProperty, value); }

    /// <summary>Where the mascot looks, -1 (left) .. 1 (right); 0 = straight ahead.</summary>
    public double LookX { get => (double)GetValue(LookXProperty); set => SetValue(LookXProperty, value); }

    /// <summary>Starts or stops all ambient animations (breathe, blink, twinkle, rare events).</summary>
    public bool IsAnimating { get => (bool)GetValue(IsAnimatingProperty); set => SetValue(IsAnimatingProperty, value); }

    private static readonly Color Accent = Color.FromRgb(0x3D, 0xDC, 0x97);
    private static readonly IEasingFunction Sine = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });
    private static readonly IEasingFunction Out = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    private readonly DispatcherTimer _blinkTimer = new();
    private readonly DispatcherTimer _shootTimer = new();
    private readonly Random _rng = new();

    public SelaMascot()
    {
        InitializeComponent();
        _blinkTimer.Tick += (_, _) => { Blink_(); _blinkTimer.Interval = Seconds(6, 12); };
        _shootTimer.Tick += (_, _) => { ShootingStar(); _shootTimer.Interval = Seconds(45, 90); };
        Loaded += (_, _) => { ApplyPhase(); Refresh(); };
        Unloaded += (_, _) => Stop();
        ApplyPhase();
    }

    private bool Live => IsAnimating && IsLoaded && SystemParameters.ClientAreaAnimation;

    private void ApplyPhase()
    {
        var phase = Phase;
        int ms = Live ? 400 : 0;
        Color body = phase switch
        {
            SkyPhase.Dawn => Color.FromRgb(0x9D, 0xB4, 0xFF),
            SkyPhase.Dusk => Color.FromRgb(0xFF, 0xB0, 0x66),
            _ => Color.FromRgb(0xEA, 0xFB, 0xF3),
        };
        Color glow = phase switch
        {
            SkyPhase.Day => Color.FromRgb(0xFF, 0xD3, 0x6E),
            SkyPhase.Night => Accent,
            _ => body,
        };
        Tween(MoonBrush, SolidColorBrush.ColorProperty, body, ms);
        Tween(GlowStop, GradientStop.ColorProperty, glow, ms);
        Fade(Moon, phase == SkyPhase.Day ? 0 : 1, ms);
        Fade(Sun, phase == SkyPhase.Day ? 1 : 0, ms);
        Fade(StarLayer, phase == SkyPhase.Night ? 1 : 0, ms);
        Fade(Horizon, phase == SkyPhase.Dawn ? 1 : 0, ms);
        if (Live) StartRays(); // rays only spin by day
    }

    private void ApplyLook()
    {
        double x = Math.Clamp(LookX, -1, 1);
        // Slow and small, but enough to read at 30 DIP: drift 3 DIP and lean 7° toward the pointer.
        int ms = Live ? 450 : 0;
        Tween(Look, TranslateTransform.XProperty, x * 3, ms);
        Tween(Tilt, RotateTransform.AngleProperty, x * 7, ms);
    }

    /// <summary>Rebuilds ambient motion from current state, or settles into the static pose.</summary>
    private void Refresh()
    {
        Stop();
        bool excited = IsExcited;
        if (!Live)
        {
            Glow.Opacity = excited ? 0.6 : 0.35;
            foreach (var s in Stars) s.Opacity = 0.8;
            return;
        }

        // Breathing: a slow, shallow 7 s rhythm — calm, not busy.
        var breathe = Loop(1.0, 1.03, 3500);
        Breathe.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
        Breathe.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        Glow.BeginAnimation(OpacityProperty, excited ? Loop(0.45, 0.65, 3500) : Loop(0.28, 0.4, 3500));

        // Stars drift in and out gently, never fully off.
        int[] twinkle = [3200, 4100, 3700, 4600];
        for (int i = 0; i < Stars.Length; i++)
        {
            var a = Loop(0.45, 0.95, twinkle[i]);
            a.BeginTime = TimeSpan.FromMilliseconds(i * 900);
            Stars[i].BeginAnimation(OpacityProperty, a);
        }

        if (excited)
        {
            // Near prayer time: a slow float and a soft halo instead of a bounce.
            Bounce.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1, 1800));
            var period = new Duration(TimeSpan.FromMilliseconds(3200));
            var grow = Forever(0.8, 1.4, period, Out);
            RingScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            RingScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            Ring.BeginAnimation(OpacityProperty, Forever(0.35, 0, period));
        }

        StartRays();
        _blinkTimer.Interval = Seconds(6, 12);
        _shootTimer.Interval = Seconds(90, 180);
        _blinkTimer.Start();
        _shootTimer.Start();
    }

    private void Stop()
    {
        _blinkTimer.Stop();
        _shootTimer.Stop();
        Breathe.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Breathe.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Bounce.BeginAnimation(TranslateTransform.YProperty, null);
        RingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Rays.BeginAnimation(RotateTransform.AngleProperty, null);
        _raysSpinning = false;
        StreakMove.BeginAnimation(TranslateTransform.XProperty, null);
        StreakMove.BeginAnimation(TranslateTransform.YProperty, null);
        foreach (UIElement e in new UIElement[] { Glow, Ring, Streak, Star1, Star2, Star3, Star4 })
            e.BeginAnimation(OpacityProperty, null);
        Ring.Opacity = 0;
        Streak.Opacity = 0;
    }

    private bool _raysSpinning;

    private void StartRays()
    {
        bool spin = Phase == SkyPhase.Day;
        if (spin == _raysSpinning) return; // never restart a turn that is already running
        _raysSpinning = spin;
        // One slow, continuous full turn (24 s), picking up from the current angle so it never jumps.
        Rays.BeginAnimation(RotateTransform.AngleProperty, spin
            ? Forever(Rays.Angle % 360, Rays.Angle % 360 + 360, Ms(24000))
            : null);
    }

    private void Blink_()
    {
        if (Phase == SkyPhase.Day) return; // the sun doesn't wink
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.2, Ms(90)) { AutoReverse = true, EasingFunction = Sine });
    }

    private void ShootingStar()
    {
        if (Phase != SkyPhase.Night) return;
        var d = Ms(650);
        StreakMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-10, 34, d));
        StreakMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(1, 19, d));
        var fade = new DoubleAnimationUsingKeyFrames { Duration = d };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.3)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Streak.BeginAnimation(OpacityProperty, fade);
    }

    private UIElement[] Stars => [Star1, Star2, Star3, Star4];

    // Ambient loops run forever, so cap them well below the default 60 fps: a 30 DIP figure
    // breathing over seconds looks identical at 24 fps and costs a fraction of the CPU.
    private const int AmbientFps = 24;

    private static DoubleAnimation Loop(double from, double to, int halfMs)
    {
        var a = new DoubleAnimation(from, to, Ms(halfMs)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine };
        Timeline.SetDesiredFrameRate(a, AmbientFps);
        return a;
    }

    private static DoubleAnimation Forever(double from, double to, Duration d, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation(from, to, d) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        Timeline.SetDesiredFrameRate(a, AmbientFps);
        return a;
    }

    private static void Fade(UIElement e, double to, int ms) => Tween(e, OpacityProperty, to, ms);

    /// <summary>Eases to a value when live; otherwise drops any clock and sets it directly (static pose, no ticking).</summary>
    private static void Tween(IAnimatable target, DependencyProperty dp, object to, int ms)
    {
        var obj = (DependencyObject)target;
        if (ms <= 0)
        {
            target.BeginAnimation(dp, null);
            obj.SetValue(dp, to);
            return;
        }
        AnimationTimeline anim = to is Color c
            ? new ColorAnimation(c, Ms(ms)) { EasingFunction = Out }
            : new DoubleAnimation((double)to, Ms(ms)) { EasingFunction = Out };
        // Hand-off: keep the target value as the base so stopping later doesn't snap back.
        anim.Completed += (_, _) => { target.BeginAnimation(dp, null); obj.SetValue(dp, to); };
        target.BeginAnimation(dp, anim);
    }

    private static Duration Ms(int ms) => new(TimeSpan.FromMilliseconds(ms));

    private TimeSpan Seconds(int min, int max) => TimeSpan.FromSeconds(min + _rng.NextDouble() * (max - min));

    private static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }
}
