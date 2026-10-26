using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sela.Core;

namespace Sela.Views.Notch;

/// <summary>Small sky orb that lives in the idle pill: a 14 DIP sky gradient per phase. Designed for 30x30 DIP.</summary>
public partial class SelaMascot : UserControl
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(SkyPhase), typeof(SelaMascot),
        new PropertyMetadata(SkyPhase.Night, (d, _) => ((SelaMascot)d).ApplyPhase()));

    public static readonly DependencyProperty IsExcitedProperty = DependencyProperty.Register(
        nameof(IsExcited), typeof(bool), typeof(SelaMascot),
        new PropertyMetadata(false, (d, _) => ((SelaMascot)d).Refresh()));

    public static readonly DependencyProperty LookXProperty = DependencyProperty.Register(
        nameof(LookX), typeof(double), typeof(SelaMascot), new PropertyMetadata(0.0));

    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(
        nameof(IsAnimating), typeof(bool), typeof(SelaMascot),
        new PropertyMetadata(false, (d, _) => ((SelaMascot)d).Refresh()));

    /// <summary>Sky mood; each phase has its own gradient, cross-faded on change.</summary>
    public SkyPhase Phase { get => (SkyPhase)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    /// <summary>True when the next prayer is close: the orb glows warm from inside, with a slowly breathing warm halo.</summary>
    public bool IsExcited { get => (bool)GetValue(IsExcitedProperty); set => SetValue(IsExcitedProperty, value); }

    /// <summary>Kept for API compatibility; the orb no longer follows the pointer.</summary>
    public double LookX { get => (double)GetValue(LookXProperty); set => SetValue(LookXProperty, value); }

    /// <summary>Starts or stops the ambient glow breathing.</summary>
    public bool IsAnimating { get => (bool)GetValue(IsAnimatingProperty); set => SetValue(IsAnimatingProperty, value); }

    private static readonly IEasingFunction Sine = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });
    private static readonly IEasingFunction Out = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    // Ambient loops run forever; at this size 24 fps is indistinguishable from 60 and far cheaper.
    private const int AmbientFps = 24;
    private const int FadeMs = 400;

    public SelaMascot()
    {
        InitializeComponent();
        Loaded += (_, _) => { ApplyPhase(); Refresh(); };
        Unloaded += (_, _) => Stop();
        ApplyPhase();
    }

    private bool Live => IsAnimating && IsLoaded && SystemParameters.ClientAreaAnimation;

    /// <summary>Top, middle, bottom of the sky, plus the glow tint.</summary>
    private static (Color Top, Color Mid, Color Low, Color Glow) Palette(SkyPhase phase) => phase switch
    {
        SkyPhase.Dawn => (Hex(0x2B3A67), Hex(0x9DB4FF), Hex(0xF4B6C2), Hex(0x9DB4FF)),
        SkyPhase.Day => (Hex(0x4C9BFF), Hex(0x86BFFF), Hex(0xBFE3FF), Hex(0x6FB0FF)),
        SkyPhase.Dusk => (Hex(0x3A2D6B), Hex(0x926A6A), Hex(0xE9A86A), Hex(0xE9A86A)),
        _ => (Hex(0x0E1A3A), Hex(0x1C2A51), Hex(0x2B3A67), Hex(0x5A6FB0)),
    };

    private void ApplyPhase()
    {
        int ms = Live ? FadeMs : 0;
        var (top, mid, low, glow) = Palette(Phase);
        Tween(SkyTop, GradientStop.ColorProperty, top, ms);
        Tween(SkyMid, GradientStop.ColorProperty, mid, ms);
        Tween(SkyLow, GradientStop.ColorProperty, low, ms);
        Tween(GlowStop, GradientStop.ColorProperty, glow, ms);
        Tween(Star, OpacityProperty, Phase is SkyPhase.Night or SkyPhase.Dawn ? 0.9 : 0.0, ms);
    }

    /// <summary>Rebuilds ambient motion from current state, or settles into the static pose.</summary>
    private void Refresh()
    {
        Stop();
        bool excited = IsExcited;
        if (!Live)
        {
            Glow.Opacity = 0.22;
            WarmGlow.Opacity = excited ? 0.32 : 0;
            WarmCore.Opacity = excited ? 0.6 : 0;
            return;
        }

        // Barely-there breathing of the sky glow: a 12 s cycle, the orb itself never moves.
        Glow.BeginAnimation(OpacityProperty, Loop(0.18, 0.26, 6000));

        if (excited)
        {
            // Prayer soon: the orb lights up warm from inside and its halo slowly swells and settles (10 s cycle).
            WarmGlow.BeginAnimation(OpacityProperty, Loop(0.22, 0.40, 5000));
            WarmCore.BeginAnimation(OpacityProperty, Loop(0.45, 0.75, 5000));
        }
    }

    private void Stop()
    {
        Glow.BeginAnimation(OpacityProperty, null);
        WarmGlow.BeginAnimation(OpacityProperty, null);
        WarmCore.BeginAnimation(OpacityProperty, null);
        WarmGlow.Opacity = 0;
        WarmCore.Opacity = 0;
    }

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

    private static Color Hex(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Duration Ms(int ms) => new(TimeSpan.FromMilliseconds(ms));

    private static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }
}
