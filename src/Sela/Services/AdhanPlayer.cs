using System.IO;
using System.Media;
using System.Windows.Media;
using System.Windows.Threading;
using Sela.Core;

namespace Sela.Services;

/// <summary>Plays the adzan with WPF's MediaPlayer. Create and use on the UI thread only; never throws.</summary>
public sealed class AdhanPlayer : IAdhanPlayer
{
    private const long MaxFileBytes = 50L * 1024 * 1024;
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(6);
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".wma", ".m4a", ".aac" };

    private readonly HashSet<string> _rejectedLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _cap;
    private MediaPlayer? _player;
    private bool _isPlaying;

    public AdhanPlayer()
    {
        _cap = new DispatcherTimer { Interval = SafetyCap };
        _cap.Tick += (_, _) => Stop();
    }

    public bool IsPlaying => _isPlaying;
    public event Action? PlayingChanged;

    public void PlayFor(Prayer prayer, SelaSettings settings)
    {
        try
        {
            if (!settings.ShouldPlayAdhan(prayer)) return;
            bool subuh = prayer == Prayer.Subuh;
            Play(Resolve(subuh ? settings.AdhanSubuhFile : settings.AdhanFile, subuh), settings.AdhanVolume);
        }
        catch (Exception ex)
        {
            Log.Error("Adzan", ex);
        }
    }

    public void Preview(string? file, bool subuh, int volume)
    {
        try { Play(Resolve(file, subuh), volume); }
        catch (Exception ex) { Log.Error("Adzan (pratinjau)", ex); }
    }

    public void Stop()
    {
        _cap.Stop();
        var player = _player;
        _player = null;
        if (player is not null)
        {
            try
            {
                player.Stop();
                player.Close();
            }
            catch (Exception ex) { Log.Error("Adzan stop", ex); }
        }
        SetPlaying(false);
    }

    private void Play(string? path, int volume)
    {
        Stop(); // one playback at a time
        if (path is null)
        {
            Log.Write("Adzan: file audio tidak ada");
            SystemSounds.Exclamation.Play();
            return;
        }

        var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 100) / 100.0 };
        player.MediaOpened += (_, _) => { if (_player == player) SetPlaying(true); };
        player.MediaEnded += (_, _) => { if (_player == player) Stop(); };
        player.MediaFailed += (_, e) =>
        {
            if (_player != player) return;
            Log.Write($"Adzan gagal diputar: {e.ErrorException?.GetType().Name}: {e.ErrorException?.Message}");
            Stop();
        };
        _player = player;
        player.Open(new Uri(path, UriKind.Absolute));
        player.Play();
        _cap.Start();
    }

    /// <summary>User file if valid; else bundled (Subuh: bundled Subuh, then bundled regular); null if nothing exists.</summary>
    private string? Resolve(string? userFile, bool subuh)
    {
        if (!string.IsNullOrWhiteSpace(userFile))
        {
            if (IsValidUserFile(userFile)) return Path.GetFullPath(userFile);
            if (_rejectedLogged.Add(userFile)) Log.Write("Adzan: file pilihan tidak valid, memakai adzan bawaan");
        }
        if (subuh && Bundled("adhan-subuh.mp3") is { } s) return s;
        return Bundled("adhan.mp3");
    }

    private static bool IsValidUserFile(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists && AllowedExtensions.Contains(info.Extension) && info.Length <= MaxFileBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string? Bundled(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", name);
        return File.Exists(path) ? path : null;
    }

    private void SetPlaying(bool value)
    {
        if (_isPlaying == value) return;
        _isPlaying = value;
        try { PlayingChanged?.Invoke(); }
        catch (Exception ex) { Log.Error("Adzan event", ex); }
    }
}
