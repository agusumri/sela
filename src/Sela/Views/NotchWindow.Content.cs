using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sela.Core;
using Sela.Localization;
using Sela.Views.Notch;

namespace Sela.Views;

// Notch content beyond prayers + agenda: the Hadits tab, the "ibadah hari ini" strip,
// calendar source chips and the Habit / Hadith reminder cards.
public partial class NotchWindow
{
    private static readonly Color GoogleColor = Color.FromRgb(0xE9, 0xC4, 0x6A);
    private static readonly Habit[] HabitOrder = [Habit.DzikirPagi, Habit.Sholawat, Habit.DzikirPetang];
    private const int HadithExcerptLength = 110;

    private readonly ObservableCollection<HabitRow> _habitRows = new();
    private Hadith? _shownHadith;

    private enum Tab { Sholat, Agenda, Hadits }

    private Tab CurrentTab => TabHadits.IsChecked == true ? Tab.Hadits : TabAgenda.IsChecked == true ? Tab.Agenda : Tab.Sholat;

    /// <summary>Opens the panel on the "Hadits" tab (tray item, hadith card "Baca").</summary>
    public void ShowHadith()
    {
        // Pick the tab first, so the panel unfolds straight to the Hadits height (no second resize).
        TabHadits.IsChecked = true;
        ExpandPanel();
        TabHadits.Focus();
    }

    // ---------------- panel height ----------------

    // Panel chrome around the page: grid margin 14 top + 22 bottom, 30 top bar (switch; the 25 DIP mark fits inside), 16 page margin.
    private const double PanelChrome = 14 + 30 + 16 + 22;
    private const double PanelContentWidth = PanelWidth - 40;
    private const int PanelResizeMs = 220;

    private double _panelHeight = PanelMaxHeight;

    /// <summary>
    /// Fits the panel to the active tab: Sholat = header + its rows, Agenda = header + cards (then scrolls),
    /// Hadits = max (long text scrolls), empty states = min. Animated one-shot while the panel is open.
    /// </summary>
    private void UpdatePanelHeight()
    {
        var h = Math.Ceiling(Math.Clamp(PanelChrome + MeasureActivePage(), PanelMinHeight, PanelMaxHeight));
        if (Math.Abs(h - _panelHeight) < 0.5 && Math.Abs(PanelLayer.Height - h) < 0.5) return;
        var growing = h > _panelHeight;
        _panelHeight = h;
        SyncShape(); // corner ramp uses the new height

        if (_mode != Mode.Panel)
        {
            PanelLayer.Height = h; // GoTo(Panel) unfolds to SizeOf(Panel)
            return;
        }
        if (!Motion)
        {
            PanelLayer.Height = h;
            SetNow(Island, HeightProperty, h);
            return;
        }
        // Growing: the sheet is ready at full height and the island reveals it.
        // Shrinking: the island tucks first, then the sheet (and its hairline) settles on the new bottom.
        if (growing) PanelLayer.Height = h;
        var a = Anim(h, PanelResizeMs, Soft);
        var gen = _generation;
        if (!growing) a.Completed += (_, _) => { if (gen == _generation && _mode == Mode.Panel) PanelLayer.Height = _panelHeight; };
        Island.BeginAnimation(HeightProperty, a);
    }

    private double MeasureActivePage() =>
        MeasurePageBody() + (StatusLine.Visibility == Visibility.Visible ? StatusLineHeight : 0);

    private double MeasurePageBody()
    {
        switch (CurrentTab)
        {
            case Tab.Hadits:
                return double.PositiveInfinity; // clamps to max
            case Tab.Agenda:
                if (AgendaContent.Visibility != Visibility.Visible) return MeasureHeight(AgendaEmpty);
                return MeasureHeight(AgendaHeader) + MeasureHeight(AgendaList);
            default:
                if (SholatContent.Visibility != Visibility.Visible) return MeasureHeight(SholatEmpty);
                var rows = MeasureHeight(PrayerList);
                if (rows < 1) rows = _prayerRows.Sum(r => r.IsSecondary ? 28.0 : 36.0); // containers not generated yet
                return MeasureHeight(SholatHeader) + rows;
        }
    }

    /// <summary>Natural height at the page width, margins included (render transforms don't count).</summary>
    private static double MeasureHeight(FrameworkElement e)
    {
        e.Measure(new Size(PanelContentWidth, double.PositiveInfinity));
        return e.DesiredSize.Height;
    }

    private void InitContent()
    {
        HabitList.ItemsSource = _habitRows;
        RebuildHabitRows();
    }

    private static Color SourceColor(CalendarSource s) => s == CalendarSource.Google ? GoogleColor : MeetingColor;

    // ---------------- ibadah strip ----------------

    /// <summary>Only habits present in the map are shown (the shell leaves disabled ones out).</summary>
    private void RebuildHabitRows()
    {
        _habitRows.Clear();
        var map = State.HabitsDone ?? new Dictionary<Habit, bool>();
        foreach (var h in HabitOrder)
            if (map.TryGetValue(h, out var done)) _habitRows.Add(new HabitRow(h, done));
        HabitStrip.Visibility = _habitRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HabitPill_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is Habit h && State.HabitsDone?.TryGetValue(h, out var done) == true && !done)
            Commands.MarkHabitDone(h); // the shell replaces HabitsDone, which rebuilds the strip
    }

    // ---------------- Hadits page ----------------

    private void RefreshHadith()
    {
        var hadith = State.Hadith;
        var has = hadith is not null;
        HaditsContent.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        HaditsEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

        if (hadith is not null && !Equals(hadith, _shownHadith))
        {
            _shownHadith = hadith;
            HadithSource.Text = $"{hadith.Collection} · No. {hadith.Number}";
            HadithTitle.Text = hadith.Title;
            HadithArabic.Text = hadith.Arabic?.Trim() ?? string.Empty;
            HadithArabic.Visibility = string.IsNullOrWhiteSpace(hadith.Arabic) ? Visibility.Collapsed : Visibility.Visible;
            HadithIndonesian.Text = hadith.Translation?.Trim() ?? string.Empty;
            _arabicExpanded = false; // a new hadith (or language) starts collapsed
            ApplyArabicClamp();
            HaditsScroll.ScrollToTop();
            System.Windows.Automation.AutomationProperties.SetName(HaditsScroll, $"{hadith.Title}. {hadith.Translation}");
        }

        if (State.HadithLoading && !has)
        {
            HaditsEmptyIcon.Text = "\uE895"; // sync
            HaditsEmptyTitle.Text = Loc.T("Notch_LoadingHadith");
            HaditsEmptyBody.Text = Loc.T("Notch_JustAMoment");
        }
        else
        {
            HaditsEmptyIcon.Text = "\uE82D"; // book
            HaditsEmptyTitle.Text = Loc.T("Notch_HadithUnavailable");
            HaditsEmptyBody.Text = Loc.T("Notch_CheckInternet");
        }
        HaditsRetry.Visibility = State.HadithLoading && !has ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------- long Arabic: clamp + toggle, scroll fade ----------------

    // 3, not 4: the body viewport is ~208 DIP above the habit strip, and 4 lines left only ~1 line of translation visible.
    private const int ArabicMaxLines = 3;
    private const double ArabicLineHeight = 36; // matches HadithArabic.LineHeight in XAML
    private bool _arabicExpanded;
    private double _arabicMeasureWidth;
    private Brush? _haditsFade;

    /// <summary>
    /// Long Arabic is clamped to 4 lines so the translation follows on screen; the toggle shows only when the
    /// text really is clamped (measured at the body's width), and expands / collapses it in place.
    /// </summary>
    private void ApplyArabicClamp()
    {
        var width = HaditsBody.ActualWidth > 0 ? HaditsBody.ActualWidth : PanelContentWidth - 14; // body margin 4 + 10
        _arabicMeasureWidth = width;
        var clampHeight = ArabicMaxLines * ArabicLineHeight;
        var clamped = false;
        if (HadithArabic.Visibility == Visibility.Visible && HadithArabic.Text.Length > 0)
        {
            HadithArabic.MaxHeight = double.PositiveInfinity;
            HadithArabic.Measure(new Size(width, double.PositiveInfinity));
            clamped = HadithArabic.DesiredSize.Height > clampHeight + 1;
        }
        HadithArabic.MaxHeight = clamped && !_arabicExpanded ? clampHeight : double.PositiveInfinity;
        ArabicToggle.Visibility = clamped ? Visibility.Visible : Visibility.Collapsed;
        ArabicToggle.Content = Loc.T(_arabicExpanded ? "Notch_HideArabic" : "Notch_ShowFullArabic");
        System.Windows.Automation.AutomationProperties.SetName(ArabicToggle, (string)ArabicToggle.Content);
    }

    private void ArabicToggle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _arabicExpanded = !_arabicExpanded;
        ApplyArabicClamp();
        // collapsing from further down: come back to the top so the translation is in view again
        if (!_arabicExpanded) HaditsScroll.ScrollToTop();
    }

    /// <summary>Width settles after the first layout pass (and on DPI changes): re-check whether the text is clamped.</summary>
    private void HaditsBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && Math.Abs(HaditsBody.ActualWidth - _arabicMeasureWidth) > 0.5) ApplyArabicClamp();
    }

    /// <summary>The bottom fade hints at more text; at the end it's dropped so the last line reads in full.</summary>
    private void HaditsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _haditsFade ??= HaditsScroll.OpacityMask;
        var atEnd = HaditsScroll.VerticalOffset >= HaditsScroll.ScrollableHeight - 1;
        var mask = atEnd ? null : _haditsFade;
        if (!ReferenceEquals(HaditsScroll.OpacityMask, mask)) HaditsScroll.OpacityMask = mask;
    }

    // ---------------- refresh + offline status ----------------

    // Status line: 16 tall, 10 above it, and the page margin below shrinks by 4 (see XAML).
    private const double StatusLineHeight = 16 + 10 - 4;

    private bool _spinning;

    /// <summary>While a manual refresh runs: refresh icon turns once a second (dimmed when OS animations are off),
    /// every refresh / retry button is disabled and the retry pills read "Memuat…".</summary>
    private void ApplyRefreshing()
    {
        var busy = State.Refreshing;
        RefreshButton.IsEnabled = !busy;
        foreach (var b in new[] { SholatRetry, AgendaRetry, HaditsRetry })
        {
            b.IsEnabled = !busy;
            b.Content = Loc.T(busy ? "Notch_Loading" : "Notch_Retry");
        }

        var spin = busy && Motion;
        RefreshIcon.Opacity = busy && !Motion ? 0.45 : 1;
        if (spin == _spinning) return;
        _spinning = spin;
        if (spin)
        {
            var a = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(a, 24);
            RefreshSpin.BeginAnimation(RotateTransform.AngleProperty, a);
        }
        else
        {
            RefreshSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            RefreshSpin.Angle = 0;
        }
    }

    /// <summary>Offline wins; on the Agenda tab a cached list says when it was last updated; otherwise hidden.
    /// A visibility change re-fits the panel (one-shot resize), so nothing below is pushed into the curve.</summary>
    private void RefreshStatusLine()
    {
        string? glyph = null, text = null;
        if (!State.IsOnline)
        {
            glyph = "\uF384"; // network offline
            text = Loc.T("Notch_OfflineSaved");
        }
        else if (CurrentTab == Tab.Agenda && State.AgendaFromCache && State.AgendaUpdatedAt is { } at)
        {
            glyph = "\uE823"; // clock
            text = NotchFormat.Updated(at, State.Now);
        }

        var show = text is not null;
        if (show)
        {
            StatusGlyph.Text = glyph;
            StatusText.Text = text;
        }
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        if (StatusLine.Visibility == v) return;
        StatusLine.Visibility = v;
        if (show && Motion && _mode == Mode.Panel) StatusLine.BeginAnimation(OpacityProperty, Anim(1, 200, Soft, from: 0));
        UpdatePanelHeight();
    }

    // ---------------- glance ----------------

    private void SetGlanceMeetingMeta(string when, CalendarSource? source)
    {
        GlanceMeetingWhen.Text = when;
        GlanceMeetingSep.Text = source is null ? string.Empty : " · ";
        GlanceMeetingSrc.Text = source is { } s ? NotchFormat.SourceName(s) : string.Empty;
        if (source is { } src) GlanceMeetingSrc.Foreground = Frozen(SourceColor(src), 0.9);
    }

    // ---------------- reminder cards ----------------

    /// <summary>Accent, glyph and auto-dismiss seconds for each reminder kind.</summary>
    private static (Color Accent, string Glyph, int Seconds) ReminderLook(Reminder r) => r.Kind switch
    {
        ReminderKind.PrayerSoon => (SholatColor, "\uE823", 12), // clock
        ReminderKind.PrayerNow => (SholatColor, "\uE708", 25),  // crescent
        ReminderKind.Habit => (SholatColor, "\uEB51", 20),      // heart
        ReminderKind.Hadith => (SholatColor, "\uE82D", 15),     // book
        _ => (r.Source is { } s ? SourceColor(s) : MeetingColor, "\uE787", 25), // calendar
    };

    private static string ReminderBodyText(Reminder r) =>
        r.Kind == ReminderKind.Hadith ? NotchFormat.Excerpt(r.Body, HadithExcerptLength) : r.Body;

    private void ApplyReminderSource(Reminder r)
    {
        if (r.Kind == ReminderKind.Meeting && r.Source is { } s)
        {
            var c = SourceColor(s);
            ReminderSource.Visibility = Visibility.Visible;
            ReminderSource.Background = Frozen(c, 0.14);
            ReminderSourceGlyph.Text = NotchFormat.SourceGlyph(s);
            ReminderSourceGlyph.Foreground = Frozen(c);
            ReminderSourceText.Text = NotchFormat.SourceName(s);
            ReminderSourceText.Foreground = Frozen(c);
        }
        else ReminderSource.Visibility = Visibility.Collapsed;
    }

    /// <summary>The right-hand slot holds exactly one of: badge, Join, Hentikan, Sudah/Nanti, Baca.</summary>
    private void ApplyReminderSlot()
    {
        var r = _reminder;
        var holds = AdhanHoldsReminder;
        var join = !holds && r?.Kind == ReminderKind.Meeting && NotchFormat.IsHttps(r.JoinUrl);
        var habit = !holds && r?.Kind == ReminderKind.Habit && r.Habit is not null;
        var read = !holds && r?.Kind == ReminderKind.Hadith;
        var badge = !holds && !join && !habit && !read;

        ReminderStop.Visibility = Show(holds);
        ReminderJoin.Visibility = Show(join);
        ReminderHabit.Visibility = Show(habit);
        ReminderRead.Visibility = Show(read);
        ReminderBadge.Visibility = Show(badge);

        static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HabitDone_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_reminder?.Habit is { } h) Commands.MarkHabitDone(h);
        DismissReminder();
    }

    private void HabitSnooze_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_reminder?.Habit is { } h) Commands.SnoozeHabit(h);
        DismissReminder();
    }

    private void HadithRead_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DismissReminder();
        ShowHadith();
    }

    // ---------------- tabs ----------------

    private FrameworkElement PageOf(Tab t) => t switch
    {
        Tab.Agenda => AgendaPage,
        Tab.Hadits => HaditsPage,
        _ => SholatPage,
    };

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SholatPage is null || AgendaPage is null || HaditsPage is null || TabHadits is null) return; // during InitializeComponent
        var tab = CurrentTab;
        var show = PageOf(tab);
        foreach (var p in new[] { SholatPage, AgendaPage, HaditsPage })
            if (!ReferenceEquals(p, show)) p.Visibility = Visibility.Collapsed;
        show.Visibility = Visibility.Visible;
        RefreshStatusLine(); // the "Diperbarui" line belongs to the Agenda tab only
        UpdatePanelHeight();

        var x = SegmentThumb.ActualWidth * (int)tab;
        if (Motion)
        {
            SegmentShift.BeginAnimation(TranslateTransform.XProperty, Anim(x, 260, new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }));
            show.BeginAnimation(OpacityProperty, Anim(1, 180, Soft, from: 0));
            StaggerPage(tab, 40);
        }
        else
        {
            SegmentShift.BeginAnimation(TranslateTransform.XProperty, null);
            SegmentShift.X = x;
        }
    }

    /// <summary>Header first, then the body (Hadits) or each row 25 ms after the previous one.</summary>
    private void StaggerPage(Tab tab, int delay)
    {
        if (tab == Tab.Hadits)
        {
            Rise(HaditsHeader, delay);
            Rise(HaditsBody, delay + 50);
            Rise(HabitStrip, delay + 100);
            return;
        }
        var agenda = tab == Tab.Agenda;
        var list = agenda ? AgendaList : PrayerList;
        Rise(agenda ? AgendaHeader : SholatHeader, delay);
        if (!agenda)
        {
            Rise(TimelineTrack, delay + 40);
            Rise(TimelineFill, delay + 40);
        }
        for (var i = 0; i < list.Items.Count; i++)
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement row)
                Rise(row, delay + 40 + i * 25);
    }
}
