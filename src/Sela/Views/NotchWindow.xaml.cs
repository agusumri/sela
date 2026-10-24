using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Sela.Core;
using Sela.Localization;
using Sela.Views.Notch;

namespace Sela.Views;

/// <summary>
/// The notch: one black shape flush to the top edge that morphs between idle pill, hover glance,
/// click panel and reminder card. Only the island's Width/Height (and the derived corner radius) animate;
/// every layer inside has a fixed size and simply cross-fades.
/// </summary>
public partial class NotchWindow : Window, INotchHost
{
    private enum Mode { Hidden, Idle, Glance, Panel, Reminder }

    private static readonly Size IdleSize = new(236, 30); // room for "Maghrib · 1j 05m 09d" without reflow
    private static readonly Size GlanceSize = new(380, 96);
    private static readonly Size GlanceAdhanSize = new(380, 132); // + "Hentikan adzan" row
    // The panel is 344 wide; its height fits the active tab (see NotchWindow.Content.cs, UpdatePanelHeight).
    private const double PanelWidth = 344;
    private const double PanelMinHeight = 260;
    private const double PanelMaxHeight = 412; // fits 720 DIP screens with room to spare
    private static readonly Size ReminderSize = new(380, 92);
    private const double StageSize = 420;
    private const double MaxCorner = 26;
    private const double PanelCorner = 30;

    // Debug: --panel opens the full panel right after load (for screenshots).
    private static readonly bool OpenPanelOnLoad =
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--panel", StringComparison.OrdinalIgnoreCase));
    private static readonly bool OpenAgendaOnLoad =
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--panel=agenda", StringComparison.OrdinalIgnoreCase));
    private static readonly bool OpenHaditsOnLoad =
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--panel=hadits", StringComparison.OrdinalIgnoreCase));

    private static readonly Color SholatColor = Color.FromRgb(0x3D, 0xDC, 0x97);
    private static readonly Color MeetingColor = Color.FromRgb(0x4C, 0x9B, 0xFF);

    private readonly Queue<Reminder> _queue = new();
    private readonly ObservableCollection<PrayerRow> _prayerRows = new();
    private readonly ObservableCollection<AgendaRow> _agendaRows = new();
    private readonly DispatcherTimer _hoverIntent = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _leaveGrace = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _queueGap = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _lookTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private Mode _mode = Mode.Hidden;
    private bool _idleVisible = true;

    private bool _activatable;
    private int _generation;
    private IntPtr _hwnd;
    private Reminder? _reminder;
    private AnimationClock? _reminderClock;
    private string? _glanceJoinUrl;
    private bool _ambientAnimations = true;

    // Debug overrides for screenshots: --sky=dawn|day|dusk|night, --excited
    private static readonly SkyPhase? ForcedSky = ParseForcedSky();
    private static readonly bool SkyTour =
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--sky-tour", StringComparison.OrdinalIgnoreCase));
    private static readonly (SkyPhase Phase, bool Excited, string LabelKey)[] Tour =
    [
        (SkyPhase.Dawn, false, "Notch_TourDawn"),
        (SkyPhase.Day, false, "Notch_TourDay"),
        (SkyPhase.Dusk, false, "Notch_TourDusk"),
        (SkyPhase.Night, false, "Notch_TourNight"),
        (SkyPhase.Night, true, "Notch_TourExcited"),
    ];
    private readonly DispatcherTimer _tourTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private int _tourStep;
    private static readonly bool ForcedExcited =
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--excited", StringComparison.OrdinalIgnoreCase));


    public NotchWindow(NotchState state, IAppCommands commands)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        InitializeComponent();

        PrayerList.ItemsSource = _prayerRows;
        AgendaList.ItemsSource = _agendaRows;

        _hoverIntent.Tick += (_, _) => { _hoverIntent.Stop(); if (_mode == Mode.Idle) GoTo(Mode.Glance); };
        _leaveGrace.Tick += (_, _) => { _leaveGrace.Stop(); if (_mode is Mode.Glance or Mode.Panel) Rest(); };
        _queueGap.Tick += (_, _) => { _queueGap.Stop(); TryShowNext(); };
        _lookTimer.Tick += (_, _) => LookTowardCursor();
        if (SkyTour)
        {
            _tourTimer.Tick += (_, _) => { _tourStep = (_tourStep + 1) % Tour.Length; Refresh(); };
            _tourTimer.Start();
        }

        Island.SizeChanged += (_, _) => SyncShape();
        InitPlacement();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += (_, _) => { if (_mode == Mode.Panel) Rest(); };
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        IsVisibleChanged += (_, _) => UpdateMascotAnimating();


        State.PropertyChanged += OnStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        RebuildPrayerRows();
        RebuildAgendaRows();
        InitContent();
        Refresh();
    }

    public NotchState State { get; }
    public IAppCommands Commands { get; }

    private static bool Motion => SystemParameters.ClientAreaAnimation;

    // ---------------- INotchHost + public surface ----------------

    public void ShowReminder(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        _queue.Enqueue(reminder);
        TryShowNext();
    }

    public void SetIdleVisible(bool visible)
    {
        if (_idleVisible == visible) return;
        _idleVisible = visible;
        if (_mode is Mode.Idle or Mode.Hidden) Rest();
        UpdateMascotAnimating();
    }

    /// <summary>User preference for the mascot's ambient motion; the OS animation setting still applies.</summary>
    public bool AmbientAnimationsEnabled
    {
        get => _ambientAnimations;
        set
        {
            _ambientAnimations = value;
            UpdateMascotAnimating();
        }
    }

    private void UpdateMascotAnimating()
    {
        bool live = _idleVisible && _mode == Mode.Idle && IsVisible && _ambientAnimations && Motion;
        Mascot.IsAnimating = live;
        if (live) _lookTimer.Start();
        else { _lookTimer.Stop(); Mascot.LookX = 0; }
    }

    /// <summary>
    /// The mascot follows the pointer whenever it moves near the top of the screen, not only on hover
    /// (hovering the pill expands it after 120 ms, which would hide the glance). Polled at 10 Hz while idle.
    /// </summary>
    private void LookTowardCursor()
    {
        if (!NativeMethods.GetCursorPos(out var c) || PresentationSource.FromVisual(Mascot) is null) return;
        var p = Mascot.PointFromScreen(new Point(c.X, c.Y)); // DIPs relative to the mascot
        double dx = p.X - Mascot.ActualWidth / 2, dy = p.Y;
        double target = dy < 260 && Math.Abs(dx) < 700 ? Math.Clamp(dx / 220, -1, 1) : 0;
        if (Math.Abs(target - Mascot.LookX) > 0.08) Mascot.LookX = Math.Round(target, 2);
    }

    private static SkyPhase? ParseForcedSky()
    {
        const string prefix = "--sky=";
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return arg is not null && Enum.TryParse<SkyPhase>(arg[prefix.Length..], ignoreCase: true, out var p)
            && Enum.IsDefined(p) ? p : null;
    }

    /// <summary>Opens the full panel and takes focus (for tray clicks and other explicit user actions).</summary>
    public void ExpandPanel()
    {
        if (_mode == Mode.Reminder) DismissReminder();
        EnsureShown();
        OpenPanel();
    }

    // ---------------- window plumbing ----------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        SetActivatable(false);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Recenter();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        // Clicks on the idle pill, glance or reminder must never steal focus from the user's work.
        if (msg == WM_MOUSEACTIVATE && !_activatable)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void SetActivatable(bool on)
    {
        _activatable = on;
        if (_hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetExStyle(_hwnd) | NativeMethods.WS_EX_TOOLWINDOW;
        style = on ? style & ~NativeMethods.WS_EX_NOACTIVATE : style | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetExStyle(_hwnd, style);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {

        Recenter();
        if (_mode == Mode.Hidden) Rest();
        if (OpenPanelOnLoad || OpenAgendaOnLoad || OpenHaditsOnLoad)
            Dispatcher.BeginInvoke(() =>
            {
                if (OpenHaditsOnLoad) { ShowHadith(); return; }
                ExpandPanel();
                if (OpenAgendaOnLoad) TabAgenda.IsChecked = true;
            }, DispatcherPriority.ApplicationIdle);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        State.PropertyChanged -= OnStateChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _hoverIntent.Stop();
        _leaveGrace.Stop();
        _queueGap.Stop();
        _reminderClock?.Controller?.Stop();
    }

    /// <summary>XAML bindings refresh themselves; everything built in code is rebuilt here.</summary>
    private void OnLanguageChanged()
    {
        RebuildPrayerRows();
        RebuildAgendaRows();
        RebuildHabitRows();
        _shownHadith = null; // re-render the hadith page
        Refresh();
        UpdatePanelHeight();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(Recenter, DispatcherPriority.Background);

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.BeginInvoke(Recenter, DispatcherPriority.Background);
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Dispatcher.BeginInvoke(() => { UpdateMascotAnimating(); UpdateAdhanUi(); }, DispatcherPriority.Background);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(Recenter, DispatcherPriority.Background);
    }

    // Window position is owned by NotchWindow.Placement.cs (edge + horizontal position, dragging).
    private void Recenter() => PositionWindow();

    private void EnsureShown()
    {
        if (IsVisible) return;
        Show();
        Recenter();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _mode == Mode.Panel)
        {
            Rest();
            e.Handled = true;
        }
    }

    // ---------------- pointer ----------------

    private void Shape_MouseEnter(object sender, MouseEventArgs e)
    {
        _leaveGrace.Stop();
        if (_mode == Mode.Reminder) _reminderClock?.Controller?.Pause();
        else if (_mode == Mode.Idle) _hoverIntent.Start();
    }

    private void Shape_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverIntent.Stop();
        if (_mode == Mode.Reminder) { if (!AdhanHoldsReminder) _reminderClock?.Controller?.Resume(); }
        else if (_mode is Mode.Glance or Mode.Panel) _leaveGrace.Start();
    }


    private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ConsumeDragClick()) return; // the press was a drag, not a click
        switch (_mode)
        {
            case Mode.Reminder: DismissReminder(); break;
            case Mode.Idle or Mode.Glance: OpenPanel(); break;
        }
    }

    private void OpenPanel()
    {
        _hoverIntent.Stop();
        _leaveGrace.Stop();
        Refresh();
        UpdatePanelHeight(); // not in Panel mode yet: only records the height GoTo unfolds to
        GoTo(Mode.Panel);
        // Only an explicit click lets the notch take focus, so keyboard and click-outside work.
        SetActivatable(true);
        Activate();
        TabSholat.Focus();
    }

    /// <summary>Back to the resting state: idle pill, or nothing when the pill is turned off.</summary>
    private void Rest()
    {
        var wasPanel = _mode == Mode.Panel;
        if (_activatable) SetActivatable(false);
        GoTo(_idleVisible ? Mode.Idle : Mode.Hidden);
        if (wasPanel && _queue.Count > 0) _queueGap.Start();
    }

    // ---------------- buttons ----------------

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Rest();
        Commands.OpenSettings();
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => Commands.RefreshNow();

    /// <summary>Stops the sound only; the card or glance stays where it is.</summary>
    private void StopAdhan_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Commands.StopAdhan();
    }

    private void AgendaJoin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string url) Join(url);
        Rest();
    }

    private void GlanceJoin_Click(object sender, RoutedEventArgs e)
    {
        if (_glanceJoinUrl is { } url) Join(url);
        Rest();
    }

    private void ReminderJoin_Click(object sender, RoutedEventArgs e)
    {
        if (_reminder?.JoinUrl is { } url) Join(url);
        DismissReminder();
    }

    private void Join(string url)
    {
        if (NotchFormat.IsHttps(url)) Commands.OpenJoinUrl(url);
    }


    // ---------------- reminders ----------------

    private void TryShowNext()
    {
        if (_reminder is not null || _queueGap.IsEnabled || _mode == Mode.Panel || _queue.Count == 0) return;
        ShowReminderNow(_queue.Dequeue());
    }

    private void ShowReminderNow(Reminder r)
    {
        _reminder = r;
        _hoverIntent.Stop();
        _leaveGrace.Stop();

        var (accent, glyph, seconds) = ReminderLook(r);
        var accentBrush = Frozen(accent);
        ReminderIconBg.Fill = Frozen(accent, 0.16);
        ReminderIcon.Foreground = accentBrush;
        ReminderIcon.Text = glyph;
        ReminderTitle.Text = r.Title;
        ReminderBody.Text = ReminderBodyText(r);
        ReminderProgress.Fill = Frozen(accent, 0.75);
        ApplyReminderSource(r);

        ReminderBadge.Text = r.Badge ?? string.Empty;
        ReminderBadge.Foreground = accentBrush;
        ApplyReminderSlot();
        AutomationProperties_SetName(r);

        var bar = Motion ? Visibility.Visible : Visibility.Collapsed;
        ReminderTrack.Visibility = bar;
        ReminderProgress.Visibility = bar;

        Glow(r.Kind == ReminderKind.PrayerNow ? accent : (Color?)null);

        // The bar doubles as the auto-dismiss timer, so pausing it on hover pauses dismissal too.
        _reminderClock?.Controller?.Stop();
        var countdown = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(seconds));
        var clock = countdown.CreateClock();
        clock.Completed += (_, _) => { if (ReferenceEquals(_reminderClock, clock)) DismissReminder(); };
        _reminderClock = clock;
        ReminderProgressScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, clock);

        GoTo(Mode.Reminder);
        UpdateAdhanUi();
        if (IsMouseOver || AdhanHoldsReminder) clock.Controller?.Pause();
        try { SystemSounds.Asterisk.Play(); } catch { /* sound is best effort */ }
    }

    private void AutomationProperties_SetName(Reminder r) =>
        System.Windows.Automation.AutomationProperties.SetName(ReminderLayer, $"{r.Title}. {r.Body}");

    private void DismissReminder()
    {
        if (_reminder is null) return;
        _reminder = null;
        var clock = _reminderClock;
        _reminderClock = null;
        clock?.Controller?.Pause();
        Glow(null);
        Rest();
        UpdateAdhanUi();
        _queueGap.Start();
    }

    // ---------------- adzan ----------------

    /// <summary>The "sholat now" card stays up (countdown paused) for as long as the adzan sounds.</summary>
    private bool AdhanHoldsReminder => State.AdhanPlaying && _reminder?.Kind == ReminderKind.PrayerNow;

    private bool _waveRunning;

    /// <summary>Shows or hides every stop control and the sound bars for the current adzan state.</summary>
    private void UpdateAdhanUi()
    {
        var playing = State.AdhanPlaying;
        var holds = AdhanHoldsReminder;

        ApplyReminderSlot();
        AdhanWave.Visibility = holds ? Visibility.Visible : Visibility.Collapsed;
        ReminderIcon.Visibility = holds ? Visibility.Collapsed : Visibility.Visible;
        SetWave(holds && _mode == Mode.Reminder && Motion);

        GlanceStop.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        IdleAdhan.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Three bars breathing on a slow 1.2 s cycle, offset so they read as a soft wave. Off = no clocks at all.</summary>
    private void SetWave(bool on)
    {
        if (on == _waveRunning) return;
        _waveRunning = on;
        var bars = new[] { (Wave1, 0.45, 0), (Wave2, 0.85, 400), (Wave3, 0.6, 800) };
        foreach (var (bar, rest, offset) in bars)
        {
            if (!on)
            {
                bar.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                bar.ScaleY = rest;
                continue;
            }
            var a = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(600))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(-offset), // start mid-cycle, no staggered pop-in
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(a, 24);
            bar.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    private void OnAdhanPlayingChanged()
    {
        UpdateAdhanUi();
        if (_mode == Mode.Reminder && _reminder?.Kind == ReminderKind.PrayerNow)
        {
            if (AdhanHoldsReminder) _reminderClock?.Controller?.Pause();
            else if (!IsMouseOver) _reminderClock?.Controller?.Resume(); // adzan ended: normal countdown again
        }
        if (_mode == Mode.Glance) GoTo(Mode.Glance); // grow/shrink for the stop row
    }

    private void Glow(Color? color)
    {
        ShapeShadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        if (color is { } c)
        {
            ShapeShadow.Color = c;
            ShapeShadow.BlurRadius = 34;
            ShapeShadow.ShadowDepth = 2;
            if (Motion)
            {
                // a slow breath, not a flash
                var glow = new DoubleAnimation(0.22, 0.5, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(glow, 24);
                ShapeShadow.BeginAnimation(DropShadowEffect.OpacityProperty, glow);
            }
            else ShapeShadow.Opacity = 0.4;
        }
        else
        {
            ShapeShadow.Color = Colors.Black;
            ShapeShadow.BlurRadius = 28;
            ShapeShadow.ShadowDepth = 6;
            ShapeShadow.Opacity = 0.5;
        }
    }

    // ---------------- state machine + motion ----------------

    private void GoTo(Mode target)
    {
        var from = _mode;
        _mode = target;
        var gen = ++_generation;
        UpdateMascotAnimating();
        if (target != Mode.Idle) Mascot.LookX = 0;


        if (target == Mode.Hidden)
        {
            HideAll(gen, from);
            return;
        }

        EnsureShown();
        Refresh();

        var size = SizeOf(target);
        var layer = LayerOf(target);
        var growing = size.Width * size.Height > Island.ActualWidth * Island.ActualHeight + 1;

        foreach (var l in new[] { IdleLayer, GlanceLayer, PanelLayer, ReminderLayer })
            if (!ReferenceEquals(l, layer)) FadeOut(l, gen);

        layer.Visibility = Visibility.Visible;
        var shift = (TranslateTransform)layer.RenderTransform;

        if (!Motion)
        {
            SetNow(Shape, OpacityProperty, 1);
            SetNow(Island, WidthProperty, size.Width);
            SetNow(Island, HeightProperty, size.Height);
            SetNow(layer, OpacityProperty, 1);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
            return;
        }

        if (from == Mode.Hidden)
        {
            // grow out of the top edge from pill size
            SetNow(Island, WidthProperty, IdleSize.Width);
            SetNow(Island, HeightProperty, IdleSize.Height);
            Shape.BeginAnimation(OpacityProperty, Anim(1, 140));
        }
        else Shape.BeginAnimation(OpacityProperty, Anim(1, 100));

        if (growing)
        {
            IEasingFunction w, h;
            int wMs, hMs;
            if (target == Mode.Reminder)
            {
                w = new ElasticEase { Oscillations = 1, Springiness = 8, EasingMode = EasingMode.EaseOut };
                h = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
                wMs = 640; hMs = 520;
            }
            else if (target == Mode.Panel)
            {
                // a sheet unfolding: long, decelerating, a breath of overshoot on width only
                w = new BackEase { Amplitude = 0.08, EasingMode = EasingMode.EaseOut };
                h = PanelUnfold;
                wMs = 460; hMs = 520;
            }
            else
            {
                w = new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut };
                h = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };
                wMs = 440; hMs = 400;
            }
            var delay = from == Mode.Hidden ? 80 : 0;
            Island.BeginAnimation(WidthProperty, Anim(size.Width, wMs, w, delay));
            Island.BeginAnimation(HeightProperty, Anim(size.Height, hMs, h, delay));
            layer.BeginAnimation(OpacityProperty, Anim(1, 240, Soft, delay + 150));
            if (target == Mode.Panel)
            {
                // the layer itself stays put; its rows rise in one after another
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.Y = 0;
                Rise((FrameworkElement)PanelTop, delay + 120);
                StaggerPage(CurrentTab, delay + 170);
            }
            else shift.BeginAnimation(TranslateTransform.YProperty, Anim(0, 360, Soft, delay + 130, from: -6));
        }
        else
        {
            var tuck = new CubicEase { EasingMode = EasingMode.EaseInOut };
            Island.BeginAnimation(WidthProperty, Anim(size.Width, 320, tuck, 40));
            Island.BeginAnimation(HeightProperty, Anim(size.Height, 300, tuck, 40));
            layer.BeginAnimation(OpacityProperty, Anim(1, 180, Soft, 200));
            shift.BeginAnimation(TranslateTransform.YProperty, Anim(0, 220, Soft, 180, from: -3));
        }
    }

    private void HideAll(int gen, Mode from)
    {
        if (!IsVisible || from == Mode.Hidden || !Motion)
        {
            foreach (var l in new[] { IdleLayer, GlanceLayer, PanelLayer, ReminderLayer }) FadeOut(l, gen, instant: true);
            SetNow(Shape, OpacityProperty, 0);
            SetNow(Island, WidthProperty, IdleSize.Width);
            SetNow(Island, HeightProperty, IdleSize.Height);
            if (IsVisible) Hide();
            return;
        }

        foreach (var l in new[] { IdleLayer, GlanceLayer, PanelLayer, ReminderLayer }) FadeOut(l, gen);
        var tuck = new CubicEase { EasingMode = EasingMode.EaseIn };
        Island.BeginAnimation(WidthProperty, Anim(IdleSize.Width, 300, tuck, 60));
        Island.BeginAnimation(HeightProperty, Anim(IdleSize.Height, 280, tuck, 60));
        var fade = Anim(0, 160, null, 300);
        fade.Completed += (_, _) => { if (gen == _generation && IsVisible) Hide(); };
        Shape.BeginAnimation(OpacityProperty, fade);
    }

    private void FadeOut(FrameworkElement layer, int gen, bool instant = false)
    {
        if (layer.Visibility != Visibility.Visible) return;
        if (instant || !Motion)
        {
            SetNow(layer, OpacityProperty, 0);
            layer.Visibility = Visibility.Collapsed;
            return;
        }
        var a = Anim(0, 110);
        a.Completed += (_, _) => { if (gen == _generation && !ReferenceEquals(layer, LayerOf(_mode))) layer.Visibility = Visibility.Collapsed; };
        layer.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>Bottom corners track the height, so the pill morphs smoothly into the card; the stage stays centered.</summary>
    private void SyncShape()
    {
        // Above glance height the cap eases from 26 up to the panel's softer 30, so only the panel sheet changes.
        var h = Island.ActualHeight;
        // The ramp ends at the current panel height, so a short panel still reaches the sheet's 30 corner.
        var t = Math.Clamp((h - GlanceSize.Height) / (_panelHeight - GlanceSize.Height), 0, 1);
        var r = Math.Min(h / 2, MaxCorner + (PanelCorner - MaxCorner) * t);
        SetIslandCorners(r); // placement decides which edge gets the rounded corners
        Canvas.SetLeft(Stage, Math.Round((Island.ActualWidth - StageSize) / 2));
    }

    private Size SizeOf(Mode m) => m switch
    {
        Mode.Glance => State.AdhanPlaying ? GlanceAdhanSize : GlanceSize,
        Mode.Panel => new Size(PanelWidth, _panelHeight),
        Mode.Reminder => ReminderSize,
        _ => IdleSize,
    };

    private FrameworkElement LayerOf(Mode m) => m switch
    {
        Mode.Glance => GlanceLayer,
        Mode.Panel => PanelLayer,
        Mode.Reminder => ReminderLayer,
        _ => IdleLayer,
    };

    private static readonly IEasingFunction Soft = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction PanelUnfold = Freeze(new QuarticEase { EasingMode = EasingMode.EaseOut });

    /// <summary>One-shot: fade in and rise 6 px. Without OS animations everything is simply shown.</summary>
    private static void Rise(FrameworkElement e, int delay)
    {
        if (e.RenderTransform is not TranslateTransform shift)
            e.RenderTransform = shift = new TranslateTransform();
        if (!Motion)
        {
            SetNow(e, OpacityProperty, 1);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
            return;
        }
        e.BeginAnimation(OpacityProperty, Anim(1, 200, Soft, delay, from: 0));
        shift.BeginAnimation(TranslateTransform.YProperty, Anim(0, 220, Soft, delay, from: 6));
    }

    private void PrayerList_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimeline();

    /// <summary>
    /// Lays the timeline through the dot centers (from layout heights, so the rise animation doesn't skew it)
    /// and fills it up to the next prayer.
    /// </summary>
    private void UpdateTimeline()
    {
        var centers = new List<double>(_prayerRows.Count);
        double y = 0;
        for (var i = 0; i < PrayerList.Items.Count; i++)
        {
            if (PrayerList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) return;
            centers.Add(y + row.ActualHeight / 2);
            y += row.ActualHeight;
        }
        if (centers.Count < 2)
        {
            TimelineTrack.Height = TimelineFill.Height = 0;
            return;
        }
        var top = centers[0];
        Canvas.SetTop(TimelineTrack, top);
        Canvas.SetTop(TimelineFill, top);
        TimelineTrack.Height = centers[^1] - top;

        var next = -1;
        for (var i = 0; i < _prayerRows.Count; i++) if (_prayerRows[i].IsNext) { next = i; break; }
        var allPassed = _prayerRows.All(r => r.IsPassed);
        var end = next >= 0 ? centers[next] : allPassed ? centers[^1] : top;
        TimelineFill.Height = Math.Max(0, end - top);
    }

    private static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }

    private static DoubleAnimation Anim(double to, int ms, IEasingFunction? ease = null, int delay = 0, double? from = null)
    {
        var a = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = ease,
        };
        if (from is { } f) a.From = f;
        return a;
    }

    private static void SetNow(UIElement e, DependencyProperty p, double value)
    {
        e.BeginAnimation(p, null);
        e.SetValue(p, value);
    }

    private static SolidColorBrush Frozen(Color c, double opacity = 1)
    {
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    // ---------------- content ----------------

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotchState.Today) or nameof(NotchState.HiddenPrayers)) RebuildPrayerRows();
        if (e.PropertyName == nameof(NotchState.Agenda)) RebuildAgendaRows();
        if (e.PropertyName == nameof(NotchState.AdhanPlaying)) OnAdhanPlayingChanged();
        if (e.PropertyName == nameof(NotchState.HabitsDone)) RebuildHabitRows();
        Refresh();
        // Not on the 1 s Now tick: only content that changes the row/card count re-fits the panel.
        if (e.PropertyName is nameof(NotchState.Today) or nameof(NotchState.HiddenPrayers) or nameof(NotchState.Agenda)
            or nameof(NotchState.CalendarLoading) or nameof(NotchState.HabitsDone))
            UpdatePanelHeight();
    }

    private void RebuildPrayerRows()
    {
        _prayerRows.Clear();
        var hidden = State.HiddenPrayers ?? Array.Empty<Prayer>();
        if (State.Today?.Times is { } times)
            foreach (var t in times.Where(t => !hidden.Contains(t.Prayer)).OrderBy(t => t.Time)) _prayerRows.Add(new PrayerRow(t));
    }

    private void RebuildAgendaRows()
    {
        _agendaRows.Clear();
        foreach (var e in (State.Agenda ?? Array.Empty<CalendarEvent>()).OrderBy(e => e.Start)) _agendaRows.Add(new AgendaRow(e));
    }

    /// <summary>Re-renders text for the current tick. Cheap: plain property sets, no layout of fixed-size layers changes.</summary>
    private void Refresh()
    {
        var now = State.Now;
        var next = State.Next;

        // idle pill
        if (next is { } n)
        {
            IdleName.Text = NotchFormat.PrayerName(n.Prayer);
            IdleSep.Text = "  ·  ";
            IdleCountdown.Text = NotchFormat.Countdown(n.Time - now);
        }
        else
        {
            IdleName.Text = "Sela";
            IdleSep.Text = string.Empty;
            IdleCountdown.Text = string.Empty;
        }
        Mascot.Phase = ForcedSky ?? SkyPhases.For(now, State.Today);
        var left = next is { } soon ? soon.Time - now : TimeSpan.MaxValue;
        var excited = ForcedExcited || (left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(10));
        Mascot.IsExcited = excited; // only the mascot reacts; the text stays still

        if (SkyTour)
        {
            // Demo: --sky-tour cycles every look so all of them can be seen in one run.
            var step = Tour[_tourStep];
            Mascot.Phase = step.Phase;
            Mascot.IsExcited = step.Excited;
            IdleName.Text = Loc.T(step.LabelKey);
            IdleSep.Text = "  ·  ";
            IdleCountdown.Text = $"{_tourStep + 1}/{Tour.Length}";
        }


        // glance
        if (next is { } g)
        {
            GlanceLabel.Text = Loc.T(g.Time.Date > now.Date ? "Notch_NextPrayerTomorrow" : "Notch_NextPrayer");
            GlancePrayer.Text = NotchFormat.PrayerName(g.Prayer);
            GlancePrayerTime.Text = NotchFormat.Hm(g.Time);
            GlanceCountdown.Text = NotchFormat.Countdown(g.Time - now);
        }
        else
        {
            GlanceLabel.Text = "Sela";
            GlancePrayer.Text = Loc.T(State.Today is null ? "Notch_ScheduleUnavailable" : "Notch_PrayerTimes");
            GlancePrayerTime.Text = string.Empty;
            GlanceCountdown.Text = string.Empty;
        }

        var meeting = State.Agenda?
            .Where(e => e.End > now && e.Start.Date == now.Date)
            .OrderBy(e => e.Start)
            .FirstOrDefault();
        if (meeting is not null)
        {
            GlanceMeeting.Text = string.IsNullOrWhiteSpace(meeting.Subject) ? Loc.T("Notch_NoTitle") : meeting.Subject;
            GlanceMeeting.Foreground = (Brush)FindResource("TextPrimary");
            var when = meeting.Start <= now ? Loc.T("Notch_InProgress") : NotchFormat.Hm(meeting.Start);
            SetGlanceMeetingMeta(when, meeting.Source);
            _glanceJoinUrl = NotchFormat.IsHttps(meeting.JoinUrl) ? meeting.JoinUrl : null;
        }
        else
        {
            GlanceMeeting.Text = Loc.T(State.CalendarLoading ? "Notch_LoadingAgenda" : "Notch_NoMoreMeetings");
            GlanceMeeting.Foreground = (Brush)FindResource("TextSecondary");
            SetGlanceMeetingMeta(string.Empty, null);
            _glanceJoinUrl = null;
        }
        GlanceJoin.Visibility = _glanceJoinUrl is null ? Visibility.Collapsed : Visibility.Visible;

        // panel: sholat
        var hasSchedule = State.Today is not null && _prayerRows.Count > 0;
        SholatContent.Visibility = hasSchedule ? Visibility.Visible : Visibility.Collapsed;
        SholatEmpty.Visibility = hasSchedule ? Visibility.Collapsed : Visibility.Visible;
        CityText.Text = (State.Today?.City ?? State.City).DisplayName;
        GregorianText.Text = NotchFormat.Gregorian(now);
        HijriText.Text = NotchFormat.HijriDate(now);
        foreach (var row in _prayerRows)
        {
            var isNext = next is { } x && x.Prayer == row.Prayer && x.Time == row.Time;
            row.IsNext = isNext;
            row.IsPassed = !isNext && row.Time <= now;
            row.Countdown = isNext ? NotchFormat.Countdown(row.Time - now) : string.Empty;
        }
        UpdateTimeline();

        // panel: agenda
        AgendaDate.Text = NotchFormat.Gregorian(now);
        foreach (var row in _agendaRows)
        {
            row.IsNow = row.Event.Start <= now && now < row.Event.End;
            row.IsPast = row.Event.End <= now;
        }
        var hasAgenda = _agendaRows.Count > 0;
        AgendaContent.Visibility = hasAgenda ? Visibility.Visible : Visibility.Collapsed;
        AgendaEmpty.Visibility = hasAgenda ? Visibility.Collapsed : Visibility.Visible;
        AgendaCount.Text = hasAgenda ? Loc.F(_agendaRows.Count == 1 ? "Notch_AgendaCountOne" : "Notch_AgendaCount", _agendaRows.Count) : string.Empty;
        if (State.CalendarLoading)
        {
            AgendaEmptyIcon.Text = "\uE895"; // sync
            AgendaEmptyTitle.Text = Loc.T("Notch_LoadingAgenda");
            AgendaEmptyBody.Text = Loc.T("Notch_JustAMoment");
        }
        else
        {
            AgendaEmptyIcon.Text = "\uE787";
            AgendaEmptyTitle.Text = Loc.T("Notch_NoAgenda");
            AgendaEmptyBody.Text = Loc.T("Notch_TimeToFocus");
        }

        // panel: hadits
        RefreshHadith();
    }
}
