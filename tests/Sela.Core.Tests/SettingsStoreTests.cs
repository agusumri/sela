namespace Sela.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sela-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(DefaultCity.Jakarta, s.GetCity() with { Province = "DKI JAKARTA" });
        Assert.True(s.PrayerEnabled[Prayer.Subuh]);
    }

    [Fact]
    public void Roundtrip_with_enum_strings()
    {
        var store = new SettingsStore(FilePath);
        var s = new SelaSettings { CityId = "1219", CityName = "KOTA BANDUNG", PrayerSoonMinutes = 15 };
        s.PrayerEnabled[Prayer.Imsak] = true;
        store.Save(s);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"Imsak\"", json);
        Assert.False(File.Exists(FilePath + ".tmp"));

        var loaded = store.Load();
        Assert.Equal("1219", loaded.CityId);
        Assert.Equal("KOTA BANDUNG", loaded.CityName);
        Assert.Equal(15, loaded.PrayerSoonMinutes);
        Assert.True(loaded.PrayerEnabled[Prayer.Imsak]);
        Assert.Equal(new City("1219", "KOTA BANDUNG"), loaded.GetCity());

        store.Save(new SelaSettings { CityId = "0228", CityName = "KOTA MEDAN" }); // overwrite existing
        Assert.Equal("KOTA MEDAN", store.Load().CityName);
    }

    [Fact]
    public void Corrupt_file_returns_defaults_and_keeps_backup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal("1301", s.CityId);
        Assert.Equal("{ not json", File.ReadAllText(FilePath + ".bak"));
    }

    [Fact]
    public void Null_prayer_map_is_replaced_by_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"CityId\":\"0228\",\"CityName\":\"KOTA MEDAN\",\"PrayerEnabled\":null}");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal("KOTA MEDAN", s.CityName);
        Assert.True(s.PrayerEnabled[Prayer.Isya]);
    }

    [Fact]
    public void Old_file_without_adhan_keys_gets_adhan_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"CityId\":\"1301\",\"CityName\":\"KOTA JAKARTA\"}");
        var s = new SettingsStore(FilePath).Load();
        Assert.True(s.AdhanEnabled);
        Assert.False(s.AdhanMuted);
        Assert.Equal(80, s.AdhanVolume);
        foreach (var p in ReminderScheduler.Wajib) Assert.True(s.ShouldPlayAdhan(p));
        Assert.False(s.ShouldPlayAdhan(Prayer.Imsak));
    }

    [Fact]
    public void Null_adhan_map_and_bad_volume_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"CityId\":\"1301\",\"CityName\":\"KOTA JAKARTA\",\"AdhanPrayers\":null,\"AdhanVolume\":900}");
        var s = new SettingsStore(FilePath).Load();
        Assert.True(s.ShouldPlayAdhan(Prayer.Maghrib));
        Assert.Equal(100, s.AdhanVolume);
    }

    [Fact]
    public void Muted_or_disabled_adhan_never_plays()
    {
        var s = new SelaSettings { AdhanMuted = true };
        Assert.False(s.ShouldPlayAdhan(Prayer.Dzuhur));
        s = new SelaSettings { AdhanEnabled = false };
        Assert.False(s.ShouldPlayAdhan(Prayer.Dzuhur));
        s = new SelaSettings();
        s.AdhanPrayers[Prayer.Ashar] = false;
        Assert.False(s.ShouldPlayAdhan(Prayer.Ashar));
        Assert.True(s.ShouldPlayAdhan(Prayer.Isya));
    }

    [Fact]
    public void Legacy_file_without_city_id_falls_back_to_jakarta()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"CityName\":\"Medan\",\"Ihtiyat\":2}");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(DefaultCity.Jakarta.Id, s.CityId);
        Assert.Equal(DefaultCity.Jakarta.Name, s.CityName);
    }
}
