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
    void RefreshNow();
    /// <summary>Stops the adzan if it is playing (e.g. the stop button on the reminder card).</summary>
    void StopAdhan();
}

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

/// <summary>Searches cities for the settings picker (wraps MyQuranClient.SearchCitiesAsync).</summary>
public delegate Task<IReadOnlyList<City>> CitySearch(string query, CancellationToken ct);
