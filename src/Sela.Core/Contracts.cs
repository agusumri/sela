namespace Sela.Core;

// Shared contracts between Sela.Core (logic, platform-neutral) and Sela (WPF UI + Windows-only providers).

public enum Prayer { Imsak, Subuh, Terbit, Dhuha, Dzuhur, Ashar, Maghrib, Isya }

/// <summary>A city/regency from the myquran.com (Kemenag) API. Name is as returned, e.g. "KOTA JAKARTA".</summary>
public sealed record City(string Id, string Name, string? Province = null)
{
    /// <summary>Title-cased name for display, e.g. "Kota Jakarta", "Kab. Lampung Tengah".</summary>
    public string DisplayName => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Name.ToLowerInvariant());
}

public static class DefaultCity
{
    public static City Jakarta { get; } = new("1301", "KOTA JAKARTA", "DKI JAKARTA");
}

public sealed record PrayerTime(Prayer Prayer, DateTime Time);

public sealed record DaySchedule(DateOnly Date, City City, IReadOnlyList<PrayerTime> Times)
{
    public DateTime this[Prayer p] => Times.First(t => t.Prayer == p).Time;
}

public enum CalendarSource { Outlook, Google }

public sealed record CalendarEvent(
    string Key,              // stable id + occurrence start, used for de-duplication
    string Subject,
    DateTime Start,          // local time
    DateTime End,
    string? Location,
    string? JoinUrl,         // https Teams/Zoom/Meet link if found
    int ReminderMinutes,     // minutes before Start to remind
    CalendarSource Source);

public interface ICalendarProvider
{
    CalendarSource Source { get; }
    /// <summary>Return non-all-day, non-declined, non-cancelled events overlapping [from, to). Never throws for "unavailable"; return empty.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct);
}

public enum ReminderKind { PrayerSoon, PrayerNow, Meeting, Habit, Hadith }

/// <summary>Daily ibadah check-ins Sela asks about.</summary>
public enum Habit { DzikirPagi, DzikirPetang, Sholawat }

public sealed record Reminder(
    string Key,
    ReminderKind Kind,
    string Title,
    string Body,
    string? Badge,           // short right-aligned text, e.g. "11:39"
    string? JoinUrl,
    DateTime DueAt,
    Prayer? Prayer = null,   // set for prayer reminders (used to pick the adzan)
    Habit? Habit = null,     // set for Habit reminders: the card offers "Sudah" / "Nanti"
    CalendarSource? Source = null); // set for Meeting reminders: shown as an Outlook / Google badge

/// <summary>One hadith for the daily card. Text comes from api.myquran.com (Arbain Nawawi).</summary>
public sealed record Hadith(
    string Collection,       // e.g. "Arbain Nawawi"
    int Number,
    string Title,            // e.g. "Amalan Bergantung pada Niat"
    string Arabic,
    string Indonesian,
    DateOnly Date);          // the day this hadith was chosen for

public sealed class SelaSettings
{
    public string CityId { get; set; } = "1301";          // myquran.com city id
    public string CityName { get; set; } = "KOTA JAKARTA";
    public int PrayerSoonMinutes { get; set; } = 10;      // 0 disables early heads-up
    public Dictionary<Prayer, bool> PrayerEnabled { get; set; } = new()
    {
        [Prayer.Subuh] = true, [Prayer.Dzuhur] = true, [Prayer.Ashar] = true,
        [Prayer.Maghrib] = true, [Prayer.Isya] = true,
        [Prayer.Imsak] = false, [Prayer.Terbit] = false, [Prayer.Dhuha] = false,
    };
    public bool OutlookEnabled { get; set; } = true;
    public string? GoogleIcalUrl { get; set; }            // secret iCal address; stored only locally
    public int DefaultMeetingReminderMinutes { get; set; } = 10;
    public bool StartWithWindows { get; set; } = true;

    // ---- adzan ----
    /// <summary>Master switch: play the adzan when a prayer time arrives.</summary>
    public bool AdhanEnabled { get; set; } = true;
    /// <summary>Quick mute from the tray; keeps the per-prayer choices intact.</summary>
    public bool AdhanMuted { get; set; }
    /// <summary>Which prayers play the adzan (only the five wajib are offered).</summary>
    public Dictionary<Prayer, bool> AdhanPrayers { get; set; } = new()
    {
        [Prayer.Subuh] = true, [Prayer.Dzuhur] = true, [Prayer.Ashar] = true,
        [Prayer.Maghrib] = true, [Prayer.Isya] = true,
    };
    /// <summary>Playback volume 0..100.</summary>
    public int AdhanVolume { get; set; } = 80;
    /// <summary>Optional user-chosen audio file (mp3/wav/wma/m4a); null = bundled adzan.</summary>
    public string? AdhanFile { get; set; }
    /// <summary>Optional separate file for Subuh; null = same as <see cref="AdhanFile"/> / bundled Subuh adzan.</summary>
    public string? AdhanSubuhFile { get; set; }

    // ---- ibadah check-ins + daily hadith ----
    /// <summary>Master switch for dzikir/sholawat check-in reminders.</summary>
    public bool HabitRemindersEnabled { get; set; } = true;
    public Dictionary<Habit, bool> HabitEnabled { get; set; } = new()
    {
        [Habit.DzikirPagi] = true, [Habit.DzikirPetang] = true, [Habit.Sholawat] = true,
    };
    /// <summary>Show the "Hadits hari ini" card once each morning.</summary>
    public bool DailyHadithCard { get; set; } = true;

    public bool IsHabitOn(Habit h) =>
        HabitRemindersEnabled && HabitEnabled is not null && HabitEnabled.TryGetValue(h, out var on) && on;

    /// <summary>True when the adzan should sound for this prayer right now.</summary>
    public bool ShouldPlayAdhan(Prayer p) =>
        AdhanEnabled && !AdhanMuted && AdhanPrayers is not null && AdhanPrayers.TryGetValue(p, out var on) && on;

    /// <summary>The selected city; falls back to Jakarta when the id is missing.</summary>
    public City GetCity() => string.IsNullOrWhiteSpace(CityId)
        ? DefaultCity.Jakarta
        : new City(CityId.Trim(), string.IsNullOrWhiteSpace(CityName) ? CityId.Trim() : CityName.Trim());
}
