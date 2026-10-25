using System.IO;
using System.Security.Cryptography;
using System.Text;
using Sela.Core;

namespace Sela.Services;

/// <summary>
/// Last successfully fetched agenda, kept on disk so the Agenda tab has content offline.
/// Stored at %LOCALAPPDATA%\Sela\cache\agenda.dat, DPAPI-protected (CurrentUser) because it holds meeting
/// subjects and join links. Event content is never logged. Disabled instances (--mock) never touch disk.
/// </summary>
public sealed class AgendaCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Sela.AgendaCache.v1");

    private readonly string _path;
    private readonly bool _enabled;

    public AgendaCache(bool enabled, string? path = null)
    {
        _enabled = enabled;
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sela", "cache", "agenda.dat");
    }

    public void Save(IReadOnlyList<CalendarEvent> events, DateTimeOffset savedAt)
    {
        if (!_enabled) return;
        var temp = _path + ".tmp";
        try
        {
            var data = ProtectedData.Protect(AgendaCacheCodec.Serialize(events, savedAt), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temp, data);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Log.Write($"Cache agenda tidak tersimpan ({ex.GetType().Name})");
            TryDelete(temp);
        }
    }

    /// <summary>The cached events and when they were fetched; null when missing, unreadable or older than 48 h.</summary>
    public (IReadOnlyList<CalendarEvent> Events, DateTimeOffset SavedAt)? Load()
    {
        if (!_enabled) return null;
        try
        {
            if (!File.Exists(_path)) return null;
            var data = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            if (!AgendaCacheCodec.TryDeserialize(data, out var savedAt, out var events))
            {
                Log.Write("Cache agenda rusak; dihapus");
                TryDelete(_path);
                return null;
            }
            var age = DateTimeOffset.Now - savedAt;
            if (age > MaxAge || age < -MaxAge) return null; // stale (or clock went far backwards)
            return (events, savedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Log.Write($"Cache agenda diabaikan ({ex.GetType().Name})");
            TryDelete(_path);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
