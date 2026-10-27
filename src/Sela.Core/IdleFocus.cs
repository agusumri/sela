namespace Sela.Core;

/// <summary>
/// The one thing the idle pill shows: normally the next prayer, but for a short while after a wajib prayer
/// begins, that prayer ("Waktu Dzuhur · 12m lalu").
/// </summary>
public abstract record IdleFocus
{
    /// <summary>How long a wajib prayer stays in the pill after it begins.</summary>
    public static readonly TimeSpan JustStartedWindow = TimeSpan.FromMinutes(30);

    public sealed record Next(Prayer Prayer, DateTime Time) : IdleFocus;

    public sealed record JustStarted(Prayer Prayer, DateTime Since) : IdleFocus;

    private IdleFocus() { }

    /// <summary>
    /// JustStarted while a wajib prayer (Subuh, Dzuhur, Ashar, Maghrib, Isya) began less than
    /// <see cref="JustStartedWindow"/> ago, unless the next prayer also begins inside that window (then the next
    /// prayer wins). Dhuha never counts, and neither does Isya after midnight (its start isn't in today's schedule).
    /// Otherwise Next; null when there is nothing to show (no schedule, no next prayer).
    /// </summary>
    public static IdleFocus? For(DateTime now, DaySchedule? today, (Prayer Prayer, DateTime Time)? next)
    {
        if (CurrentPrayer.For(now, today) is { Since: { } since } current
            && ReminderScheduler.Wajib.Contains(current.Prayer)
            && now - since < JustStartedWindow
            && !(next is { } n && n.Time < since + JustStartedWindow))
            return new JustStarted(current.Prayer, since);

        return next is { } x ? new Next(x.Prayer, x.Time) : null;
    }
}
