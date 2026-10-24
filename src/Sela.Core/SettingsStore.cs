using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sela.Core;

/// <summary>
/// JSON settings persistence. Missing/corrupt files yield defaults (corrupt file is kept as .bak).
/// Writes are atomic (temp file + move). Note: GoogleIcalUrl is stored in plain text here; Sela.Core is
/// platform-neutral, so any DPAPI protection must be applied by the Windows app layer.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? path = null) => Path = path ?? DefaultPath;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sela", "settings.json");

    public string Path { get; }

    public SelaSettings Load()
    {
        if (!File.Exists(Path)) return new SelaSettings();
        try
        {
            var text = File.ReadAllText(Path);
            var settings = JsonSerializer.Deserialize<SelaSettings>(text, Options)
                           ?? throw new JsonException("null settings");
            var defaults = new SelaSettings();
            settings.PrayerEnabled ??= defaults.PrayerEnabled;
            settings.AdhanPrayers ??= defaults.AdhanPrayers;
            settings.AdhanVolume = Math.Clamp(settings.AdhanVolume, 0, 100);
            // Files written before CityId existed carry a free-text CityName that no longer maps to an API id.
            bool hasCityId = System.Text.Json.Nodes.JsonNode.Parse(text) is System.Text.Json.Nodes.JsonObject o && o.ContainsKey(nameof(SelaSettings.CityId));
            if (!hasCityId || string.IsNullOrWhiteSpace(settings.CityId))
            {
                settings.CityId = DefaultCity.Jakarta.Id;
                settings.CityName = DefaultCity.Jakarta.Name;
            }
            settings.CityName ??= settings.CityId;
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            try { File.Copy(Path, Path + ".bak", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new SelaSettings();
        }
    }

    public void Save(SelaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SafeIo.WriteAllTextAtomic(Path, JsonSerializer.Serialize(settings, Options));
    }
}
