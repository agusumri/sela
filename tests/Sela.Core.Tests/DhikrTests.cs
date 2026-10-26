namespace Sela.Core.Tests;

public class DhikrTests
{
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static DateTime At(int h, int m) => Day.ToDateTime(new TimeOnly(h, m));
    private static readonly IReadOnlyList<CalendarEvent> NoEvents = Array.Empty<CalendarEvent>();

    private static DaySchedule Schedule() => new(Day, DefaultCity.Jakarta, new[]
    {
        new PrayerTime(Prayer.Imsak, At(4, 1)), new PrayerTime(Prayer.Subuh, At(4, 11)),
        new PrayerTime(Prayer.Terbit, At(5, 30)), new PrayerTime(Prayer.Dhuha, At(6, 30)),
        new PrayerTime(Prayer.Dzuhur, At(11, 39)), new PrayerTime(Prayer.Ashar, At(14, 51)),
        new PrayerTime(Prayer.Maghrib, At(17, 48)), new PrayerTime(Prayer.Isya, At(18, 59)),
    });

    [Fact]
    public void Pick_is_deterministic_and_in_range()
    {
        var d = new DateOnly(2026, 1, 1);
        for (int i = 0; i < 400; i++, d = d.AddDays(1))
            foreach (var p in Enum.GetValues<Prayer>())
            {
                var k = DhikrLibrary.Pick(d, p);
                Assert.True(Enum.IsDefined(k));
                Assert.Equal(k, DhikrLibrary.Pick(d, p));
            }
    }

    [Fact]
    public void Consecutive_prayers_differ_and_a_day_uses_every_kind()
    {
        var d = new DateOnly(2026, 1, 1);
        for (int i = 0; i < 60; i++, d = d.AddDays(1))
        {
            var kinds = ReminderScheduler.Wajib.Select(p => DhikrLibrary.Pick(d, p)).ToList();
            Assert.Equal(kinds.Count, kinds.Distinct().Count());
            for (int j = 1; j < kinds.Count; j++) Assert.NotEqual(kinds[j - 1], kinds[j]);
            // Across midnight too: Isya → next day's Subuh.
            Assert.NotEqual(DhikrLibrary.Pick(d, Prayer.Isya), DhikrLibrary.Pick(d.AddDays(1), Prayer.Subuh));
        }
    }

    [Fact]
    public void Pick_varies_across_days()
    {
        var d = new DateOnly(2026, 1, 1);
        for (int i = 0; i < 30; i++, d = d.AddDays(1))
        {
            Assert.NotEqual(DhikrLibrary.Pick(d, Prayer.Subuh), DhikrLibrary.Pick(d.AddDays(1), Prayer.Subuh));
            var today = ReminderScheduler.Wajib.Select(p => DhikrLibrary.Pick(d, p));
            var tomorrow = ReminderScheduler.Wajib.Select(p => DhikrLibrary.Pick(d.AddDays(1), p));
            Assert.NotEqual(today, tomorrow);
        }
    }

    [Fact]
    public void Each_prayer_cycles_through_every_kind()
    {
        int n = Enum.GetValues<DhikrKind>().Length;
        var start = new DateOnly(2026, 1, 1);
        foreach (var p in ReminderScheduler.Wajib)
            Assert.Equal(n, Enumerable.Range(0, n).Select(i => DhikrLibrary.Pick(start.AddDays(i), p)).Distinct().Count());
    }

    [Fact]
    public void Every_kind_has_text_and_an_invite()
    {
        foreach (var k in Enum.GetValues<DhikrKind>())
            foreach (var lang in new[] { AppLanguage.Indonesian, AppLanguage.English })
            {
                var d = DhikrLibrary.For(k, lang);
                Assert.False(string.IsNullOrWhiteSpace(d.Arabic));
                Assert.False(string.IsNullOrWhiteSpace(d.Latin));
                Assert.False(string.IsNullOrWhiteSpace(d.Meaning));
                Assert.True(Enum.IsDefined(DhikrLibrary.InviteOf(k)));
            }
    }

    [Theory]
    [InlineData(DhikrKind.Istighfar, "أَسْتَغْفِرُ اللّٰهَ", "Astaghfirullah", "Aku memohon ampun kepada Allah", "I seek Allah's forgiveness")]
    [InlineData(DhikrKind.Tahmid, "اَلْحَمْدُ لِلّٰهِ", "Alhamdulillah", "Segala puji bagi Allah", "All praise is for Allah")]
    [InlineData(DhikrKind.Tahlil, "لَا إِلٰهَ إِلَّا اللّٰهُ", "Lā ilāha illallāh", "Tiada Tuhan selain Allah", "There is no god but Allah")]
    [InlineData(DhikrKind.Takbir, "اَللّٰهُ أَكْبَرُ", "Allāhu akbar", "Allah Maha Besar", "Allah is the Greatest")]
    [InlineData(DhikrKind.Shalawat, "صَلَّى اللّٰهُ عَلَىٰ مُحَمَّدٍ", "Shallallāhu ‘alā Muhammad",
        "Semoga Allah melimpahkan shalawat kepada Muhammad", "May Allah send blessings upon Muhammad")]
    public void For_returns_text_per_language(DhikrKind kind, string arabic, string latin, string id, string en)
    {
        Assert.Equal(new Dhikr(kind, arabic, latin, id), DhikrLibrary.For(kind, AppLanguage.Indonesian));
        Assert.Equal(new Dhikr(kind, arabic, latin, en), DhikrLibrary.For(kind, AppLanguage.English));
    }

    [Theory]
    [InlineData(AppLanguage.Indonesian)]
    [InlineData(AppLanguage.English)]
    public void Wajib_prayer_soon_carries_dhikr_in_reminder_language(AppLanguage lang)
    {
        var s = new SelaSettings { Language = lang };
        var r = Assert.Single(new ReminderScheduler().Due(At(11, 29), Schedule(), NoEvents, s));
        Assert.Equal(ReminderKind.PrayerSoon, r.Kind);
        Assert.Equal(DhikrLibrary.For(DhikrLibrary.Pick(Day, Prayer.Dzuhur), lang), r.Dhikr);
        Assert.Equal(lang == AppLanguage.English ? "Dhuhr in 10 min" : "10 menit lagi Dzuhur", r.Title);
        Assert.Equal("soon|20261024|Dzuhur", r.Key);
    }

    [Fact]
    public void Every_wajib_soon_card_has_one()
    {
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        var sch = new ReminderScheduler();
        foreach (var p in ReminderScheduler.Wajib)
        {
            var t = Schedule()[p];
            var r = Assert.Single(sch.Due(t.AddMinutes(-5), Schedule(), NoEvents, s));
            Assert.Equal(p, r.Prayer);
            Assert.NotNull(r.Dhikr);
        }
    }

    [Fact]
    public void Not_attached_to_prayer_now_optional_times_or_meetings()
    {
        var s = new SelaSettings { Language = AppLanguage.Indonesian };
        s.PrayerEnabled[Prayer.Imsak] = true;
        s.PrayerEnabled[Prayer.Terbit] = true;
        s.PrayerEnabled[Prayer.Dhuha] = true;

        Assert.Null(Assert.Single(new ReminderScheduler().Due(At(11, 40), Schedule(), NoEvents, s)).Dhikr); // PrayerNow
        foreach (var t in new[] { At(3, 55), At(5, 25), At(6, 25) })                                       // Imsak/Terbit/Dhuha soon
        {
            var r = Assert.Single(new ReminderScheduler().Due(t, Schedule(), NoEvents, s));
            Assert.Equal(ReminderKind.PrayerSoon, r.Kind);
            Assert.Null(r.Dhikr);
        }

        var meeting = new CalendarEvent("m|1", "Sync", At(9, 0), At(9, 30), null, null, 10, CalendarSource.Google);
        Assert.Null(Assert.Single(new ReminderScheduler().Due(At(8, 52), Schedule(), new[] { meeting }, s)).Dhikr);
    }

    [Fact]
    public void Switched_off_gives_null()
    {
        var s = new SelaSettings { Language = AppLanguage.Indonesian, PrayerDhikr = false };
        var r = Assert.Single(new ReminderScheduler().Due(At(11, 29), Schedule(), NoEvents, s));
        Assert.Null(r.Dhikr);
        Assert.Equal("Bersiap untuk sholat Dzuhur", r.Body);
    }
}
