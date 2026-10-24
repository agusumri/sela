namespace Sela.Core;

/// <summary>Coarse sky mood used by the idle mascot.</summary>
public enum SkyPhase { Dawn, Day, Dusk, Night }

public static class SkyPhases
{
    /// <summary>
    /// Dawn: Subuh until 30 min after Terbit. Day: until Ashar. Dusk: Ashar until 20 min after Maghrib.
    /// Night otherwise. Without a schedule, falls back to clock hours.
    /// </summary>
    public static SkyPhase For(DateTime now, DaySchedule? today)
    {
        if (today is null || today.Date != DateOnly.FromDateTime(now))
            return now.Hour switch
            {
                >= 4 and < 6 => SkyPhase.Dawn,
                >= 6 and < 15 => SkyPhase.Day,
                >= 15 and < 18 => SkyPhase.Dusk,
                _ => SkyPhase.Night,
            };

        if (now < today[Prayer.Subuh]) return SkyPhase.Night;
        if (now < today[Prayer.Terbit].AddMinutes(30)) return SkyPhase.Dawn;
        if (now < today[Prayer.Ashar]) return SkyPhase.Day;
        if (now < today[Prayer.Maghrib].AddMinutes(20)) return SkyPhase.Dusk;
        return SkyPhase.Night;
    }
}
