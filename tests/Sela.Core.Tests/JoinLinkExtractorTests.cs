namespace Sela.Core.Tests;

public class JoinLinkExtractorTests
{
    [Theory]
    [InlineData("Join: https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%7d.", "https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%7d")]
    [InlineData("<https://teams.live.com/meet/9876543210>", "https://teams.live.com/meet/9876543210")]
    [InlineData("Zoom https://us02web.zoom.us/j/123456789?pwd=abc, see you", "https://us02web.zoom.us/j/123456789?pwd=abc")]
    [InlineData("https://zoom.us/my/someone)", "https://zoom.us/my/someone")]
    [InlineData("Meet: https://meet.google.com/abc-defg-hij", "https://meet.google.com/abc-defg-hij")]
    [InlineData("https://acme.webex.com/acme/j.php?MTID=m123;", "https://acme.webex.com/acme/j.php?MTID=m123")]
    public void Finds_supported_links(string text, string expected) => Assert.Equal(expected, JoinLinkExtractor.Find(text));

    [Theory]
    [InlineData("http://zoom.us/j/123")]
    [InlineData("https://example.com/j/123")]
    [InlineData("https://meet.google.com/landing")]
    [InlineData("https://evilzoom.us.example.com/j/1")]
    [InlineData("")]
    public void Ignores_other_links(string text) => Assert.Null(JoinLinkExtractor.Find(text));

    [Fact]
    public void Returns_first_text_with_match()
    {
        Assert.Equal("https://meet.google.com/abc-defg-hij",
            JoinLinkExtractor.Find(null, "Ruang 3", "https://meet.google.com/abc-defg-hij", "https://zoom.us/j/1"));
        Assert.Null(JoinLinkExtractor.Find());
    }
}
