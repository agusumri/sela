using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Sela.Core;
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
    private PrayerScheduleService _prayers = null!;
    private readonly ReminderScheduler _scheduler = new();
    private IcsCalendarProvider _ics = null!;
    private AgendaService _agenda = null!;

    private readonly NotchState _state = new();
    private NotchWindow _notch = null!;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private AdhanPlayer _adhan = null!;
    private readonly List<DispatcherTimer> _timers = new();
    private readonly CancellationTokenSource _shutdown = new();

    private DaySchedule? _today, _tomorrow;
    private (DateOnly Date, string CityId) _scheduleKey;
    private bool _scheduleBusy, _calendarBusy;
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

        DispatcherUnhandledException += (_, args) => { Log.Error("UI", args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write($"Fatal: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Task", args.Exception); args.SetObserved(); };
        Log.Write($"Sela start ({string.Join(' ', e.Args)})");

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sela/1.0");
        _store = new SettingsStore();
        _uiPath = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "ui.json");
        LoadSettings();
        _ui = LoadUi();

        _myQuran = new MyQuranClient(_http, Log.Write);
        _prayers = new PrayerScheduleService(_myQuran, log: Log.Write);
        _ics = new IcsCalendarProvider(_http, () => _settings.GoogleIcalUrl, () => _settings.DefaultMeetingReminderMinutes, Log.Write);
        _agenda = CreateAgenda();
        _state.City = _settings.GetCity();

        _adhan = new AdhanPlayer();
        _adhan.PlayingChanged += () => _state.AdhanPlaying = _adhan.IsPlaying;

        _notch = new NotchWindow(_state, this);
        _notch.Show();
        _tray = new TrayIcon(this, () => _notch.ExpandPanel(), TestNotify, Shutdown,
            () => _settings.AdhanMuted, SetAdhanMuted, () => _adhan.IsPlaying);

        StartTimer(TimeSpan.FromSeconds(1), ClockTick);
        StartTimer(TimeSpan.FromSeconds(2), UpdateIdleVisibility);
        StartTimer(TimeSpan.FromSeconds(20), () => _ = ReminderTickAsync());
        StartTimer(TimeSpan.FromSeconds(60), () => _ = RefreshCalendarAsync());
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;

        UpdateIdleVisibility();
        RefreshNow();

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) _ = ShowDemoAsync();
        else if (e.Args.Contains("--test-notify", StringComparer.OrdinalIgnoreCase)) _ = ShowAfterDelayAsync(TestNotify);
        if (e.Args.Contains("--test-adhan", StringComparer.OrdinalIgnoreCase)) _ = ShowAfterDelayAsync(TestAdhan);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex is not null)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
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

        bool autostart;
        try { autostart = Autostart.IsEnabled; }
        catch (Exception ex) { Log.Error("Autostart", ex); autostart = _settings.StartWithWindows; }

        _settingsWindow = new SettingsWindow(Clone(_settings), Clone(_ui), autostart, (q, ct) => _myQuran.SearchCitiesAsync(q, ct));
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

    public void RefreshNow()
    {
        _scheduleKey = default; // force schedule reload
        _ = ReminderTickAsync();
        _ = RefreshCalendarAsync();
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
        _ui = Clone(ui);
        SaveSettings();
        SaveUi();
        try { Autostart.Set(startWithWindows); }
        catch (Exception ex) { Log.Error("Autostart", ex); }
        if (_adhan.IsPlaying && (_settings.AdhanMuted || !_settings.AdhanEnabled)) _adhan.Stop();

        _agenda = CreateAgenda();
        _state.City = _settings.GetCity();
        _idleVisible = null;
        UpdateIdleVisibility();
        RefreshNow();
    }

    private AgendaService CreateAgenda()
    {
        var providers = new List<ICalendarProvider>();
        if (_settings.OutlookEnabled) providers.Add(new OutlookCalendarProvider(() => _settings.DefaultMeetingReminderMinutes, Log.Write));
        providers.Add(_ics);
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

    private async Task ReminderTickAsync()
    {
        try
        {
            await EnsureScheduleAsync();
            var now = DateTime.Now;
            // Without a schedule, still deliver meeting reminders via an empty prayer list.
            var today = _today ?? new DaySchedule(DateOnly.FromDateTime(now), _settings.GetCity(), Array.Empty<PrayerTime>());
            foreach (var reminder in _scheduler.Due(now, today, _events, _settings))
                Deliver(reminder);
        }
        catch (Exception ex)
        {
            Log.Error("Pengingat", ex);
        }
    }

    private async Task EnsureScheduleAsync()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var city = _settings.GetCity();
        var key = (date, city.Id);
        if (_scheduleBusy || (key == _scheduleKey && _today is not null && _tomorrow is not null)) return;

        _scheduleBusy = true;
        try
        {
            var today = await _prayers.GetDayAsync(city, date, _shutdown.Token);
            var tomorrow = await _prayers.GetDayAsync(city, date.AddDays(1), _shutdown.Token);
            if (_settings.GetCity().Id != city.Id) return; // city changed meanwhile; next tick reloads

            _today = today;
            _tomorrow = tomorrow;
            _scheduleKey = key;
            _state.Today = today;
            _state.ScheduleAvailable = today is not null;
            _state.City = today?.City ?? city;
            ClockTick();
            _ = PrefetchAsync(city);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("Jadwal", ex);
            _state.ScheduleAvailable = _today is not null;
        }
        finally
        {
            _scheduleBusy = false;
        }
    }

    private async Task PrefetchAsync(City city)
    {
        try { await _prayers.PrefetchAsync(city, _shutdown.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("Prefetch", ex); }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshCalendarAsync()
    {
        if (_calendarBusy) return;
        _calendarBusy = true;
        _state.CalendarLoading = true;
        try
        {
            var now = DateTime.Now;
            var from = now.AddHours(-1);
            var to = now.Date.AddDays(2); // end of tomorrow
            var agenda = _agenda;
            // Run off the UI thread: providers may do blocking work (process lookup, COM thread start).
            var events = await Task.Run(() => agenda.GetEventsAsync(from, to, _shutdown.Token));
            _events = events;

            now = DateTime.Now;
            var endOfToday = now.Date.AddDays(1);
            _state.Agenda = events.Where(e => e.End > now && e.Start < endOfToday).ToList();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("Kalender", ex);
        }
        finally
        {
            _state.CalendarLoading = false;
            _calendarBusy = false;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(RefreshNow);
    }

    private void OnTimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshNow);

    // ---- demo / test ----

    /// <summary>Shows a reminder and, for prayer-time reminders, sounds the adzan (PlayFor checks the settings).</summary>
    private void Deliver(Reminder reminder)
    {
        _notch.ShowReminder(reminder);
        if (reminder is { Kind: ReminderKind.PrayerNow, Prayer: { } p }) _adhan.PlayFor(p, _settings);
    }

    private void TestNotify()
    {
        var now = DateTime.Now;
        Deliver(new Reminder($"test|{Guid.NewGuid():N}", ReminderKind.PrayerNow, "Waktu Ashar",
            $"Sudah masuk waktu Ashar untuk {_state.City.DisplayName}", now.ToString("HH:mm"), null, now, Prayer.Ashar));
    }

    private void TestAdhan()
    {
        var settings = Clone(_settings);
        settings.AdhanEnabled = true;
        settings.AdhanMuted = false;
        settings.AdhanPrayers ??= new();
        settings.AdhanPrayers[Prayer.Dzuhur] = true;
        _adhan.PlayFor(Prayer.Dzuhur, settings);
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
        _notch.ShowReminder(new Reminder($"demo|soon|{id}", ReminderKind.PrayerSoon, "10 menit lagi Ashar",
            "Bersiap untuk sholat Ashar", soon.ToString("HH:mm"), null, now));
        Deliver(new Reminder($"demo|now|{id}", ReminderKind.PrayerNow, "Waktu Ashar",
            $"Sudah masuk waktu Ashar untuk {_state.City.DisplayName}", now.ToString("HH:mm"), null, now.AddSeconds(1), Prayer.Ashar));
        var start = now.AddMinutes(5);
        _notch.ShowReminder(new Reminder($"demo|meeting|{id}", ReminderKind.Meeting, "Weekly sync",
            $"{start:HH:mm}–{start.AddMinutes(30):HH:mm} · mulai 5 menit lagi · Microsoft Teams", start.ToString("HH:mm"),
            "https://teams.microsoft.com/l/meetup-join/demo", now.AddSeconds(2)));
    });
}
