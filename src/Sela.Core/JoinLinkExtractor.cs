using System.Text.RegularExpressions;

namespace Sela.Core;

/// <summary>Finds the first online-meeting join link (Teams, Zoom, Google Meet, Webex) in free text.</summary>
public static partial class JoinLinkExtractor
{
    [GeneratedRegex(
        @"https://(?:" +
        @"teams\.microsoft\.com/l/meetup-join/[^\s""'<>]+" +
        @"|teams\.live\.com/meet/[^\s""'<>]+" +
        @"|(?:[a-z0-9-]+\.)*zoom\.us/(?:j|my)/[^\s""'<>]+" +
        @"|meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}(?:\?[^\s""'<>]*)?" +
        @"|(?:[a-z0-9-]+\.)+webex\.com/[^\s""'<>]+" +
        @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    private static readonly char[] TrailingJunk = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '>', '*' };

    /// <summary>Searches each text in order and returns the first matching https link, or null.</summary>
    public static string? Find(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            var m = LinkRegex().Match(text);
            if (m.Success) return m.Value.TrimEnd(TrailingJunk);
        }
        return null;
    }
}
