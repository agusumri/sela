namespace Sela.Core.Tests;

// Security review finding: geocoder text is untrusted and ends up in the UI and settings.json.
public class GeocodingSanitizeTests
{
    private static Task<IReadOnlyList<City>> Search(string body) =>
        new GeocodingClient(new HttpClient(new StubHandler(_ => StubHandler.Json(body))))
            .SearchAsync("test", AppLanguage.English, default);

    // Arguments are raw JSON values (already quoted), so escapes like \u202E reach the parser as-is.
    private static string Result(string name, string admin1 = "\"England\"", string country = "\"United Kingdom\"") =>
        $$"""{"results":[{"id":1,"name":{{name}},"latitude":51.5,"longitude":-0.1,"timezone":"Europe/London","country":{{country}},"admin1":{{admin1}},"country_code":"GB"}]}""";

    [Fact]
    public async Task Bidi_overrides_and_control_characters_are_removed()
    {
        // U+202E RIGHT-TO-LEFT OVERRIDE, U+200B ZERO WIDTH SPACE, a tab and a newline.
        var city = Assert.Single(await Search(Result("\"Lon\\u202Edon\\u200B\\t\\nTown\"")));
        Assert.Equal("London Town", city.Name);
        Assert.DoesNotContain(city.Name, c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
    }

    [Fact]
    public async Task Region_and_country_are_cleaned_too()
    {
        var city = Assert.Single(await Search(Result("\"London\"", admin1: "\"Eng\\u202Eland\"", country: "\"United\\u0007 Kingdom\"")));
        Assert.Equal("England", city.Province);
        Assert.Equal("United Kingdom", city.Country);
    }

    [Fact]
    public async Task Very_long_names_are_capped()
    {
        var city = Assert.Single(await Search(Result($"\"{new string('a', 5000)}\"")));
        Assert.Equal(100, city.Name.Length);
    }

    [Fact]
    public async Task A_name_with_nothing_readable_is_skipped()
    {
        Assert.Empty(await Search(Result("\"\\u202E\\u200B\\u0007\"")));
    }
}
