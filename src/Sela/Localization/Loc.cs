using System.ComponentModel;
using System.Globalization;
using Sela.Core;

namespace Sela.Localization;

/// <summary>
/// UI strings in Indonesian and English, switchable at runtime.
/// <list type="bullet">
/// <item>XAML: <c>xmlns:l="clr-namespace:Sela.Localization"</c> then
/// <c>Text="{Binding [Settings_Title], Source={x:Static l:Loc.Instance}}"</c>. Bindings refresh when the language changes.</item>
/// <item>Code: <c>Loc.T("Notch_Next")</c>, or <c>Loc.F("Notch_InMinutes", n)</c> for <see cref="string.Format(IFormatProvider, string, object[])"/>.
/// Code that builds text itself must re-run on <see cref="LanguageChanged"/>.</item>
/// <item>Keys: <c>Area_Name</c> (letters, digits, underscore). Each area owns one partial file with its table:
/// Loc.Notch.cs (notch window), Loc.Settings.cs (settings window), Loc.Shell.cs (tray, app, shared).</item>
/// </list>
/// A missing key shows the key itself, so gaps are visible; it is logged once per key.
/// </summary>
public sealed partial class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private AppLanguage _language = AppLanguage.Indonesian;
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            Culture = AppLanguages.Culture(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Culture for dates and numbers in the current language (id-ID or en-US).</summary>
    public CultureInfo Culture { get; private set; } = AppLanguages.Culture(AppLanguage.Indonesian);

    /// <summary>Raised on the UI thread after <see cref="Language"/> changes.</summary>
    public event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Optional sink for missing-key reports (the shell points it at Log.Write).</summary>
    public static Action<string>? MissingKeyLog { get; set; }

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, T(key), args);

    private string Get(string key)
    {
        foreach (var table in Tables)
            if (table.TryGetValue(key, out var text))
                return _language == AppLanguage.English ? text.En : text.Id;
        if (_reportedMissing.Add(key)) MissingKeyLog?.Invoke($"Loc: missing key '{key}'");
        return key;
    }

    // A property, not a field: static field initializers in different partial files run in an
    // unspecified order, so a field here could capture the tables before they exist.
    private static IReadOnlyDictionary<string, (string Id, string En)>[] Tables =>
        [ShellStrings, NotchStrings, SettingsStrings];

    /// <summary>All keys, for checks that every key used exists.</summary>
    public static IEnumerable<string> Keys => Tables.SelectMany(t => t.Keys);
}
