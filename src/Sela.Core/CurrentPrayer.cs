namespace Sela.Core;

public static class CurrentPrayer
{
    /// <summary>
    /// The prayer period <paramref name="now"/> falls in, with the time it began:
    /// Subuh until Terbit, then Dhuha (a quiet period, not a wajib prayer) until Dzuhur, then Dzuhur, Ashar,
    /// Maghrib and Isya. Before today's Subuh it is still last night's Isya; that start is not in today's
    /// schedule, so <c>Since</c> is null. Null without a schedule for today's date.
    /// </summary>
    public static (Prayer Prayer, DateTime? Since)? For(DateTime now, DaySchedule? today)
    {
        if (today is null || today.Date != DateOnly.FromDateTime(now)) return null;
        if (now < today[Prayer.Subuh]) return (Prayer.Isya, null);
        if (now < today[Prayer.Terbit]) return (Prayer.Subuh, today[Prayer.Subuh]);
        if (now < today[Prayer.Dzuhur]) return (Prayer.Dhuha, today[Prayer.Terbit]);
        if (now < today[Prayer.Ashar]) return (Prayer.Dzuhur, today[Prayer.Dzuhur]);
        if (now < today[Prayer.Maghrib]) return (Prayer.Ashar, today[Prayer.Ashar]);
        if (now < today[Prayer.Isya]) return (Prayer.Maghrib, today[Prayer.Maghrib]);
        return (Prayer.Isya, today[Prayer.Isya]);
    }

    /// <summary>How far <paramref name="now"/> is from <paramref name="start"/> to <paramref name="end"/>, clamped to 0..1 (0 when the span is empty).</summary>
    public static double Progress(DateTime now, DateTime start, DateTime end)
    {
        var span = (end - start).Ticks;
        return span <= 0 ? 0 : Math.Clamp((double)(now - start).Ticks / span, 0, 1);
    }
}
