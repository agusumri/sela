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

    // Panel chrome around the page: grid margin 14 top + 22 bottom, 30 top bar, 16 page margin.
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

    private double MeasureActivePage()
    {
        switch (CurrentTab)
        {
            case Tab.Hadits:
                return double.PositiveInfinity; // clamps to max
            case Tab.Agenda:
                if (AgendaContent.Visibility != Visibility.Visible) return 0;
                return MeasureHeight(AgendaHeader) + MeasureHeight(AgendaList);
            default:
                if (SholatContent.Visibility != Visibility.Visible) return 0;
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
