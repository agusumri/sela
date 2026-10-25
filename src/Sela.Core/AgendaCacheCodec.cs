using System.Text.Json;

namespace Sela.Core;

/// <summary>
/// JSON form of the saved agenda (the app encrypts the bytes before writing them to disk).
/// Reading is defensive: anything malformed or out of bounds is rejected as a whole.
/// </summary>
public static class AgendaCacheCodec
{
    public const int MaxEvents = 500;
    /// <summary>Upper bound on input size; 500 events with long fields stay far below this.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;
    private const int Version = 1;

    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 8 };

    public static byte[] Serialize(IReadOnlyList<CalendarEvent> events, DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(events);
        var file = new CacheFile { V = Version, SavedAt = savedAt, Events = events.Take(MaxEvents).Select(e => (Dto?)Dto.From(e)).ToList() };
        return JsonSerializer.SerializeToUtf8Bytes(file, Json);
    }

    /// <summary>False (with empty results) for any malformed, oversized or invalid input. Never throws.</summary>
    public static bool TryDeserialize(byte[] data, out DateTimeOffset savedAt, out IReadOnlyList<CalendarEvent> events)
    {
        savedAt = default;
        events = Array.Empty<CalendarEvent>();
        try
        {
            if (data is null || data.Length == 0 || data.Length > MaxBytes) return false;
            var file = JsonSerializer.Deserialize<CacheFile>(data, Json);
            if (file is not { V: Version, Events: { } list } || list.Count > MaxEvents) return false;

            var result = new List<CalendarEvent>(list.Count);
            foreach (var d in list)
            {
                if (d?.ToEvent() is not { } e) return false;
                result.Add(e);
            }
            savedAt = file.SavedAt;
            events = result;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class CacheFile
    {
        public int V { get; set; }
        public DateTimeOffset SavedAt { get; set; }
        public List<Dto?>? Events { get; set; }
    }

    private sealed class Dto
    {
        public string? Key { get; set; }
        public string? Subject { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string? Location { get; set; }
        public string? JoinUrl { get; set; }
        public int ReminderMinutes { get; set; }
        public int Source { get; set; }

        public static Dto From(CalendarEvent e) => new()
        {
            Key = e.Key, Subject = e.Subject, Start = e.Start, End = e.End, Location = e.Location,
            JoinUrl = e.JoinUrl, ReminderMinutes = e.ReminderMinutes, Source = (int)e.Source,
        };

        public CalendarEvent? ToEvent()
        {
            if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Subject)) return null;
            if (Start > End || !Enum.IsDefined((CalendarSource)Source)) return null;
            if (ReminderMinutes is < 0 or > ReminderScheduler.MaxReminderMinutes) return null;
            // Only https join links survive a round trip; the shell refuses anything else anyway.
            var join = JoinUrl is not null && Uri.TryCreate(JoinUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? JoinUrl : null;
            return new CalendarEvent(Key, Subject,
                DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), DateTime.SpecifyKind(End, DateTimeKind.Unspecified),
                string.IsNullOrWhiteSpace(Location) ? null : Location, join, ReminderMinutes, (CalendarSource)Source);
        }
    }
}
