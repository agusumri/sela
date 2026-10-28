using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sela.Core;
using Sela.Localization;

namespace Sela.Views;

public partial class SettingsWindow : Window
{
    private const int MinQueryLength = 2;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly Prayer[] AllPrayers = Enum.GetValues<Prayer>();

    private readonly SelaSettings _settings;
    private readonly UiSettings _ui;
    private readonly CitySearch _searchCities;
    private readonly Dictionary<Prayer, CheckBox> _prayerBoxes;
    private CancellationTokenSource? _searchCts;
    private bool _syncingSecret;
    private readonly Dictionary<Prayer, CheckBox> _adhanBoxes;
    private IAdhanPlayer? _adhan;
    private bool? _previewSubuh; // which row started the current preview

    /// <param name="settings">A copy the window may edit; the shell persists it on Saved.</param>
    /// <param name="startWithWindows">Current autostart state read from the registry.</param>
    public SettingsWindow(SelaSettings settings, UiSettings ui, bool startWithWindows, CitySearch searchCities)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(searchCities);

        InitializeComponent();

        // Work on copies so Batal never leaks edits back to the caller.
        _settings = Clone(settings);
        _ui = new UiSettings { NotchAlwaysVisible = ui.NotchAlwaysVisible };
        _searchCities = searchCities;
        Settings = _settings;
        Ui = _ui;
        StartWithWindows = startWithWindows;
        SearchCities = searchCities;

        _prayerBoxes = new()
        {
            [Prayer.Imsak] = PImsak, [Prayer.Subuh] = PSubuh, [Prayer.Terbit] = PTerbit, [Prayer.Dhuha] = PDhuha,
            [Prayer.Dzuhur] = PDzuhur, [Prayer.Ashar] = PAshar, [Prayer.Maghrib] = PMaghrib, [Prayer.Isya] = PIsya,
        };

        _adhanBoxes = new()
        {
            [Prayer.Subuh] = ASubuh, [Prayer.Dzuhur] = ADzuhur, [Prayer.Ashar] = AAshar,
            [Prayer.Maghrib] = AMaghrib, [Prayer.Isya] = AIsya,
        };

        _openingLanguage = Loc.Instance.Language;

        LoadLanguage();
        LoadFromSettings(startWithWindows);
        LoadCalculation();
        LoadAdhan();
        LoadHabits();
        ApplyLanguageTexts();

        Loc.Instance.LanguageChanged += ApplyLanguageTexts;
        PreviewKeyDown += OnWindowKeyDown;
        Loaded += (_, _) =>
        {
            ((Storyboard)FindResource("Enter")).Begin(this);
            SearchBox.Focus();
        };
        Closed += (_, _) =>
        {
            Loc.Instance.LanguageChanged -= ApplyLanguageTexts;
            CancelSearch();
            StopPreview();
            Adhan = null; // unsubscribe from PlayingChanged
            // Batal / close: undo the live language preview. On Simpan the shell applies the saved value.
            if (!_saved) Loc.Instance.Language = _openingLanguage;
        };
    }

    // ---------- Language ----------

    private readonly AppLanguage _openingLanguage;
    private bool _saved;
    private bool _loadingLanguage;

    private void LoadLanguage()
    {
        _loadingLanguage = true;
        (_settings.Language switch
        {
            AppLanguage.Indonesian => LangIndonesian,
            AppLanguage.English => LangEnglish,
            _ => LangSystem,
        }).IsChecked = true;
        _loadingLanguage = false;
    }

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        if (_loadingLanguage || _settings is null) return;
        _settings.Language = sender == LangIndonesian ? AppLanguage.Indonesian
            : sender == LangEnglish ? AppLanguage.English
            : null;
        Loc.Instance.Language = _settings.ResolveLanguage(); // live preview; raises LanguageChanged
    }

    /// <summary>Re-renders every text built in code (XAML bindings refresh by themselves).</summary>
    private void ApplyLanguageTexts()
    {
        var lang = Loc.Instance.Language;
        foreach (var (prayer, box) in _prayerBoxes) box.Content = Names.Prayer(prayer, lang);
        foreach (var (prayer, box) in _adhanBoxes) box.Content = Names.Prayer(prayer, lang);

        HPagiName.Text = Names.Habit(Habit.DzikirPagi, lang);
        HSholawatName.Text = Names.Habit(Habit.Sholawat, lang);
        HPetangName.Text = Names.Habit(Habit.DzikirPetang, lang);
        foreach (var (habit, box) in HabitBoxes) AutomationProperties.SetName(box, Names.Habit(habit, lang));

        UpdateCityTexts();
        UpdateFileLabels();
        UpdatePreviewIcons();
        UpdateRevealLabel();
        RefreshMethodLabels();
        if (_statusText is not null) SearchStatusText.Text = _statusText();
    }

    // ---------- Calculation (worldwide places) ----------

    /// <summary>Aladhan method ids with their proper names (same in both languages).</summary>
    private static readonly (int Id, string Name)[] AladhanMethods =
    [
        (3, "Muslim World League"),
        (2, "Islamic Society of North America (ISNA)"),
        (5, "Egyptian General Authority of Survey"),
        (4, "Umm Al-Qura University, Makkah"),
        (1, "University of Islamic Sciences, Karachi"),
        (7, "Institute of Geophysics, University of Tehran"),
        (0, "Shia Ithna-Ashari (Jafari)"),
        (8, "Gulf Region"),
        (9, "Kuwait"),
        (10, "Qatar"),
        (11, "Majlis Ugama Islam Singapura"),
        (12, "Union des Organisations Islamiques de France"),
        (13, "Diyanet İşleri Başkanlığı, Turkey"),
        (14, "Spiritual Administration of Muslims of Russia"),
        (15, "Moonsighting Committee Worldwide"),
        (16, "Dubai"),
        (17, "JAKIM (Malaysia)"),
        (18, "Tunisia"),
        (19, "Algeria"),
        (20, "Kemenag (Indonesia)"),
        (21, "Morocco"),
        (22, "Comunidade Islâmica de Lisboa"),
        (23, "Jordan"),
    ];

    private sealed class MethodOption(int? id, string label) : System.ComponentModel.INotifyPropertyChanged
    {
        private string _label = label;
        public int? Id { get; } = id;
        public string Label
        {
            get => _label;
            set { _label = value; PropertyChanged?.Invoke(this, new(nameof(Label))); }
        }
        public override string ToString() => Label; // used by screen readers / text search
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private void LoadCalculation()
    {
        var options = new List<MethodOption> { new(null, Loc.T("Settings_MethodAuto")) };
        options.AddRange(AladhanMethods.Select(m => new MethodOption(m.Id, m.Name)));
        MethodCombo.ItemsSource = options;
        // An unknown stored id falls back to Automatic rather than silently picking a different method.
        MethodCombo.SelectedItem = options.FirstOrDefault(o => o.Id == _settings.PrayerMethod) ?? options[0];
        HanafiToggle.IsChecked = _settings.AsrHanafi;
    }

    private void RefreshMethodLabels()
    {
        if (MethodCombo.ItemsSource is IEnumerable<MethodOption> options && options.FirstOrDefault() is { Id: null } auto)
            auto.Label = Loc.T("Settings_MethodAuto");
    }

    public SelaSettings Settings { get; }
    public UiSettings Ui { get; }
    public bool StartWithWindows { get; }
    public CitySearch SearchCities { get; }

    /// <summary>Raised when the user saves. Args: validated settings, ui settings, autostart.</summary>
    public event Action<SelaSettings, UiSettings, bool>? Saved;

    private void LoadFromSettings(bool startWithWindows)
    {
        foreach (var (prayer, box) in _prayerBoxes)
            box.IsChecked = _settings.PrayerEnabled.TryGetValue(prayer, out var on) && on;

        SelectSegment(PrayerSoonGroup, _settings.PrayerSoonMinutes, fallback: 10);
        SelectSegment(MeetingGroup, _settings.DefaultMeetingReminderMinutes, fallback: 10);

        OutlookToggle.IsChecked = _settings.OutlookEnabled;
        SetSecret(_settings.GoogleIcalUrl ?? string.Empty);

        AutostartToggle.IsChecked = startWithWindows;
        IdleToggle.IsChecked = _ui.NotchAlwaysVisible;
        UpdateToggle.IsChecked = _settings.UpdateCheckEnabled;
        SelectSegment(IdleScaleGroup, NotchScale.Normalize(_settings.NotchIdleScale), fallback: NotchScale.Default);

        UpdateGoogleValidation();
    }

    private static SelaSettings Clone(SelaSettings s) => new()
    {
        CityId = s.CityId,
        CityName = s.CityName,
        PrayerSoonMinutes = s.PrayerSoonMinutes,
        PrayerEnabled = new Dictionary<Prayer, bool>(s.PrayerEnabled),
        OutlookEnabled = s.OutlookEnabled,
        GoogleIcalUrl = s.GoogleIcalUrl,
        DefaultMeetingReminderMinutes = s.DefaultMeetingReminderMinutes,
        StartWithWindows = s.StartWithWindows,
        AdhanEnabled = s.AdhanEnabled,
        AdhanMuted = s.AdhanMuted,
        AdhanPrayers = s.AdhanPrayers is null ? new() : new Dictionary<Prayer, bool>(s.AdhanPrayers),
        AdhanVolume = s.AdhanVolume,
        AdhanFile = s.AdhanFile,
        AdhanSubuhFile = s.AdhanSubuhFile,
        HabitRemindersEnabled = s.HabitRemindersEnabled,
        HabitEnabled = s.HabitEnabled is null ? new() : new Dictionary<Habit, bool>(s.HabitEnabled),
        DailyHadithCard = s.DailyHadithCard,
        PrayerDhikr = s.PrayerDhikr,
        Language = s.Language,
        CityLatitude = s.CityLatitude,
        CityLongitude = s.CityLongitude,
        CityTimeZone = s.CityTimeZone,
        CityCountry = s.CityCountry,
        CityRegion = s.CityRegion,
        PrayerMethod = s.PrayerMethod,
        AsrHanafi = s.AsrHanafi,
        UpdateCheckEnabled = s.UpdateCheckEnabled,
        SkippedUpdateVersion = s.SkippedUpdateVersion,
        NotchIdleScale = s.NotchIdleScale,
    };

    // ---------- Ibadah harian ----------

    private Dictionary<Habit, CheckBox> HabitBoxes => new()
    {
        [Habit.DzikirPagi] = HDzikirPagi, [Habit.Sholawat] = HSholawat, [Habit.DzikirPetang] = HDzikirPetang,
    };

    private void LoadHabits()
    {
        HabitToggle.IsChecked = _settings.HabitRemindersEnabled;
        foreach (var (habit, box) in HabitBoxes)
            box.IsChecked = _settings.HabitEnabled.TryGetValue(habit, out var on) && on;
        HadithToggle.IsChecked = _settings.DailyHadithCard;
        PrayerDhikrToggle.IsChecked = _settings.PrayerDhikr;
        UpdateHabitEnabled();
    }

    private void SaveHabits()
    {
        _settings.HabitRemindersEnabled = HabitToggle.IsChecked == true;
        foreach (var (habit, box) in HabitBoxes)
            _settings.HabitEnabled[habit] = box.IsChecked == true;
        _settings.DailyHadithCard = HadithToggle.IsChecked == true;
        _settings.PrayerDhikr = PrayerDhikrToggle.IsChecked == true;
    }

    private void OnHabitToggled(object sender, RoutedEventArgs e) => UpdateHabitEnabled();

    private void UpdateHabitEnabled()
    {
        if (HabitOptions is null) return; // fires during InitializeComponent
        var on = HabitToggle.IsChecked == true;
        HabitOptions.IsEnabled = on;
        HabitOptions.Opacity = on ? 1 : 0.4;
    }

    // ---------- Adzan ----------

    /// <summary>Set by the shell after construction; preview is a no-op when null.</summary>
    public IAdhanPlayer? Adhan
    {
        get => _adhan;
        set
        {
            if (_adhan is not null) _adhan.PlayingChanged -= OnAdhanPlayingChanged;
            _adhan = value;
            if (_adhan is not null) _adhan.PlayingChanged += OnAdhanPlayingChanged;
            UpdatePreviewIcons();
        }
    }

    private void LoadAdhan()
    {
        AdhanToggle.IsChecked = _settings.AdhanEnabled;
        AdhanMuteToggle.IsChecked = _settings.AdhanMuted;
        foreach (var (prayer, box) in _adhanBoxes)
            box.IsChecked = _settings.AdhanPrayers.TryGetValue(prayer, out var on) && on;
        VolumeSlider.Value = Math.Clamp(_settings.AdhanVolume, 0, 100);
        UpdateVolumeText();
        UpdateFileLabels();
        UpdateAdhanEnabled();
    }

    private void SaveAdhan()
    {
        _settings.AdhanEnabled = AdhanToggle.IsChecked == true;
        _settings.AdhanMuted = AdhanMuteToggle.IsChecked == true;
        foreach (var (prayer, box) in _adhanBoxes)
            _settings.AdhanPrayers[prayer] = box.IsChecked == true;
        _settings.AdhanVolume = CurrentVolume;
        // AdhanFile / AdhanSubuhFile are written directly by the pickers.
    }

    private int CurrentVolume => (int)Math.Round(VolumeSlider.Value);

    private void OnAdhanToggled(object sender, RoutedEventArgs e) => UpdateAdhanEnabled();

    private void UpdateAdhanEnabled()
    {
        if (AdhanOptions is null) return; // fires during InitializeComponent
        var on = AdhanToggle.IsChecked == true;
        AdhanOptions.IsEnabled = on;
        AdhanOptions.Opacity = on ? 1 : 0.4;
        if (!on) StopPreview();
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateVolumeText();

    private void UpdateVolumeText()
    {
        if (VolumeText is null) return;
        VolumeText.Text = $"{CurrentVolume}%";
        AutomationProperties.SetHelpText(VolumeSlider, VolumeText.Text);
    }

    private void UpdateFileLabels()
    {
        AdhanFileText.Text = FileLabel(_settings.AdhanFile);
        AdhanFileText.ToolTip = _settings.AdhanFile;
        AdhanResetButton.IsEnabled = _settings.AdhanFile is not null;

        SubuhFileText.Text = FileLabel(_settings.AdhanSubuhFile);
        SubuhFileText.ToolTip = _settings.AdhanSubuhFile;
        SubuhResetButton.IsEnabled = _settings.AdhanSubuhFile is not null;
    }

    private static string FileLabel(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Loc.T("Settings_Default") : System.IO.Path.GetFileName(path);

    private string? PickAudio(string title)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title, Filter = Loc.T("Settings_AudioFilter"), CheckFileExists = true,
        };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void OnPickAdhanFile(object sender, RoutedEventArgs e)
    {
        if (PickAudio(Loc.T("Settings_PickAdhanTitle")) is not { } file) return;
        StopPreview();
        _settings.AdhanFile = file;
        UpdateFileLabels();
    }

    private void OnPickSubuhFile(object sender, RoutedEventArgs e)
    {
        if (PickAudio(Loc.T("Settings_PickSubuhTitle")) is not { } file) return;
        StopPreview();
        _settings.AdhanSubuhFile = file;
        UpdateFileLabels();
    }

    private void OnResetAdhanFile(object sender, RoutedEventArgs e)
    {
        StopPreview();
        _settings.AdhanFile = null;
        UpdateFileLabels();
    }

    private void OnResetSubuhFile(object sender, RoutedEventArgs e)
    {
        StopPreview();
        _settings.AdhanSubuhFile = null;
        UpdateFileLabels();
    }

    private void OnPreviewAdhan(object sender, RoutedEventArgs e) => TogglePreview(subuh: false);
    private void OnPreviewSubuh(object sender, RoutedEventArgs e) => TogglePreview(subuh: true);

    private void TogglePreview(bool subuh)
    {
        if (_adhan is null) return;
        var wasThisRow = _adhan.IsPlaying && _previewSubuh == subuh;
        _adhan.Stop();
        if (wasThisRow)
        {
            _previewSubuh = null;
        }
        else
        {
            _previewSubuh = subuh;
            _adhan.Preview(subuh ? _settings.AdhanSubuhFile : _settings.AdhanFile, subuh, CurrentVolume);
        }
        UpdatePreviewIcons();
    }

    private void StopPreview()
    {
        if (_adhan?.IsPlaying == true) _adhan.Stop();
        _previewSubuh = null;
        UpdatePreviewIcons();
    }

    private void OnAdhanPlayingChanged()
    {
        if (Dispatcher.CheckAccess()) UpdatePreviewIcons();
        else Dispatcher.BeginInvoke(UpdatePreviewIcons);
    }

    private void UpdatePreviewIcons()
    {
        var playing = _adhan?.IsPlaying == true;
        if (!playing) _previewSubuh = null;
        SetPreviewIcon(AdhanPreview, playing && _previewSubuh == false, "Settings_PlayAdhanSample", "Settings_StopAdhanSample");
        SetPreviewIcon(SubuhPreview, playing && _previewSubuh == true, "Settings_PlaySubuhSample", "Settings_StopSubuhSample");
        AdhanPreview.IsEnabled = SubuhPreview.IsEnabled = _adhan is not null;
    }

    private static void SetPreviewIcon(Button button, bool playing, string playKey, string stopKey)
    {
        button.Content = playing ? "\uE71A" : "\uE768";
        AutomationProperties.SetName(button, Loc.T(playing ? stopKey : playKey));
        button.ToolTip = Loc.T(playing ? "Settings_Stop" : "Settings_PlaySample");
    }

    // ---------- Segmented controls ----------

    private static void SelectSegment(Panel group, int minutes, int fallback)
    {
        var buttons = group.Children.OfType<RadioButton>().ToList();
        var match = buttons.FirstOrDefault(b => SegmentValue(b) == minutes)
                    ?? buttons.FirstOrDefault(b => SegmentValue(b) == fallback);
        if (match is not null) match.IsChecked = true;
    }

    private static int SelectedSegment(Panel group, int fallback) =>
        group.Children.OfType<RadioButton>().FirstOrDefault(b => b.IsChecked == true) is { } b ? SegmentValue(b) : fallback;

    private static int SegmentValue(RadioButton b) => int.Parse((string)b.Tag);

    // ---------- City search ----------

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        CancelSearch();
        var query = SearchBox.Text.Trim();
        if (query.Length < MinQueryLength)
        {
            ShowResults(null);
            ShowStatus(null);
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        try
        {
            await Task.Delay(SearchDebounce, cts.Token);
            ShowStatus(() => Loc.T("Settings_Searching"), spinning: true);
            var results = await _searchCities(query, cts.Token);
            if (cts.IsCancellationRequested) return;

            ShowResults(results);
            ShowStatus(results.Count == 0 ? () => Loc.T("Settings_NotFound") : null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by newer input.
        }
        catch (Exception)
        {
            if (cts.IsCancellationRequested) return;
            ShowResults(null);
            ShowStatus(() => Loc.T("Settings_SearchFailed"));
        }
    }

    /// <summary>One row in the results list: Kemenag → province + chip; world → "Region · Country".</summary>
    private sealed record CityResult(City City, string Title, string Subtitle, bool IsKemenag);

    private static CityResult ToResult(City c)
    {
        string subtitle;
        if (c.IsWorld)
        {
            var parts = new[] { c.Province, c.Country }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim());
            subtitle = string.Join(" · ", parts);
        }
        else
        {
            subtitle = string.IsNullOrWhiteSpace(c.Province)
                ? string.Empty
                : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(c.Province.Trim().ToLowerInvariant());
        }
        return new CityResult(c, c.DisplayName, subtitle, !c.IsWorld);
    }

    private void UpdateCityTexts()
    {
        var city = _settings.GetCity();
        CurrentCityText.Text = city.DisplayName;
        CurrentCityText.ToolTip = city.DisplayName;
        SourceNote.Text = Loc.T(city.IsWorld ? "Settings_SourceAladhan" : "Settings_SourceKemenag");
        CalculationPanel.Visibility = city.IsWorld ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    /// <summary>Builds the current status line; kept so it can be re-rendered on a language switch.</summary>
    private Func<string>? _statusText;

    private void ShowStatus(Func<string>? text, bool spinning = false)
    {
        _statusText = text;
        var spin = (Storyboard)FindResource("Spin");
        if (text is null)
        {
            SearchStatus.Visibility = Visibility.Collapsed;
            spin.Stop(this);
            return;
        }
        SearchStatusText.Text = text();
        SearchStatus.Visibility = Visibility.Visible;
        Spinner.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;
        if (spinning) spin.Begin(this, isControllable: true);
        else spin.Stop(this);
    }

    private void ShowResults(IReadOnlyList<City>? results)
    {
        ResultsList.ItemsSource = results?.Select(ToResult).ToList();
        ResultsList.Visibility = results is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.Items.Count > 0)
        {
            SelectCity(((CityResult)ResultsList.Items[0]).City);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Space) && ResultsList.SelectedItem is CityResult r)
        {
            SelectCity(r.City);
            e.Handled = true;
        }
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: CityResult r }) SelectCity(r.City);
    }

    private void SelectCity(City city)
    {
        _settings.SetCity(city);
        UpdateCityTexts();

        CancelSearch();
        SearchBox.TextChanged -= OnSearchTextChanged;
        SearchBox.Clear();
        SearchBox.TextChanged += OnSearchTextChanged;
        SearchPlaceholder.Visibility = Visibility.Visible;
        ShowResults(null);
        ShowStatus(() => Loc.F("Settings_CityChanged", city.DisplayName));
        SearchBox.Focus();
    }

    // ---------- Google iCal URL ----------

    private string Secret => RevealToggle.IsChecked == true ? GoogleText.Text : GooglePassword.Password;

    private void SetSecret(string value)
    {
        _syncingSecret = true;
        GooglePassword.Password = value;
        GoogleText.Text = value;
        _syncingSecret = false;
        GooglePlaceholder.Visibility = value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGooglePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingSecret) return;
        GooglePlaceholder.Visibility = GooglePassword.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateGoogleValidation();
    }

    private void OnGoogleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSecret) return;
        GooglePlaceholder.Visibility = GoogleText.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateGoogleValidation();
    }

    private void OnRevealChanged(object sender, RoutedEventArgs e)
    {
        var reveal = RevealToggle.IsChecked == true;
        // Copy from the field that was active before the toggle.
        SetSecret(reveal ? GooglePassword.Password : GoogleText.Text);

        GoogleText.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        GooglePassword.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        RevealToggle.Content = reveal ? "\uED1A" : "\uE7B3";
        UpdateRevealLabel();

        if (!reveal) GoogleText.Clear(); // don't keep plaintext in a hidden TextBox
        else GooglePassword.Clear();
    }

    private void UpdateRevealLabel()
    {
        var label = Loc.T(RevealToggle.IsChecked == true ? "Settings_HideAddress" : "Settings_ShowAddress");
        AutomationProperties.SetName(RevealToggle, label);
        RevealToggle.ToolTip = label;
    }

    private bool IsGoogleValid(out string normalized)
    {
        var raw = Secret.Trim();
        normalized = raw;
        if (raw.Length == 0) return true; // empty = Google disabled
        return IcsCalendarProvider.NormalizeUrl(raw) is not null;
    }

    private void UpdateGoogleValidation()
    {
        var valid = IsGoogleValid(out _);
        GoogleError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        GoogleField.BorderBrush = valid ? (Brush)FindResource("FieldLine") : (Brush)FindResource("Danger");
        SaveButton.IsEnabled = valid;
    }

    private void OnFieldFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var field = sender == SearchBox ? SearchField : GoogleField;
        if (field == GoogleField && !IsGoogleValid(out _)) return;
        field.BorderBrush = (Brush)FindResource(field == SearchField ? "Green" : "Blue");
    }

    private void OnFieldBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender == SearchBox) SearchField.BorderBrush = (Brush)FindResource("FieldLine");
        else UpdateGoogleValidation();
    }

    // ---------- Footer ----------

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!IsGoogleValid(out var google))
        {
            UpdateGoogleValidation();
            return;
        }

        foreach (var p in AllPrayers)
            _settings.PrayerEnabled[p] = _prayerBoxes[p].IsChecked == true;
        _settings.PrayerSoonMinutes = SelectedSegment(PrayerSoonGroup, 10);
        _settings.DefaultMeetingReminderMinutes = SelectedSegment(MeetingGroup, 10);
        _settings.OutlookEnabled = OutlookToggle.IsChecked == true;
        _settings.GoogleIcalUrl = google.Length == 0 ? null : google;

        var autostart = AutostartToggle.IsChecked == true;
        _settings.StartWithWindows = autostart;
        _ui.NotchAlwaysVisible = IdleToggle.IsChecked == true;
        _settings.UpdateCheckEnabled = UpdateToggle.IsChecked == true;
        _settings.NotchIdleScale = NotchScale.Normalize(SelectedSegment(IdleScaleGroup, NotchScale.Default));
        SaveAdhan();
        SaveHabits();
        // Language is written by the segmented control; calculation only matters for world places but is kept either way.
        _settings.PrayerMethod = (MethodCombo.SelectedItem as MethodOption)?.Id;
        _settings.AsrHanafi = HanafiToggle.IsChecked == true;

        _saved = true;
        Saved?.Invoke(_settings, _ui, autostart);
        Close();
    }
}
