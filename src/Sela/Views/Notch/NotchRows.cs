using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sela.Core;

namespace Sela.Views.Notch;

public abstract class NotchRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One line in the Sholat list. Static parts come from the schedule; flags update every tick.</summary>
public sealed class PrayerRow : NotchRow
{
    private bool _isPassed, _isNext;
    private string _countdown = string.Empty;

    public PrayerRow(PrayerTime time)
    {
        Prayer = time.Prayer;
        Time = time.Time;
    }

    public Prayer Prayer { get; }
    public DateTime Time { get; }
    public string Name => NotchFormat.PrayerName(Prayer);
    public string TimeText => NotchFormat.Hm(Time);
    public bool IsSecondary => NotchFormat.IsSecondary(Prayer);

    public bool IsPassed { get => _isPassed; set => Set(ref _isPassed, value); }
    public bool IsNext { get => _isNext; set => Set(ref _isNext, value); }
    public string Countdown { get => _countdown; set => Set(ref _countdown, value); }
}

/// <summary>One event in the Agenda list.</summary>
public sealed class AgendaRow : NotchRow
{
    private bool _isNow, _isPast;

    public AgendaRow(CalendarEvent e)
    {
        Event = e;
        JoinUrl = NotchFormat.IsHttps(e.JoinUrl) ? e.JoinUrl : null;
    }

    public CalendarEvent Event { get; }
    public string Subject => string.IsNullOrWhiteSpace(Event.Subject) ? "(Tanpa judul)" : Event.Subject;
    public string StartText => NotchFormat.Hm(Event.Start);
    public string EndText => NotchFormat.Hm(Event.End);
    public string? Location => string.IsNullOrWhiteSpace(Event.Location) ? null : Event.Location.Trim();
    public bool HasLocation => Location is not null;
    public string SourceText => NotchFormat.SourceName(Event.Source);
    public string SourceGlyph => NotchFormat.SourceGlyph(Event.Source);
    public string SourceLabel => $"Dari {SourceText}";
    public bool IsGoogle => Event.Source == CalendarSource.Google;
    public string? JoinUrl { get; }
    public bool HasJoin => JoinUrl is not null;

    public bool IsNow { get => _isNow; set => Set(ref _isNow, value); }
    public bool IsPast { get => _isPast; set => Set(ref _isPast, value); }
}

/// <summary>One pill in the "ibadah hari ini" strip. Immutable: the strip is rebuilt when NotchState.HabitsDone is replaced.</summary>
public sealed class HabitRow : NotchRow
{
    public HabitRow(Habit habit, bool done)
    {
        Habit = habit;
        Done = done;
    }

    public Habit Habit { get; }
    public bool Done { get; }
    public string Name => NotchFormat.HabitName(Habit);
    public string Hint => Done ? $"{Name}: sudah" : $"Tandai {Name} sudah";
}
