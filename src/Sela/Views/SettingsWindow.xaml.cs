using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sela.Core;

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

        LoadFromSettings(startWithWindows);
        LoadAdhan();
        UpdatePreviewIcons();

        PreviewKeyDown += OnWindowKeyDown;
        Loaded += (_, _) =>
        {
            ((Storyboard)FindResource("Enter")).Begin(this);
            SearchBox.Focus();
        };
        Closed += (_, _) =>
        {
            CancelSearch();
            StopPreview();
            Adhan = null; // unsubscribe from PlayingChanged
        };
    }

    public SelaSettings Settings { get; }
    public UiSettings Ui { get; }
    public bool StartWithWindows { get; }
    public CitySearch SearchCities { get; }

    /// <summary>Raised when the user saves. Args: validated settings, ui settings, autostart.</summary>
    public event Action<SelaSettings, UiSettings, bool>? Saved;

    private void LoadFromSettings(bool startWithWindows)
    {
        CurrentCityText.Text = _settings.GetCity().DisplayName;

        foreach (var (prayer, box) in _prayerBoxes)
            box.IsChecked = _settings.PrayerEnabled.TryGetValue(prayer, out var on) && on;

        SelectSegment(PrayerSoonGroup, _settings.PrayerSoonMinutes, fallback: 10);
        SelectSegment(MeetingGroup, _settings.DefaultMeetingReminderMinutes, fallback: 10);

        OutlookToggle.IsChecked = _settings.OutlookEnabled;
        SetSecret(_settings.GoogleIcalUrl ?? string.Empty);

        AutostartToggle.IsChecked = startWithWindows;
        IdleToggle.IsChecked = _ui.NotchAlwaysVisible;

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
    };

    // ---------- Adzan ----------

    private const string AudioFilter = "Audio (*.mp3;*.wav;*.wma;*.m4a;*.aac)|*.mp3;*.wav;*.wma;*.m4a;*.aac";

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
        string.IsNullOrWhiteSpace(path) ? "Bawaan" : System.IO.Path.GetFileName(path);

    private string? PickAudio(string title)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = AudioFilter, CheckFileExists = true };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void OnPickAdhanFile(object sender, RoutedEventArgs e)
    {
        if (PickAudio("Pilih suara adzan") is not { } file) return;
        StopPreview();
        _settings.AdhanFile = file;
        UpdateFileLabels();
    }

    private void OnPickSubuhFile(object sender, RoutedEventArgs e)
    {
        if (PickAudio("Pilih suara adzan Subuh") is not { } file) return;
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
        SetPreviewIcon(AdhanPreview, playing && _previewSubuh == false, "adzan");
        SetPreviewIcon(SubuhPreview, playing && _previewSubuh == true, "adzan Subuh");
        AdhanPreview.IsEnabled = SubuhPreview.IsEnabled = _adhan is not null;
    }

    private static void SetPreviewIcon(Button button, bool playing, string what)
    {
        button.Content = playing ? "\uE71A" : "\uE768";
        var label = playing ? $"Hentikan contoh {what}" : $"Putar contoh {what}";
        AutomationProperties.SetName(button, label);
        button.ToolTip = playing ? "Hentikan" : "Putar contoh";
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
            ShowStatus("Mencari…", spinning: true);
            var results = await _searchCities(query, cts.Token);
            if (cts.IsCancellationRequested) return;

            ShowResults(results);
            ShowStatus(results.Count == 0 ? "Kota tidak ditemukan" : null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by newer input.
        }
        catch (Exception)
        {
            if (cts.IsCancellationRequested) return;
            ShowResults(null);
            ShowStatus("Gagal mencari. Cek koneksi internet.");
        }
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private void ShowStatus(string? text, bool spinning = false)
    {
        var spin = (Storyboard)FindResource("Spin");
        if (text is null)
        {
            SearchStatus.Visibility = Visibility.Collapsed;
            spin.Stop(this);
            return;
        }
        SearchStatusText.Text = text;
        SearchStatus.Visibility = Visibility.Visible;
        Spinner.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;
        if (spinning) spin.Begin(this, isControllable: true);
        else spin.Stop(this);
    }

    private void ShowResults(IReadOnlyList<City>? results)
    {
        ResultsList.ItemsSource = results;
        ResultsList.Visibility = results is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.Items.Count > 0)
        {
            SelectCity((City)ResultsList.Items[0]);
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
        if ((e.Key == Key.Enter || e.Key == Key.Space) && ResultsList.SelectedItem is City city)
        {
            SelectCity(city);
            e.Handled = true;
        }
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: City city }) SelectCity(city);
    }

    private void SelectCity(City city)
    {
        _settings.CityId = city.Id;
        _settings.CityName = city.Name;
        CurrentCityText.Text = city.DisplayName;

        CancelSearch();
        SearchBox.TextChanged -= OnSearchTextChanged;
        SearchBox.Clear();
        SearchBox.TextChanged += OnSearchTextChanged;
        SearchPlaceholder.Visibility = Visibility.Visible;
        ShowResults(null);
        ShowStatus($"Lokasi diganti ke {city.DisplayName}");
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
        var label = reveal ? "Sembunyikan alamat" : "Tampilkan alamat";
        AutomationProperties.SetName(RevealToggle, label);
        RevealToggle.ToolTip = label;

        if (!reveal) GoogleText.Clear(); // don't keep plaintext in a hidden TextBox
        else GooglePassword.Clear();
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
        SaveAdhan();

        Saved?.Invoke(_settings, _ui, autostart);
        Close();
    }
}
