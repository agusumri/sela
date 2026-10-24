namespace Sela.Core.Tests;

public class ReminderSchedulerTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static DateTime At(int h, int m, int s = 0, int dayOffset = 0) => Day.ToDateTime(new TimeOnly(h, m, s)).AddDays(dayOffset);

    private static DaySchedule Schedule(int dayOffset = 0)
    {
        DateTime T(int h, int m) => At(h, m, 0, dayOffset);
        return new DaySchedule(Day.AddDays(dayOffset), DefaultCity.Jakarta, new[]
        {
            new PrayerTime(Prayer.Imsak, T(4, 1)), new PrayerTime(Prayer.Subuh, T(4, 11)),
            new PrayerTime(Prayer.Terbit, T(5, 30)), new PrayerTime(Prayer.Dhuha, T(5, 50)),
            new PrayerTime(Prayer.Dzuhur, T(11, 39)), new PrayerTime(Prayer.Ashar, T(14, 51)),
            new PrayerTime(Prayer.Maghrib, T(17, 48)), new PrayerTime(Prayer.Isya, T(18, 59)),
        });
    }

    private static readonly IReadOnlyList<CalendarEvent> NoEvents = Array.Empty<CalendarEvent>();

    [Fact]
    public void PrayerSoon_fires_once_inside_lead_window()
    {
        var sch = new ReminderScheduler();
        var s = new SelaSettings();
        Assert.Empty(sch.Due(At(11, 28, 59), Schedule(), NoEvents, s));

        var r = Assert.Single(sch.Due(At(11, 29), Schedule(), NoEvents, s));
        Assert.Equal(ReminderKind.PrayerSoon, r.Kind);
        Assert.Equal("10 menit lagi Dzuhur", r.Title);
        Assert.Equal("Bersiap untuk sholat Dzuhur", r.Body);
        Assert.Equal("11:39", r.Badge);

        Assert.Empty(sch.Due(At(11, 35), Schedule(), NoEvents, s)); // de-dup
    }

    [Fact]
    public void PrayerNow_fires_in_five_minute_window_only()
    {
        var s = new SelaSettings();
        var r = Assert.Single(new ReminderScheduler().Due(At(11, 39, 30), Schedule(), NoEvents, s));
        Assert.Equal(ReminderKind.PrayerNow, r.Kind);
        Assert.Equal("Waktu Dzuhur", r.Title);
        Assert.Equal("Sudah masuk waktu Dzuhur untuk Kota Jakarta", r.Body);

        Assert.Empty(new ReminderScheduler().Due(At(11, 44), Schedule(), NoEvents, s));
    }

    [Fact]
    public void Waking_from_sleep_does_not_replay_missed_reminders()
    {
        var sch = new ReminderScheduler();
        // Asleep from 10:00 to 15:00: Dzuhur reminders are gone; Ashar (14:51) is still in its 5-min window.
        var due = sch.Due(At(14, 52), Schedule(), NoEvents, new SelaSettings());
        var r = Assert.Single(due);
        Assert.Equal("Waktu Ashar", r.Title);
    }

    [Fact]
    public void Disabled_prayers_and_zero_lead_are_respected()
    {
        var s = new SelaSettings { PrayerSoonMinutes = 0 };
        s.PrayerEnabled[Prayer.Dzuhur] = false;
        Assert.Empty(new ReminderScheduler().Due(At(11, 39), Schedule(), NoEvents, s));
        Assert.Empty(new ReminderScheduler().Due(At(14, 45), Schedule(), NoEvents, s));
        Assert.Single(new ReminderScheduler().Due(At(14, 51), Schedule(), NoEvents, s));
    }

    [Fact]
    public void Optional_prayers_have_their_own_copy()
    {
        var s = new SelaSettings();
        s.PrayerEnabled[Prayer.Imsak] = true;
        s.PrayerEnabled[Prayer.Terbit] = true;
        var imsak = Assert.Single(new ReminderScheduler().Due(At(3, 55), Schedule(), NoEvents, s));
        Assert.Equal("Imsak 6 menit lagi", imsak.Title);
        Assert.Equal("Waktu imsak", imsak.Body);
        var terbit = Assert.Single(new ReminderScheduler().Due(At(5, 25), Schedule(), NoEvents, s));
        Assert.Equal("Matahari terbit", terbit.Body);
    }

    [Fact]
    public void Day_rollover_resets_dedup()
    {
        var sch = new ReminderScheduler();
        var s = new SelaSettings();
        Assert.Single(sch.Due(At(4, 11), Schedule(), NoEvents, s));
        Assert.Empty(sch.Due(At(4, 12), Schedule(), NoEvents, s));
        Assert.Single(sch.Due(At(4, 11, 0, 1), Schedule(1), NoEvents, s));
    }

    private static CalendarEvent Meeting(DateTime start, int reminder = 10, string? location = "Ruang 3") =>
        new("evt1|" + start.ToString("s"), "Sprint review", start, start.AddMinutes(30), location, "https://zoom.us/j/1", reminder, CalendarSource.Google);

    [Fact]
    public void Meeting_reminder_window_and_copy()
    {
        var sch = new ReminderScheduler();
        var s = new SelaSettings();
        var events = new[] { Meeting(At(9, 0)) };
        Assert.Empty(sch.Due(At(8, 49, 59), Schedule(), events, s));

        var r = Assert.Single(sch.Due(At(8, 52), Schedule(), events, s));
        Assert.Equal(ReminderKind.Meeting, r.Kind);
        Assert.Equal("Sprint review", r.Title);
        Assert.Equal("09:00–09:30 · mulai 8 menit lagi · Ruang 3", r.Body);
        Assert.Equal("09:00", r.Badge);
        Assert.Equal("https://zoom.us/j/1", r.JoinUrl);
        Assert.Empty(sch.Due(At(8, 55), Schedule(), events, s));
    }

    [Fact]
    public void Meeting_at_start_says_now_and_expires_after_one_minute()
    {
        var events = new[] { Meeting(At(9, 0), location: null) };
        var r = Assert.Single(new ReminderScheduler().Due(At(9, 0, 30), Schedule(), events, new SelaSettings()));
        Assert.Equal("09:00–09:30 · mulai sekarang", r.Body);
        Assert.Empty(new ReminderScheduler().Due(At(9, 1), Schedule(), events, new SelaSettings()));
    }

    [Fact]
    public void NextPrayer_returns_upcoming_wajib_and_rolls_to_tomorrow()
    {
        Assert.Equal((Prayer.Dzuhur, At(11, 39)), ReminderScheduler.NextPrayer(At(5, 0), Schedule(), Schedule(1)));
        Assert.Equal((Prayer.Ashar, At(14, 51)), ReminderScheduler.NextPrayer(At(11, 39), Schedule(), Schedule(1)));
        Assert.Equal((Prayer.Subuh, At(4, 11, 0, 1)), ReminderScheduler.NextPrayer(At(19, 0), Schedule(), Schedule(1)));
    }

    [Fact]
    public void NextPrayer_handles_missing_schedules()
    {
        Assert.Equal((Prayer.Isya, At(18, 59)), ReminderScheduler.NextPrayer(At(18, 0), Schedule(), null));
        Assert.Null(ReminderScheduler.NextPrayer(At(19, 0), Schedule(), null));
        Assert.Equal((Prayer.Subuh, At(4, 11, 0, 1)), ReminderScheduler.NextPrayer(At(19, 0), null, Schedule(1)));
        Assert.Null(ReminderScheduler.NextPrayer(At(19, 0), null, null));
    }
}
