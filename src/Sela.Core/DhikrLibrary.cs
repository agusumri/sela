namespace Sela.Core;

/// <summary>Short dhikr shown on the "N minutes until prayer" card, picked in a fixed rotation.</summary>
public static class DhikrLibrary
{
    private static readonly DhikrKind[] Kinds = Enum.GetValues<DhikrKind>();

    public static Dhikr For(DhikrKind kind, AppLanguage language)
    {
        bool en = language == AppLanguage.English;
        return kind switch
        {
            DhikrKind.Istighfar => new(kind, "أَسْتَغْفِرُ اللّٰهَ", "Astaghfirullah",
                en ? "I seek Allah's forgiveness" : "Aku memohon ampun kepada Allah"),
            DhikrKind.Tahmid => new(kind, "اَلْحَمْدُ لِلّٰهِ", "Alhamdulillah",
                en ? "All praise is for Allah" : "Segala puji bagi Allah"),
            DhikrKind.Tahlil => new(kind, "لَا إِلٰهَ إِلَّا اللّٰهُ", "Lā ilāha illallāh",
                en ? "There is no god but Allah" : "Tiada Tuhan selain Allah"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>
    /// Deterministic per (date, prayer): stable across ticks and restarts. Consecutive wajib prayers differ,
    /// a day's five prayers cover every kind, and the sequence shifts each day. Counting five slots per day
    /// keeps the rotation continuous across midnight, so Isya and the next day's Subuh differ too.
    /// </summary>
    public static DhikrKind Pick(DateOnly date, Prayer prayer)
    {
        int index = IndexOf(prayer);
        if (index < 0) index = (int)prayer; // non-wajib: still deterministic
        long slot = (long)date.DayNumber * ReminderScheduler.Wajib.Count + index;
        return Kinds[(int)(slot % Kinds.Length)];
    }

    private static int IndexOf(Prayer prayer)
    {
        var wajib = ReminderScheduler.Wajib;
        for (int i = 0; i < wajib.Count; i++)
            if (wajib[i] == prayer) return i;
        return -1;
    }
}
