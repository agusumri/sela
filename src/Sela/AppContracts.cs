using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sela.Core;

namespace Sela;

// Shared contract between the app shell (App + Services, owned by the shell lane)
// and the views (Views/NotchWindow, Views/SettingsWindow, owned by the UI lanes).
// All members are read/written on the WPF dispatcher thread.

/// <summary>Live state the shell keeps up to date; views bind to it.</summary>
public sealed class NotchState : INotifyPropertyChanged
{
    private City _city = DefaultCity.Jakarta;
    private DaySchedule? _today;
    private (Prayer Prayer, DateTime Time)? _next;
    private IReadOnlyList<CalendarEvent> _agenda = Array.Empty<CalendarEvent>();
    private bool _scheduleAvailable;
    private bool _calendarLoading;
    private DateTime _now = DateTime.Now;

    public City City { get => _city; set => Set(ref _city, value); }
    /// <summary>Today's schedule; null when the API and cache both failed.</summary>
    public DaySchedule? Today { get => _today; set => Set(ref _today, value); }
    /// <summary>Next wajib prayer for the countdown; null when unknown.</summary>
    public (Prayer Prayer, DateTime Time)? Next { get => _next; set => Set(ref _next, value); }
    /// <summary>Today's remaining and current events, sorted by start, both sources.</summary>
    public IReadOnlyList<CalendarEvent> Agenda { get => _agenda; set => Set(ref _agenda, value); }
    public bool ScheduleAvailable { get => _scheduleAvailable; set => Set(ref _scheduleAvailable, value); }
    public bool CalendarLoading { get => _calendarLoading; set => Set(ref _calendarLoading, value); }
    /// <summary>Ticks every second so countdowns can re-render.</summary>
    public DateTime Now { get => _now; set => Set(ref _now, value); }
    private bool _adhanPlaying;
    /// <summary>True while the adzan is sounding; the notch shows a stop control.</summary>
    public bool AdhanPlaying { get => _adhanPlaying; set => Set(ref _adhanPlaying, value); }

    private Hadith? _hadith;
    private bool _hadithLoading;
    private IReadOnlyDictionary<Habit, bool> _habitsDone = new Dictionary<Habit, bool>();
    /// <summary>Today's hadith for the panel's "Hadits" tab; null when not loaded yet / offline.</summary>
    public Hadith? Hadith { get => _hadith; set => Set(ref _hadith, value); }
    public bool HadithLoading { get => _hadithLoading; set => Set(ref _hadithLoading, value); }
    private IReadOnlyCollection<Prayer> _hiddenPrayers = Array.Empty<Prayer>();
    /// <summary>Times left out of the panel's schedule list (optional times switched off in Pengaturan). Replace to notify.</summary>
    public IReadOnlyCollection<Prayer> HiddenPrayers { get => _hiddenPrayers; set => Set(ref _hiddenPrayers, value); }
    /// <summary>Which enabled habits are done today (for the panel's check-in row). Replace the whole map to notify.</summary>
    public IReadOnlyDictionary<Habit, bool> HabitsDone { get => _habitsDone; set => Set(ref _habitsDone, value); }

    // ---- connectivity + freshness (offline UI and the refresh button) ----
    private bool _isOnline = true;
    private bool _refreshing;
    private DateTime? _agendaUpdatedAt;
    private bool _agendaFromCache;
    private bool _agendaFailed;
    /// <summary>Windows reports a usable network. False → the panel shows an "Offline" hint.</summary>
    public bool IsOnline { get => _isOnline; set => Set(ref _isOnline, value); }
    /// <summary>True while a manual refresh (IAppCommands.RefreshNow) is running; the refresh button spins.</summary>
    public bool Refreshing { get => _refreshing; set => Set(ref _refreshing, value); }
    /// <summary>When the shown agenda was last fetched successfully (local time); null = never.</summary>
    public DateTime? AgendaUpdatedAt { get => _agendaUpdatedAt; set => Set(ref _agendaUpdatedAt, value); }
    /// <summary>True when some meetings shown come from the on-disk cache because a calendar couldn't be reached.</summary>
    public bool AgendaFromCache { get => _agendaFromCache; set => Set(ref _agendaFromCache, value); }
    /// <summary>True when the last agenda fetch failed for every configured calendar and nothing is cached
    /// (so "no meetings today" would be misleading).</summary>
    public bool AgendaFailed { get => _agendaFailed; set => Set(ref _agendaFailed, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Actions views can ask the shell to perform.</summary>
public interface IAppCommands
{
    void OpenSettings();
    /// <summary>Opens a meeting link. The shell only launches https URLs.</summary>
    void OpenJoinUrl(string url);
    /// <summary>Reloads schedule, agenda and hadith now, bypassing retry backoffs and the calendar refresh interval.
    /// Sets NotchState.Refreshing while running; safe to call repeatedly.</summary>
    void RefreshNow();
    /// <summary>Stops the adzan if it is playing (e.g. the stop button on the reminder card).</summary>
    void StopAdhan();
    /// <summary>"Sudah": records the habit as done today and updates NotchState.HabitsDone.</summary>
    void MarkHabitDone(Habit habit);
    /// <summary>"Nanti": asks again in 30 minutes (within the habit's window).</summary>
    void SnoozeHabit(Habit habit);
    /// <summary>Persists where the user dragged the notch (ui.json).</summary>
    void SaveNotchPlacement(NotchEdge edge, double x);
    /// <summary>"Lihat rilis" on the new-version card (by reminder key): opens that release page on GitHub.</summary>
    void OpenUpdate(string reminderKey);
    /// <summary>"Lewati" on the new-version card: never offer that version again.</summary>
    void SkipUpdate(string reminderKey);
}

/// <summary>Screen edge the notch hangs from.</summary>
public enum NotchEdge { Top, Bottom }

/// <summary>Implemented by Views.NotchWindow.</summary>
public interface INotchHost
{
    /// <summary>Queue a reminder; shown one at a time.</summary>
    void ShowReminder(Reminder reminder);
    /// <summary>Show or hide the idle pill (reminders still show when hidden).</summary>
    void SetIdleVisible(bool visible);
}

/// <summary>UI-only preferences, persisted by the shell next to settings.json (ui.json).</summary>
public sealed class UiSettings
{
    public bool NotchAlwaysVisible { get; set; } = true;
    /// <summary>Edge of the primary work area the notch hangs from.</summary>
    public NotchEdge NotchEdge { get; set; } = NotchEdge.Top;
    /// <summary>Horizontal center of the notch as a fraction of the work-area width (0 = left, 0.5 = center, 1 = right).</summary>
    public double NotchX { get; set; } = 0.5;
    /// <summary>Day the "Hadits hari ini" card was last shown, so it appears once per day.</summary>
    public DateOnly? LastHadithCardDate { get; set; }
}

/// <summary>
/// Plays the adzan. Implemented by Services.AdhanPlayer (shell lane); used by App and the settings window.
/// Bundled files live next to the exe at Assets\Audio\adhan.mp3 and Assets\Audio\adhan-subuh.mp3.
/// </summary>
public interface IAdhanPlayer
{
    bool IsPlaying { get; }
    /// <summary>Raised on the UI thread when playback starts or stops.</summary>
    event Action? PlayingChanged;
    /// <summary>Plays the adzan for a prayer if <see cref="Sela.Core.SelaSettings.ShouldPlayAdhan"/> allows it.</summary>
    void PlayFor(Sela.Core.Prayer prayer, Sela.Core.SelaSettings settings);
    /// <summary>Preview from the settings window, ignoring mute/enable switches. Null file = bundled.</summary>
    void Preview(string? file, bool subuh, int volume);
    void Stop();
}

/// <summary>
/// Searches places for the settings picker (wraps LocationSearch.SearchAsync in the current UI language):
/// Indonesian Kemenag cities and worldwide places (<see cref="City.IsWorld"/>).
/// </summary>
public delegate Task<IReadOnlyList<City>> CitySearch(string query, CancellationToken ct);
