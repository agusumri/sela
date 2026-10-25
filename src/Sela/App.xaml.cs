using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Sela.Core;
using Sela.Localization;
using Sela.Services;
using Sela.Views;

namespace Sela;

/// <summary>Composition root: wires Core services, timers, tray and views. Everything here runs on the UI thread.</summary>
public partial class App : Application, IAppCommands
{
    private const string MutexName = @"Local\Sela.SingleInstance";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private Mutex? _mutex;
    private HttpClient? _http;
    private SettingsStore _store = null!;
    private string _uiPath = null!;
    private SelaSettings _settings = null!;   // in memory: GoogleIcalUrl is decrypted
    private UiSettings _ui = null!;
    private MyQuranClient _myQuran = null!;
    private LocationSearch _locationSearch = null!;
    private PrayerScheduleService _prayers = null!;
    private readonly ReminderScheduler _scheduler = new();
    private IcsCalendarProvider _ics = null!;
    private AgendaService _agenda = null!;
    private HabitTracker _tracker = null!;
    private readonly HabitScheduler _habitScheduler = new();
    private HadithService? _hadithService; // null under --mock
    private DateOnly _day;                  // day the habit map / hadith were last refreshed for
    private SerialJob _hadithJob = null!;
    private AppLanguage? _langOverride;     // --lang=en|id: this session only, never saved

    private readonly NotchState _state = new();
    private NotchWindow _notch = null!;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private IAdhanPlayer _adhan = null!;
    private bool _mock; // --mock: fixed fake data; no network, disk, registry or sound
    private bool _offline; // --offline (debug): IsOnline=false, HTTP fails fast, Outlook unavailable
    private AgendaCache _agendaCache = new(enabled: false);
    private IReadOnlyList<CalendarEvent>? _cachedAgenda; // last saved agenda (disk copy), used for failed sources
    private DateTimeOffset? _cachedAgendaAt;
    private bool _agendaShown;              // an agenda (fresh or cached) was shown; no more "Loading agenda…"
    private bool _refreshRunning, _refreshQueued;
    private DispatcherTimer? _onlineDebounce;
    private readonly List<DispatcherTimer> _timers = new();
    private readonly CancellationTokenSource _shutdown = new();

    private DaySchedule? _today, _tomorrow;
    private (DateOnly Date, string CityId, int? Method, bool AsrHanafi) _scheduleKey;
    // One load at a time each; a forced call (RefreshNow) queues one follow-up instead of being dropped.
    private SerialJob _scheduleJob = null!, _calendarJob = null!;
    private IReadOnlyList<CalendarEvent> _events = Array.Empty<CalendarEvent>();
    private bool? _idleVisible;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _scheduleJob = new SerialJob(LoadScheduleAsync);
        _calendarJob = new SerialJob(LoadCalendarAsync);
        _hadithJob = new SerialJob(LoadHadithCoreAsync);
        DispatcherUnhandledException += (_, args) => { Log.Error("UI", args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write($"Fatal: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Task", args.Exception); args.SetObserved(); };
        Log.Write($"Sela start ({string.Join(' ', e.Args)})");
        _mock = e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase);
        _offline = e.Args.Contains("--offline", StringComparer.OrdinalIgnoreCase);
        _langOverride = ParseLanguageArg(e.Args);
        Loc.MissingKeyLog = Log.Write;

        if (_mock)
        {
            _settings = new SelaSettings();
            _ui = new UiSettings();
            ApplyLanguage(); // before any mock text is built
            _tracker = new HabitTracker(null);
            // Seed the in-memory tracker with the sample "done" state.
            var today = DateOnly.FromDateTime(DateTime.Now);
            foreach (var (habit, done) in MockData.HabitsDone())
                if (done) _tracker.MarkDone(habit, today);
            LoadMockData(e.Args.Contains("--mock-world", StringComparer.OrdinalIgnoreCase) ? MockData.London : null);
            if (_offline)
            {
                // Lets the designer see the cached / failed agenda states without a network.
                _state.AgendaFromCache = true;
                _state.AgendaUpdatedAt = DateTime.Now.AddHours(-2);
                if (e.Args.Contains("--agenda-failed", StringComparer.OrdinalIgnoreCase))
                {
                    _events = Array.Empty<CalendarEvent>();
                    _state.Agenda = _events;
                    _state.AgendaFromCache = false;
                    _state.AgendaUpdatedAt = null;
                    _state.AgendaFailed = true;
                }
            }
            _adhan = new SilentAdhanPlayer();
        }
        else
        {
            // --offline: every request fails at once and nothing reaches the network.
            _http = _offline ? new HttpClient(new OfflineHttpHandler()) : new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sela/1.0");
            _store = new SettingsStore();
            _uiPath = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "ui.json");
            LoadSettings();
            _ui = LoadUi();
            ApplyLanguage();

            _myQuran = new MyQuranClient(_http, Log.Write);
            var aladhan = new AladhanClient(_http, Log.Write);
            _locationSearch = new LocationSearch(_myQuran, new GeocodingClient(_http, Log.Write), Log.Write);
            _prayers = new PrayerScheduleService(_myQuran, log: Log.Write, world: aladhan);
            _ics = new IcsCalendarProvider(_http, () => _settings.GoogleIcalUrl, () => _settings.DefaultMeetingReminderMinutes, Log.Write);
            _agenda = CreateAgenda();
            _state.City = _settings.GetCity();
            _adhan = new AdhanPlayer();
            _tracker = HabitTracker.Default();
            _hadithService = new HadithService(new HadithClient(_http, Log.Write), log: Log.Write);
            _agendaCache = new AgendaCache(enabled: true);
            LoadCachedAgenda(); // the Agenda tab has content before the first fetch (and offline)
        }
        _adhan.PlayingChanged += () => _state.AdhanPlaying = _adhan.IsPlaying;
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        _notch = new NotchWindow(_state, this);
        _notch.Show();
        _notch.ApplyPlacement(_ui.NotchEdge, Math.Clamp(double.IsFinite(_ui.NotchX) ? _ui.NotchX : 0.5, 0, 1));
        _tray = new TrayIcon(this, () => _notch.ExpandPanel(), () => _notch.ShowHadith(), ResetPlacement,
            TestNotify, Shutdown, () => _settings.AdhanMuted, SetAdhanMuted, () => _adhan.IsPlaying);
        CheckDayChanged();

        StartTimer(TimeSpan.FromSeconds(1), ClockTick);
        StartTimer(TimeSpan.FromSeconds(2), UpdateIdleVisibility);
        StartTimer(TimeSpan.FromSeconds(20), () => _ = ReminderTickAsync());
        StartTimer(TimeSpan.FromSeconds(60), () => _ = RefreshCalendarAsync());
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        _state.IsOnline = !_offline && NetworkInterface.GetIsNetworkAvailable();
        if (!_offline) NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        UpdateIdleVisibility();
        RefreshNow();

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) _ = ShowDemoAsync();
        else if (e.Args.Contains("--test-notify", StringComparer.OrdinalIgnoreCase)) _ = ShowAfterDelayAsync(TestNotify);
        if (e.Args.Contains("--test-adhan", StringComparer.OrdinalIgnoreCase)) _ = ShowAfterDelayAsync(TestAdhan);
        if (e.Args.Contains("--test-habit", StringComparer.OrdinalIgnoreCase)) _ = ShowAfterDelayAsync(TestHabit);
        if (e.Args.Contains("--test-hadith", StringComparer.OrdinalIgnoreCase)) _ = ShowAfterDelayAsync(TestHadith);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex is not null)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            _onlineDebounce?.Stop();
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            foreach (var t in _timers) t.Stop();
            _adhan?.Stop();
            _shutdown.Cancel();
            _tray?.Dispose();
            _http?.Dispose();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            Log.Write("Sela exit");
        }
        base.OnExit(e);
    }

    // ---- IAppCommands ----

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        bool autostart = _settings.StartWithWindows;
        if (!_mock)
        {
            try { autostart = Autostart.IsEnabled; }
            catch (Exception ex) { Log.Error("Autostart", ex); }
        }

        CitySearch search = _mock
            ? (_, _) => Task.FromResult<IReadOnlyList<City>>(Array.Empty<City>())
            : (q, ct) => _locationSearch.SearchAsync(q, Loc.Instance.Language, ct);
        _settingsWindow = new SettingsWindow(Clone(_settings), Clone(_ui), autostart, search);
        _settingsWindow.Adhan = _adhan;
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenJoinUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Write("Tautan rapat ditolak (bukan https).");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Membuka tautan rapat", ex); // URL deliberately not logged
        }
    }

    public void StopAdhan() => _adhan.Stop();

    private void SetAdhanMuted(bool muted)
    {
        if (_settings.AdhanMuted == muted) return;
        _settings.AdhanMuted = muted;
        SaveSettings();
        if (muted) _adhan.Stop();
    }

    public void MarkHabitDone(Habit habit)
    {
        try { _tracker.MarkDone(habit, DateOnly.FromDateTime(DateTime.Now)); }
        catch (Exception ex) { Log.Error("Kebiasaan", ex); }
        RefreshHabitsDone();
    }

    public void SnoozeHabit(Habit habit) => _habitScheduler.Snooze(habit, DateTime.Now);

    public void SaveNotchPlacement(NotchEdge edge, double x)
    {
        _ui.NotchEdge = edge;
        _ui.NotchX = Math.Clamp(double.IsFinite(x) ? x : 0.5, 0, 1);
        SaveUi();
    }

    private void ResetPlacement()
    {
        _notch.ApplyPlacement(NotchEdge.Top, 0.5);
        SaveNotchPlacement(NotchEdge.Top, 0.5);
    }

    public void RefreshNow()
    {
        if (_refreshRunning) { _refreshQueued = true; return; } // coalesces into exactly one more run
        _ = RunRefreshAsync();
    }

    private async Task RunRefreshAsync()
    {
        _refreshRunning = true;
        _state.Refreshing = true;
        try
        {
            do
            {
                _refreshQueued = false;
                var minSpin = Task.Delay(TimeSpan.FromMilliseconds(400)); // keeps the button spin visible
                if (_mock)
                    await Task.Delay(TimeSpan.FromMilliseconds(600)); // --mock: no network
                else
                    await Task.WhenAll(ReminderTickAsync(force: true), RefreshCalendarAsync(force: true), LoadHadithAsync(force: true));
                await minSpin;
            }
            while (_refreshQueued && !_shutdown.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            Log.Error("Segarkan", ex);
        }
        finally
        {
            _refreshRunning = false;
            _state.Refreshing = false;
        }
    }


    // ---- settings ----

    private void LoadSettings()
    {
        bool firstRun = !File.Exists(_store.Path);
        _settings = _store.Load();
        bool legacyPlaintext = !string.IsNullOrEmpty(_settings.GoogleIcalUrl) && !SecretProtector.IsProtected(_settings.GoogleIcalUrl);
        _settings.GoogleIcalUrl = SecretProtector.Unprotect(_settings.GoogleIcalUrl);

        if (firstRun)
        {
            try { Autostart.Set(_settings.StartWithWindows); }
            catch (Exception ex) { Log.Error("Autostart", ex); }
        }
        if (firstRun || legacyPlaintext) SaveSettings();
    }

    private void SaveSettings()
    {
        if (_mock) return;
        try
        {
            var onDisk = Clone(_settings);
            onDisk.GoogleIcalUrl = SecretProtector.Protect(onDisk.GoogleIcalUrl);
            _store.Save(onDisk);
        }
        catch (Exception ex)
        {
            Log.Error("Gagal menyimpan pengaturan", ex);
        }
    }

    private UiSettings LoadUi()
    {
        try
        {
            if (File.Exists(_uiPath))
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(_uiPath), Json) ?? new UiSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error("ui.json diabaikan", ex);
        }
        return new UiSettings();
    }

    private void SaveUi()
    {
        if (_mock) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_uiPath)!);
            var temp = _uiPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_ui, Json));
            File.Move(temp, _uiPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Gagal menyimpan ui.json", ex);
        }
    }

    private void OnSettingsSaved(SelaSettings settings, UiSettings ui, bool startWithWindows)
    {
        settings.StartWithWindows = startWithWindows;
        _settings = Clone(settings);
        var placed = _ui;
        _ui = Clone(ui);
        // Placement and the daily-card date are owned by the shell; the window's copy may be stale.
        _ui.NotchEdge = placed.NotchEdge;
        _ui.NotchX = placed.NotchX;
        _ui.LastHadithCardDate = placed.LastHadithCardDate;
        SaveSettings();
        SaveUi();
        if (!_mock)
        {
            try { Autostart.Set(startWithWindows); }
            catch (Exception ex) { Log.Error("Autostart", ex); }
        }
        if (_adhan.IsPlaying && (_settings.AdhanMuted || !_settings.AdhanEnabled)) _adhan.Stop();
        // A saved choice replaces the session-only --lang override.
        _langOverride = null;
        ApplyLanguage();

        if (!_mock)
        {
            _agenda = CreateAgenda();
            _state.City = _settings.GetCity();
        }
        _idleVisible = null;
        UpdateIdleVisibility();
        RefreshHabitsDone();
        RefreshHiddenPrayers();
        RefreshNow();
    }

    // ---- language ----

    private static AppLanguage? ParseLanguageArg(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) continue;
            var value = arg["--lang=".Length..];
            if (value.Equals("en", StringComparison.OrdinalIgnoreCase)) return AppLanguage.English;
            if (value.Equals("id", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Indonesian;
            Log.Write($"Ignoring {arg} (expected --lang=en or --lang=id)");
        }
        return null;
    }

    private void ApplyLanguage() => Loc.Instance.Language = _langOverride ?? _settings.ResolveLanguage();

    /// <summary>
    /// Settings for the schedulers, whose reminder copy follows SelaSettings.ResolveLanguage():
    /// pinned to the active UI language so --lang and the notch agree. A copy, never saved.
    /// </summary>
    private SelaSettings SettingsInUiLanguage()
    {
        if (_settings.Language == Loc.Instance.Language) return _settings;
        var copy = Clone(_settings);
        copy.Language = Loc.Instance.Language;
        return copy;
    }

    private void OnLanguageChanged()
    {
        _tray?.SetNextPrayer(_state.Next);
        if (_mock) { _state.Hadith = MockData.Hadith(_day); return; }
        _ = LoadHadithAsync(queue: true); // a running load finishes in the old language; this one follows
    }

    private AgendaService CreateAgenda()
    {
        var providers = new List<ICalendarProvider>();
        if (_settings.OutlookEnabled)
            providers.Add(new OutlookCalendarProvider(() => _settings.DefaultMeetingReminderMinutes, Log.Write, forceUnavailable: _offline));
        // Only a configured feed counts as a source: an empty one would "succeed" and mark the agenda fresh offline.
        if (IcsCalendarProvider.NormalizeUrl(_settings.GoogleIcalUrl) is not null) providers.Add(_ics);
        return new AgendaService(providers, log: Log.Write);
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    // ---- timers ----

    private void StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        timer.Tick += (_, _) =>
        {
            try { tick(); }
            catch (Exception ex) { Log.Error("Timer", ex); }
        };
        timer.Start();
        _timers.Add(timer);
    }

    private void ClockTick()
    {
        var now = DateTime.Now;
        _state.Now = now;
        _state.Next = ReminderScheduler.NextPrayer(now, _today, _tomorrow);
        _tray?.SetNextPrayer(_state.Next);
    }

    private void UpdateIdleVisibility()
    {
        bool visible = _ui.NotchAlwaysVisible && !FullscreenDetector.IsFullscreenActive();
        if (_idleVisible == visible) return;
        _idleVisible = visible;
        _notch.SetIdleVisible(visible);
    }

    private async Task ReminderTickAsync(bool force = false)
    {
        try
        {
            await EnsureScheduleAsync(force);
            CheckDayChanged();
            // --mock is for demos and screenshots: no automatic cards, only the explicit
            // --demo / --test-* flags show reminders (otherwise a sample habit or hadith card
            // pops up at startup and covers whatever is being captured).
            if (_mock) return;
            var now = DateTime.Now;
            // Without a schedule, still deliver meeting reminders via an empty prayer list.
            var today = _today ?? new DaySchedule(DateOnly.FromDateTime(now), _settings.GetCity(), Array.Empty<PrayerTime>());
            var settings = SettingsInUiLanguage();
            foreach (var reminder in _scheduler.Due(now, today, _events, settings))
                Deliver(reminder);
            if (_today is not null && _today.Date == DateOnly.FromDateTime(now))
                foreach (var reminder in _habitScheduler.Due(now, _today, settings, _tracker))
                    Deliver(reminder);
            MaybeShowHadithCard(now);
        }
        catch (Exception ex)
        {
            Log.Error("Pengingat", ex);
        }
    }

    // ---- habits + hadith ----

    /// <summary>On startup and when the date rolls over: refresh the habit map and today's hadith.</summary>
    private void CheckDayChanged()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        if (date == _day) return;
        _day = date;
        RefreshHabitsDone();
        RefreshHiddenPrayers(); // also covers startup: CheckDayChanged runs once when the app starts
        _ = LoadHadithAsync();
    }

    /// <summary>
    /// The five wajib times always show in the panel; Imsak, Terbit and Dhuha only when switched on in Pengaturan.
    /// </summary>
    private void RefreshHiddenPrayers()
    {
        var hidden = new List<Prayer>();
        foreach (var p in new[] { Prayer.Imsak, Prayer.Terbit, Prayer.Dhuha })
            if (_settings.PrayerEnabled is null || !_settings.PrayerEnabled.TryGetValue(p, out var on) || !on) hidden.Add(p);
        _state.HiddenPrayers = hidden;
    }

    private void RefreshHabitsDone()
    {
        // Mock mode uses the same (in-memory) tracker, so clicking a pill really checks it.
        var date = DateOnly.FromDateTime(DateTime.Now);
        var map = new Dictionary<Habit, bool>();
        foreach (var h in Enum.GetValues<Habit>())
        {
            if (!_settings.IsHabitOn(h)) continue;
            try { map[h] = _tracker.IsDone(h, date); }
            catch (Exception ex) { Log.Error("Kebiasaan", ex); map[h] = false; }
        }
        _state.HabitsDone = map;
    }

    /// <param name="force">Bypass the retry backoff (RefreshNow).</param>
    /// <param name="queue">When a load is running, load once more afterwards (e.g. the language changed).</param>
    private Task LoadHadithAsync(bool force = false, bool queue = false)
    {
        if (_mock) { _state.Hadith = MockData.Hadith(_day); return Task.CompletedTask; }
        return _hadithJob.Run(force, queue);
    }

    private async Task LoadHadithCoreAsync(bool force)
    {
        if (_hadithService is not { } service || _shutdown.IsCancellationRequested) return;
        _state.HadithLoading = true;
        try
        {
            var language = Loc.Instance.Language;
            var hadith = await Task.Run(() => service.GetTodayAsync(_shutdown.Token, language, force));
            // Offline: keep today's hadith on screen rather than blanking it.
            if (hadith is not null || _state.Hadith?.Date != _day) _state.Hadith = hadith;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("Hadits", ex);
        }
        finally
        {
            _state.HadithLoading = false;
        }
    }

    /// <summary>Shows "Hadits hari ini" once a day, from Subuh+30m (07:00 without a schedule).</summary>
    private void MaybeShowHadithCard(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        if (!_settings.DailyHadithCard || _ui.LastHadithCardDate == date) return;
        if (_state.Hadith is not { } hadith || hadith.Date != date) return;

        var subuh = _today is not null && _today.Date == date
            ? _today.Times.FirstOrDefault(t => t.Prayer == Prayer.Subuh)?.Time
            : null;
        var from = subuh?.AddMinutes(30) ?? now.Date.AddHours(7);
        if (now < from) return;

        DeliverHadith(hadith, now);
        _ui.LastHadithCardDate = date;
        SaveUi();
    }

    private void DeliverHadith(Hadith hadith, DateTime now) =>
        Deliver(new Reminder($"hadith|{now:yyyyMMdd}", ReminderKind.Hadith, hadith.Title, hadith.Translation,
            null, null, now));

    /// <param name="force">RefreshNow: load even when today's schedule is loaded, bypassing the retry backoff
    /// (queued behind a running load instead of being dropped).</param>
    private Task EnsureScheduleAsync(bool force = false)
    {
        if (_mock) return Task.CompletedTask; // fixed schedule set by LoadMockData
        if (!force && ScheduleKey(DateOnly.FromDateTime(DateTime.Now), _settings.GetCity()) == _scheduleKey
            && _today is not null && _tomorrow is not null) return Task.CompletedTask;
        return _scheduleJob.Run(force);
    }

    private async Task LoadScheduleAsync(bool force)
    {
        if (_shutdown.IsCancellationRequested) return;
        var date = DateOnly.FromDateTime(DateTime.Now);
        var city = _settings.GetCity();
        var key = ScheduleKey(date, city);
        if (!force && key == _scheduleKey && _today is not null && _tomorrow is not null) return;
        try
        {
            var calc = _settings.GetPrayerCalc();
            var today = await _prayers.GetDayAsync(city, date, _shutdown.Token, calc, force);
            var tomorrow = await _prayers.GetDayAsync(city, date.AddDays(1), _shutdown.Token, calc, force);
            // City or calculation method changed meanwhile; next tick reloads.
            if (ScheduleKey(date, _settings.GetCity()) != key) return;

            // A failed re-validation never blanks a schedule that is already shown for this key.
            bool sameKey = key == _scheduleKey;
            if (today is null && sameKey) today = _today;
            if (tomorrow is null && sameKey) tomorrow = _tomorrow;

            _today = today;
            _tomorrow = tomorrow;
            _scheduleKey = key;
            _state.Today = today;
            _state.ScheduleAvailable = today is not null;
            _state.City = today?.City ?? city;
            ClockTick();
            _ = PrefetchAsync(city, calc, force);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("Jadwal", ex);
            _state.ScheduleAvailable = _today is not null;
        }
    }

    /// <summary>Cache key of the loaded schedule: a new day, place or calculation method forces a reload.</summary>
    private (DateOnly, string, int?, bool) ScheduleKey(DateOnly date, City city) =>
        (date, city.Id, _settings.PrayerMethod, _settings.AsrHanafi);

    private async Task PrefetchAsync(City city, PrayerCalc calc, bool force)
    {
        try { await _prayers.PrefetchAsync(city, _shutdown.Token, calc, force); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("Prefetch", ex); }
        catch (OperationCanceledException) { }
    }

    /// <param name="force">RefreshNow: providers read again now, and a running fetch queues one more.</param>
    private Task RefreshCalendarAsync(bool force = false) =>
        _mock ? Task.CompletedTask : _calendarJob.Run(force); // --mock keeps its fixed agenda

    /// <summary>Startup: show the saved agenda (≤ 48 h old) until the first fetch lands.</summary>
    private void LoadCachedAgenda()
    {
        if (_agendaCache.Load() is not { } cached) return;
        _cachedAgenda = cached.Events;
        _cachedAgendaAt = cached.SavedAt;
        var now = DateTime.Now;
        // Anything still upcoming counts: the next fetch decides per source what stays.
        _events = cached.Events.Where(e => e.End > now).ToList();
        _state.Agenda = TodayRemaining(_events, now);
        _state.AgendaUpdatedAt = cached.SavedAt.LocalDateTime;
        _state.AgendaFromCache = true;
        _agendaShown = true;
    }

    private async Task LoadCalendarAsync(bool force)
    {
        if (_shutdown.IsCancellationRequested) return;
        // Only before anything was shown: a periodic refresh must not flash "Loading agenda…".
        if (!_agendaShown) _state.CalendarLoading = true;
        try
        {
            var now = DateTime.Now;
            var from = now.AddHours(-1);
            var to = now.Date.AddDays(2); // end of tomorrow
            var agenda = _agenda;
            // Run off the UI thread: providers may do blocking work (process lookup, COM thread start).
            var result = await Task.Run(() => agenda.GetEventsWithStatusAsync(from, to, _shutdown.Token, force));
            now = DateTime.Now;

            bool anySucceeded = result.Configured.Any(s => !result.Failed.Contains(s));
            var (events, usedCache) = AgendaMerge.Merge(result.Events, result.Failed, _cachedAgenda, now);
            if (anySucceeded)
            {
                // Saved: fresh events of the sources that worked plus the cached ones still standing in for
                // failed sources, so a later outage of either source still has something to show.
                var savedAt = DateTimeOffset.Now;
                var toSave = events.ToList();
                _agendaCache.Save(toSave, savedAt);
                _cachedAgenda = toSave;
                if (!usedCache) _cachedAgendaAt = savedAt;
            }

            bool allFailed = result.Configured.Count > 0 && !anySucceeded;
            if (allFailed && events.Count == 0 && _events.Count > 0)
            {
                // Never replace a shown agenda with an empty list because of a failure.
                _state.AgendaFailed = false;
                return;
            }

            _events = events;
            _state.Agenda = TodayRemaining(events, now);
            _agendaShown = true;
            _state.AgendaFromCache = usedCache;
            _state.AgendaUpdatedAt = anySucceeded ? now : _cachedAgendaAt?.LocalDateTime ?? _state.AgendaUpdatedAt;
            _state.AgendaFailed = allFailed && events.Count == 0;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("Kalender", ex);
        }
        finally
        {
            _state.CalendarLoading = false;
        }
    }

    private static List<CalendarEvent> TodayRemaining(IEnumerable<CalendarEvent> events, DateTime now)
    {
        var endOfToday = now.Date.AddDays(1);
        return events.Where(e => e.End > now && e.Start < endOfToday).ToList();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        Dispatcher.BeginInvoke(() => SetOnline(e.IsAvailable)); // raised on a thread-pool thread

    private void SetOnline(bool online)
    {
        if (_shutdown.IsCancellationRequested) return;
        bool cameBack = online && !_state.IsOnline;
        _state.IsOnline = online;
        if (!online) { _onlineDebounce?.Stop(); return; }
        if (!cameBack) return;
        // Networks flap while connecting (Wi-Fi, VPN, DHCP): refresh once it has been up for ~2 s.
        if (_onlineDebounce is null)
        {
            _onlineDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _onlineDebounce.Tick += (_, _) =>
            {
                _onlineDebounce.Stop();
                if (_state.IsOnline) RefreshNow();
            };
        }
        _onlineDebounce.Stop();
        _onlineDebounce.Start();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(RefreshNow);
    }

    private void OnTimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshNow);

    // ---- demo / test ----

    /// <summary>--mock: fixed Jakarta (or --mock-world: London) schedule (Ashar in 25 min) and three fake events today.</summary>
    private void LoadMockData(City? city)
    {
        var start = DateTime.Now;
        (_today, _tomorrow) = MockData.Schedule(start, city);
        _scheduleKey = ScheduleKey(_today.Date, _today.City);
        _events = MockData.Agenda(start);
        _state.City = _today.City;
        _state.Today = _today;
        _state.ScheduleAvailable = true;
        _state.Agenda = _events;
        _state.CalendarLoading = false;
        _state.Next = ReminderScheduler.NextPrayer(start, _today, _tomorrow);
    }

    /// <summary>Shows a reminder and, for prayer-time reminders, sounds the adzan (PlayFor checks the settings).</summary>
    private void Deliver(Reminder reminder)
    {
        _notch.ShowReminder(reminder);
        if (reminder is { Kind: ReminderKind.PrayerNow, Prayer: { } p }) _adhan.PlayFor(p, _settings);
    }

    private static string AsharName => Names.Prayer(Prayer.Ashar, Loc.Instance.Language);

    private void TestNotify()
    {
        var now = DateTime.Now;
        Deliver(new Reminder($"test|{Guid.NewGuid():N}", ReminderKind.PrayerNow, Loc.F("Shell_PrayerNowTitle", AsharName),
            Loc.F("Shell_PrayerNowBody", AsharName, _state.City.DisplayName), now.ToString("HH:mm"), null, now, Prayer.Ashar));
    }

    private void TestAdhan()
    {
        var settings = Clone(_settings);
        settings.AdhanEnabled = true;
        settings.AdhanMuted = false;
        settings.AdhanPrayers ??= new();
        settings.AdhanPrayers[Prayer.Dzuhur] = true;
        // Show the prayer-time card too, so the test looks like the real moment (card + Stop button).
        var now = DateTime.Now;
        var name = Names.Prayer(Prayer.Dzuhur, Loc.Instance.Language);
        _notch.ShowReminder(new Reminder($"test-adhan|{Guid.NewGuid():N}", ReminderKind.PrayerNow,
            Loc.F("Shell_PrayerNowTitle", name), Loc.F("Shell_PrayerNowBody", name, _state.City.DisplayName),
            now.ToString("HH:mm"), null, now, Prayer.Dzuhur));
        _adhan.PlayFor(Prayer.Dzuhur, settings);
    }

    private void TestHabit()
    {
        var now = DateTime.Now;
        Deliver(new Reminder($"test-habit|{Guid.NewGuid():N}", ReminderKind.Habit, Loc.T("Shell_TestHabitTitle"),
            Loc.T("Shell_TestHabitBody"), null, null, now, Habit: Habit.Sholawat));
    }

    private void TestHadith()
    {
        var now = DateTime.Now;
        var hadith = _state.Hadith ?? MockData.Hadith(DateOnly.FromDateTime(now));
        Deliver(new Reminder($"test-hadith|{Guid.NewGuid():N}", ReminderKind.Hadith, hadith.Title, hadith.Translation,
            null, null, now));
    }

    private async Task ShowAfterDelayAsync(Action show)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        try { show(); }
        catch (Exception ex) { Log.Error("Demo", ex); }
    }

    private Task ShowDemoAsync() => ShowAfterDelayAsync(() =>
    {
        var now = DateTime.Now;
        string id = Guid.NewGuid().ToString("N");
        var soon = now.AddMinutes(10);
        _notch.ShowReminder(new Reminder($"demo|soon|{id}", ReminderKind.PrayerSoon, Loc.F("Shell_DemoSoonTitle", AsharName),
            Loc.F("Shell_DemoSoonBody", AsharName), soon.ToString("HH:mm"), null, now));
        Deliver(new Reminder($"demo|now|{id}", ReminderKind.PrayerNow, Loc.F("Shell_PrayerNowTitle", AsharName),
            Loc.F("Shell_PrayerNowBody", AsharName, _state.City.DisplayName), now.ToString("HH:mm"), null, now.AddSeconds(1), Prayer.Ashar));
        var start = now.AddMinutes(5);
        _notch.ShowReminder(new Reminder($"demo|meeting|{id}", ReminderKind.Meeting, "Weekly sync",
            Loc.F("Shell_DemoMeetingBody", start.ToString("HH:mm"), start.AddMinutes(30).ToString("HH:mm")), start.ToString("HH:mm"),
            "https://teams.microsoft.com/l/meetup-join/demo", now.AddSeconds(2)));
    });
}
