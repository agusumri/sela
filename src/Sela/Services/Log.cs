using System.Globalization;
using System.IO;

namespace Sela.Services;

/// <summary>Append-only text log at %LOCALAPPDATA%\Sela\log.txt; rolls to log.old.txt at 1 MB. Never throws.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sela", "log.txt");

    public static void Write(string message)
    {
        try
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.txt"), overwrite: true);
                File.AppendAllText(FilePath, line);
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }

    public static void Error(string context, Exception ex) => Write($"{context}: {ex.GetType().Name}: {ex.Message}");
}
