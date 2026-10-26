namespace Sela.Core;

/// <summary>Short dhikr and shalawat shown on the "N minutes until prayer" card, picked in a fixed rotation.</summary>
public static class DhikrLibrary
{
    private static readonly DhikrKind[] Kinds = Enum.GetValues<DhikrKind>();

    public static Dhikr For(DhikrKind kind, AppLanguage language)
    {
        bool en = language == AppLanguage.English;
        Dhikr D(string arabic, string latin, string id, string english) => new(kind, arabic, latin, en ? english : id);
        return kind switch
        {
            DhikrKind.Istighfar => D("أَسْتَغْفِرُ اللّٰهَ", "Astaghfirullah",
                "Aku memohon ampun kepada Allah", "I seek Allah's forgiveness"),
            DhikrKind.Tahmid => D("اَلْحَمْدُ لِلّٰهِ", "Alhamdulillah",
                "Segala puji bagi Allah", "All praise is for Allah"),
            DhikrKind.Tahlil => D("لَا إِلٰهَ إِلَّا اللّٰهُ", "Lā ilāha illallāh",
                "Tiada Tuhan selain Allah", "There is no god but Allah"),
            DhikrKind.Takbir => D("اَللّٰهُ أَكْبَرُ", "Allāhu akbar",
                "Allah Maha Besar", "Allah is the Greatest"),
            DhikrKind.Shalawat => D("صَلَّى اللّٰهُ عَلَىٰ مُحَمَّدٍ", "Shallallāhu ‘alā Muhammad",
                "Semoga Allah melimpahkan shalawat kepada Muhammad", "May Allah send blessings upon Muhammad"),
            DhikrKind.Tasbih => D("سُبْحَانَ اللّٰهِ", "Subhānallāh",
                "Maha Suci Allah", "Glory be to Allah"),
            DhikrKind.Hauqalah => D("لَا حَوْلَ وَلَا قُوَّةَ إِلَّا بِاللّٰهِ", "Lā haula wa lā quwwata illā billāh",
                "Tiada daya dan kekuatan kecuali dengan pertolongan Allah", "There is no might nor power except with Allah"),
            DhikrKind.ShalawatNabi => D("اَللّٰهُمَّ صَلِّ وَسَلِّمْ عَلَىٰ نَبِيِّنَا مُحَمَّدٍ", "Allāhumma shalli wa sallim ‘alā nabiyyinā Muhammad",
                "Ya Allah, limpahkanlah shalawat dan salam kepada nabi kami Muhammad", "O Allah, send blessings and peace upon our Prophet Muhammad"),
            DhikrKind.TasbihBihamdih => D("سُبْحَانَ اللّٰهِ وَبِحَمْدِهِ", "Subhānallāhi wa bihamdih",
                "Maha Suci Allah dan segala puji bagi-Nya", "Glory be to Allah and praise be to Him"),
            DhikrKind.Hasbalah => D("حَسْبُنَا اللّٰهُ وَنِعْمَ الْوَكِيْلُ", "Hasbunallāh wa ni‘mal wakīl",
                "Cukuplah Allah bagi kami, dan Dia sebaik-baik pelindung", "Allah is sufficient for us, and He is the best guardian"),
            DhikrKind.TasbihAzhim => D("سُبْحَانَ اللّٰهِ الْعَظِيْمِ", "Subhānallāhil ‘azhīm",
                "Maha Suci Allah Yang Maha Agung", "Glory be to Allah, the Magnificent"),
            DhikrKind.ShalawatSayyidina => D("اَللّٰهُمَّ صَلِّ عَلَىٰ سَيِّدِنَا مُحَمَّدٍ وَعَلَىٰ آلِ سَيِّدِنَا مُحَمَّدٍ",
                "Allāhumma shalli ‘alā sayyidinā Muhammad wa ‘alā āli sayyidinā Muhammad",
                "Ya Allah, limpahkanlah shalawat kepada junjungan kami Muhammad dan keluarganya",
                "O Allah, send blessings upon our master Muhammad and his family"),
            DhikrKind.BaqiyatShalihat => D("سُبْحَانَ اللّٰهِ وَالْحَمْدُ لِلّٰهِ وَلَا إِلٰهَ إِلَّا اللّٰهُ وَاللّٰهُ أَكْبَرُ",
                "Subhānallāh walhamdulillāh wa lā ilāha illallāh wallāhu akbar",
                "Maha Suci Allah, segala puji bagi Allah, tiada Tuhan selain Allah, Allah Maha Besar",
                "Glory be to Allah, praise be to Allah, there is no god but Allah, Allah is the Greatest"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Which invitation the card uses ("yuk istighfar", "yuk bertasbih", "yuk bershalawat", ...).</summary>
    public static DhikrInvite InviteOf(DhikrKind kind) => kind switch
    {
        DhikrKind.Istighfar => DhikrInvite.Istighfar,
        DhikrKind.Tasbih or DhikrKind.TasbihBihamdih or DhikrKind.TasbihAzhim => DhikrInvite.Tasbih,
        DhikrKind.Tahmid => DhikrInvite.Tahmid,
        DhikrKind.Tahlil => DhikrInvite.Tahlil,
        DhikrKind.Takbir => DhikrInvite.Takbir,
        DhikrKind.Shalawat or DhikrKind.ShalawatNabi or DhikrKind.ShalawatSayyidina => DhikrInvite.Shalawat,
        DhikrKind.Hauqalah or DhikrKind.Hasbalah or DhikrKind.BaqiyatShalihat => DhikrInvite.Dzikir,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Deterministic per (date, prayer): stable across ticks and restarts. Five slots per day keep the rotation
    /// continuous, so consecutive prayers differ (Isya and the next day's Subuh too) and a day's five are distinct.
    /// The kind count (13) is not a multiple of five, so each prayer also walks through every kind over the days.
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
