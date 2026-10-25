namespace Sela.Core;

// Shared contracts between Sela.Core (logic, platform-neutral) and Sela (WPF UI + Windows-only providers).

public enum Prayer { Imsak, Subuh, Terbit, Dhuha, Dzuhur, Ashar, Maghrib, Isya }

/// <summary>
/// A place to get prayer times for. Two kinds:
/// <list type="bullet">
/// <item>Kemenag (Indonesia): <see cref="Id"/> is the numeric myquran.com city id, Name like "KOTA JAKARTA", no coordinates.</item>
/// <item>World: <see cref="Id"/> is "geo:{geonamesId}", with coordinates and an IANA time zone; times come from Aladhan.</item>
/// </list>
/// </summary>
public sealed record City(
    string Id,
    string Name,
    string? Province = null,     // Kemenag: province; World: region (admin1), e.g. "England"
    double? Latitude = null,
    double? Longitude = null,
    string? TimeZoneId = null,   // IANA, e.g. "Europe/London"
    string? Country = null)      // World: country name, e.g. "United Kingdom"
{
    /// <summary>True for a worldwide (Aladhan) location, false for an Indonesian Kemenag city.</summary>
    public bool IsWorld => Latitude is not null && Longitude is not null;

    /// <summary>"Kota Jakarta" for Kemenag names (title-cased), "London, United Kingdom" for world places.</summary>
    public string DisplayName => IsWorld
        ? (string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}")
        : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Name.ToLowerInvariant());
}

public static class DefaultCity
{
    public static City Jakarta { get; } = new("1301", "KOTA JAKARTA", "DKI JAKARTA");
}

public sealed record PrayerTime(Prayer Prayer, DateTime Time);

/// <summary>Aladhan calculation options. <paramref name="Method"/> null = Aladhan picks by location; AsrHanafi = school 1.</summary>
public sealed record PrayerCalc(int? Method = null, bool AsrHanafi = false);

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

/// <summary>Optional for providers that throttle or cache their own reads (e.g. the iCal feed).</summary>
public interface IRefreshableCalendarProvider : ICalendarProvider
{
    /// <summary>The next GetEventsAsync reads again even inside the throttle window (still one read at a time, same limits).</summary>
    void RequestFreshFetch();
    /// <summary>True when the latest read failed, so the events returned (if any) are stale.</summary>
    bool LastFetchFailed { get; }
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
    CalendarSource? Source = null, // set for Meeting reminders: shown as an Outlook / Google badge
    Dhikr? Dhikr = null);    // set on PrayerSoon when SelaSettings.PrayerDhikr: one short dhikr shown on the card

/// <summary>A short dhikr shown on the "N minutes until prayer" card (istighfar, tahmid, tahlil).</summary>
public sealed record Dhikr(
    DhikrKind Kind,
    string Arabic,           // with harakat, shown right-to-left
    string Latin,            // transliteration, e.g. "Astaghfirullah"
    string Meaning);         // in the reminder's language

public enum DhikrKind { Istighfar, Tahmid, Tahlil }

/// <summary>
/// One hadith for the daily card (Arbain Nawawi, 42 hadith). Indonesian text comes from api.myquran.com,
/// English from fawazahmed0/hadith-api (Unlicense).
/// </summary>
public sealed record Hadith(
    string Collection,       // e.g. "Arbain Nawawi" / "Forty Hadith of an-Nawawi"
    int Number,
    string Title,            // e.g. "Amalan Bergantung pada Niat"; English edition has none → "Hadith 12"
    string Arabic,
    string Translation,      // in Language
    AppLanguage Language,
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
    /// <summary>Add one short dhikr (istighfar, tahmid or tahlil) to the "N minutes until prayer" card.</summary>
    public bool PrayerDhikr { get; set; } = true;

    // ---- language ----
    /// <summary>Interface language; null = follow the Windows display language.</summary>
    public AppLanguage? Language { get; set; }
    public AppLanguage ResolveLanguage() =>
        Language ?? AppLanguages.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);

    // ---- worldwide location (Aladhan). All null = Indonesian Kemenag city from CityId/CityName. ----
    public double? CityLatitude { get; set; }
    public double? CityLongitude { get; set; }
    public string? CityTimeZone { get; set; }      // IANA
    public string? CityCountry { get; set; }
    public string? CityRegion { get; set; }
    /// <summary>Aladhan calculation method id (e.g. 3 = Muslim World League); null = Aladhan picks by location.</summary>
    public int? PrayerMethod { get; set; }
    /// <summary>Asr by the Hanafi school (shadow factor 2) instead of the standard Shafi'i one.</summary>
    public bool AsrHanafi { get; set; }

    /// <summary>Calculation options for worldwide (Aladhan) locations; ignored for Kemenag cities.</summary>
    public PrayerCalc GetPrayerCalc() => new(PrayerMethod, AsrHanafi);

    public bool IsHabitOn(Habit h) =>
        HabitRemindersEnabled && HabitEnabled is not null && HabitEnabled.TryGetValue(h, out var on) && on;

    /// <summary>True when the adzan should sound for this prayer right now.</summary>
    public bool ShouldPlayAdhan(Prayer p) =>
        AdhanEnabled && !AdhanMuted && AdhanPrayers is not null && AdhanPrayers.TryGetValue(p, out var on) && on;

    /// <summary>The selected place; falls back to Jakarta when nothing usable is stored.</summary>
    public City GetCity()
    {
        if (CityLatitude is { } lat && CityLongitude is { } lng && double.IsFinite(lat) && double.IsFinite(lng)
            && !string.IsNullOrWhiteSpace(CityId))
            return new City(CityId.Trim(), string.IsNullOrWhiteSpace(CityName) ? CityId.Trim() : CityName.Trim(),
                CityRegion, lat, lng, CityTimeZone, CityCountry);
        return string.IsNullOrWhiteSpace(CityId)
            ? DefaultCity.Jakarta
            : new City(CityId.Trim(), string.IsNullOrWhiteSpace(CityName) ? CityId.Trim() : CityName.Trim());
    }

    /// <summary>Stores <paramref name="city"/> as the selected place (Kemenag or world).</summary>
    public void SetCity(City city)
    {
        CityId = city.Id;
        CityName = city.Name;
        CityLatitude = city.Latitude;
        CityLongitude = city.Longitude;
        CityTimeZone = city.TimeZoneId;
        CityCountry = city.Country;
        CityRegion = city.IsWorld ? city.Province : null;
    }
}
