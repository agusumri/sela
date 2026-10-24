namespace Sela.Core.Tests;

public class HabitTests
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

    private static HabitTracker Mem() => new(null);

    [Fact]
    public void DzikirPagi_fires_once_inside_window_not_before_or_after()
    {
        var sch = new HabitScheduler();
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        var t = Mem();
        Assert.Empty(sch.Due(At(4, 25, 59), Schedule(), s, t));

        var r = Assert.Single(sch.Due(At(4, 26), Schedule(), s, t));
        Assert.Equal(ReminderKind.Habit, r.Kind);
        Assert.Equal(Habit.DzikirPagi, r.Habit);
        Assert.Equal("Sudah dzikir pagi?", r.Title);
        Assert.Equal("Luangkan sebentar untuk dzikir pagi.", r.Body);
        Assert.Null(r.Badge);
        Assert.Null(r.JoinUrl);
        Assert.Equal(At(4, 26), r.DueAt);
        Assert.Equal("habit|20261024|DzikirPagi|0", r.Key);

        Assert.Empty(sch.Due(At(9, 0), Schedule(), s, t)); // de-dup
    }

    [Fact]
    public void Window_end_is_exclusive_and_late_start_still_fires_inside()
    {
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        Assert.Empty(new HabitScheduler().Due(At(11, 39), Schedule(), s, Mem()));
        var r = Assert.Single(new HabitScheduler().Due(At(11, 38), Schedule(), s, Mem()));
        Assert.Equal(At(4, 26), r.DueAt);
    }

    [Fact]
    public void Sholawat_and_DzikirPetang_use_their_windows()
    {
        var sch = new HabitScheduler();
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        var t = Mem();
        Assert.Empty(sch.Due(At(12, 8), Schedule(), s, t));
        var sh = Assert.Single(sch.Due(At(12, 9), Schedule(), s, t));
        Assert.Equal(Habit.Sholawat, sh.Habit);
        Assert.Equal("Sudah bersholawat hari ini?", sh.Title);
        Assert.Equal("Allahumma shalli 'ala Muhammad.", sh.Body);

        Assert.Empty(sch.Due(At(15, 5), Schedule(), s, t));
        var pt = Assert.Single(sch.Due(At(15, 6), Schedule(), s, t));
        Assert.Equal(Habit.DzikirPetang, pt.Habit);
        Assert.Equal("Sudah dzikir petang?", pt.Title);
        Assert.Equal("Sempatkan dzikir petang sebelum Maghrib.", pt.Body);
        Assert.Empty(new HabitScheduler().Due(At(17, 48), Schedule(), s, t));
    }

    [Fact]
    public void Not_fired_when_done_today()
    {
        var t = Mem();
        t.MarkDone(Habit.DzikirPagi, Day);
        Assert.Empty(new HabitScheduler().Due(At(5, 0), Schedule(), new SelaSettings { Language = AppLanguage.Indonesian }, t));
    }

    [Fact]
    public void Not_fired_when_disabled()
    {
        var master = new SelaSettings { Language = AppLanguage.Indonesian, HabitRemindersEnabled = false };
        Assert.Empty(new HabitScheduler().Due(At(5, 0), Schedule(), master, Mem()));

        var perHabit = new SelaSettings { Language = AppLanguage.Indonesian };
        perHabit.HabitEnabled[Habit.DzikirPagi] = false;
        Assert.Empty(new HabitScheduler().Due(At(5, 0), Schedule(), perHabit, Mem()));
        Assert.Single(new HabitScheduler().Due(At(12, 30), Schedule(), perHabit, Mem()));
    }

    [Fact]
    public void Snooze_refires_after_30_minutes_with_new_key()
    {
        var sch = new HabitScheduler();
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        var t = Mem();
        var first = Assert.Single(sch.Due(At(5, 0), Schedule(), s, t));
        sch.Snooze(Habit.DzikirPagi, At(5, 1));

        Assert.Empty(sch.Due(At(5, 30, 59), Schedule(), s, t));
        var again = Assert.Single(sch.Due(At(5, 31), Schedule(), s, t));
        Assert.Equal("habit|20261024|DzikirPagi|1", again.Key);
        Assert.NotEqual(first.Key, again.Key);
        Assert.Equal(At(5, 31), again.DueAt);
        Assert.Empty(sch.Due(At(6, 30), Schedule(), s, t));
    }

    [Fact]
    public void Snooze_past_window_end_never_refires()
    {
        var sch = new HabitScheduler();
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        var t = Mem();
        Assert.Single(sch.Due(At(11, 20), Schedule(), s, t));
        sch.Snooze(Habit.DzikirPagi, At(11, 20)); // 11:50 is after Dzuhur 11:39
        Assert.Empty(sch.Due(At(11, 38), Schedule(), s, t));
    }

    [Fact]
    public void Day_rollover_resets_state()
    {
        var sch = new HabitScheduler();
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        var t = Mem();
        Assert.Single(sch.Due(At(5, 0), Schedule(), s, t));
        var next = Assert.Single(sch.Due(At(5, 0, 0, 1), Schedule(1), s, t));
        Assert.Equal("habit|20261025|DzikirPagi|0", next.Key);
    }

    [Fact]
    public void Tracker_persists_across_instances()
    {
        var dir = Directory.CreateTempSubdirectory("sela-habits-");
        try
        {
            var path = Path.Combine(dir.FullName, "sub", "habits.json");
            new HabitTracker(path).MarkDone(Habit.Sholawat, Day);
            var reloaded = new HabitTracker(path);
            Assert.True(reloaded.IsDone(Habit.Sholawat, Day));
            Assert.False(reloaded.IsDone(Habit.DzikirPagi, Day));
            Assert.Contains("\"Sholawat\":[\"2026-10-24\"]", File.ReadAllText(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Tracker_prunes_older_than_60_days()
    {
        var dir = Directory.CreateTempSubdirectory("sela-habits-");
        try
        {
            var path = Path.Combine(dir.FullName, "habits.json");
            var t = new HabitTracker(path);
            t.MarkDone(Habit.DzikirPagi, Day.AddDays(-60));
            t.MarkDone(Habit.DzikirPagi, Day.AddDays(-59));
            t.MarkDone(Habit.DzikirPagi, Day);
            var reloaded = new HabitTracker(path);
            Assert.False(reloaded.IsDone(Habit.DzikirPagi, Day.AddDays(-60)));
            Assert.True(reloaded.IsDone(Habit.DzikirPagi, Day.AddDays(-59)));
            Assert.True(reloaded.IsDone(Habit.DzikirPagi, Day));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Tracker_survives_corrupt_file()
    {
        var dir = Directory.CreateTempSubdirectory("sela-habits-");
        try
        {
            var path = Path.Combine(dir.FullName, "habits.json");
            File.WriteAllText(path, "{not json");
            var t = new HabitTracker(path);
            Assert.False(t.IsDone(Habit.DzikirPagi, Day));
            t.MarkDone(Habit.DzikirPagi, Day);
            Assert.True(new HabitTracker(path).IsDone(Habit.DzikirPagi, Day));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Null_path_keeps_memory_only()
    {
        var t = new HabitTracker(null);
        t.MarkDone(Habit.DzikirPetang, Day);
        Assert.True(t.IsDone(Habit.DzikirPetang, Day));
        Assert.False(new HabitTracker(null).IsDone(Habit.DzikirPetang, Day));
    }
}
