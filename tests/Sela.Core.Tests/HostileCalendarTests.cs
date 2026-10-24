namespace Sela.Core.Tests;

// Regression tests for untrusted calendar data (security review findings #1 and #2).
public class HostileCalendarTests
{
    private static readonly TimeZoneInfo Wib = TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");
    private static readonly DateTime From = new(2026, 10, 26, 0, 0, 0);
    private static readonly DateTime To = From.AddDays(3);

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string uid, string extra = "", string start = "20261026T020000Z", string end = "20261026T030000Z") =>
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTART:{start}\r\nDTEND:{end}\r\nSUMMARY:{uid}\r\n{extra}END:VEVENT\r\n";

    [Fact]
    public void Oversized_trigger_is_clamped_to_one_week()
    {
        var alarm = "BEGIN:VALARM\r\nACTION:DISPLAY\r\nDESCRIPTION:x\r\nTRIGGER:-P200000W\r\nEND:VALARM\r\n";
        var events = IcsCalendarProvider.Parse(Calendar(Event("big", alarm)), From, To, 10, Wib);

        var e = Assert.Single(events);
        Assert.InRange(e.ReminderMinutes, 0, ReminderScheduler.MaxReminderMinutes);
    }

    [Theory]
    [InlineData(int.MaxValue, 1)] // clamped to a week: one minute before start is inside the window
    [InlineData(int.MinValue, 0)] // clamped to zero: fires at start, not before
    public void Scheduler_does_not_throw_on_extreme_reminder_minutes(int minutes, int expected)
    {
        var start = new DateTime(2026, 10, 26, 9, 0, 0);
        var ev = new CalendarEvent("k", "Rapat", start, start.AddHours(1), null, null, minutes, CalendarSource.Google);
        var today = new DaySchedule(new DateOnly(2026, 10, 26), DefaultCity.Jakarta, Array.Empty<PrayerTime>());

        var due = new ReminderScheduler().Due(start.AddMinutes(-1), today, new[] { ev }, new SelaSettings());

        Assert.Equal(expected, due.Count);
    }

    [Fact]
    public void Meeting_near_min_date_with_long_lead_is_skipped_not_thrown()
    {
        var start = DateTime.MinValue.AddMinutes(5);
        var ev = new CalendarEvent("k", "x", start, start.AddMinutes(30), null, null, ReminderScheduler.MaxReminderMinutes, CalendarSource.Outlook);
        var today = new DaySchedule(new DateOnly(1, 1, 1), DefaultCity.Jakarta, Array.Empty<PrayerTime>());

        var due = new ReminderScheduler().Due(start, today, new[] { ev }, new SelaSettings());

        Assert.Empty(due);
    }

    [Theory]
    [InlineData("SECONDLY")]
    [InlineData("MINUTELY")]
    [InlineData("HOURLY")]
    public void Sub_daily_recurrence_is_skipped(string freq)
    {
        var ics = Calendar(
            Event("flood", $"RRULE:FREQ={freq}\r\n", start: "20200101T000000Z", end: "20200101T000100Z"),
            Event("normal"));

        var events = IcsCalendarProvider.Parse(ics, From, To, 10, Wib);

        Assert.Equal(new[] { "normal" }, events.Select(e => e.Subject));
    }

    [Fact]
    public void Occurrences_are_capped()
    {
        // 1500 daily events x 3 days = 4500 occurrences in the window, above the cap.
        var many = Enumerable.Range(0, 1500).Select(i => Event($"e{i}", "RRULE:FREQ=DAILY\r\n")).ToArray();

        var events = IcsCalendarProvider.Parse(Calendar(many), From, To, 10, Wib);

        Assert.InRange(events.Count, 1, IcsCalendarProvider.MaxOccurrences);
    }

    [Fact]
    public void Malformed_event_is_skipped_while_others_parse()
    {
        var ics = Calendar(
            Event("broken", "RRULE:FREQ=DAILY;UNTIL=notadate;BYDAY=XX\r\n"),
            Event("good", start: "20261027T020000Z", end: "20261027T030000Z"));

        var events = IcsCalendarProvider.Parse(ics, From, To, 10, Wib);

        Assert.Contains(events, e => e.Subject == "good");
    }

    [Fact]
    public void Expansion_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            IcsCalendarProvider.Parse(Calendar(Event("x")), From, To, 10, Wib, cts.Token));
    }
}
