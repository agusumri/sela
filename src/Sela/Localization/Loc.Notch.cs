namespace Sela.Localization;

// Owned by the notch lane: NotchWindow.* and Views/Notch/*.
public sealed partial class Loc
{
    private static readonly Dictionary<string, (string Id, string En)> NotchStrings = new(StringComparer.Ordinal)
    {
        // tabs + top bar
        ["Notch_TabSholat"] = ("Sholat", "Prayer"),
        ["Notch_TabAgenda"] = ("Agenda", "Agenda"),
        ["Notch_TabHadits"] = ("Hadits", "Hadith"),
        ["Notch_Settings"] = ("Pengaturan", "Settings"),
        ["Notch_Refresh"] = ("Segarkan", "Refresh"),

        // status line + offline
        ["Notch_OfflineSaved"] = ("Offline · menampilkan data tersimpan", "Offline · showing saved data"),
        ["Notch_UpdatedAt"] = ("Diperbarui {0}", "Updated {0}"),
        ["Notch_Yesterday"] = ("kemarin {0}", "yesterday {0}"),
        ["Notch_Loading"] = ("Memuat…", "Loading…"),
        ["Notch_AgendaFailed"] = ("Agenda belum bisa dimuat", "Couldn't load your agenda"),
        ["Notch_AgendaFailedHint"] = ("Periksa internet atau Outlook, lalu coba lagi", "Check your internet or Outlook, then try again"),

        // idle pill, second line under the current prayer: {0} = next prayer, {1} = its time
        ["Notch_IdleNextAt"] = ("Berikutnya {0} {1}", "Next {0} {1}"),
        // idle pill, second line in the first 30 min after a wajib prayer begins: {0} = elapsed ("12m lalu" / "baru saja")
        ["Notch_IdleStarted"] = ("Sudah masuk · {0}", "Started · {0}"),
        ["Notch_MinutesAgo"] = ("{0}m lalu", "{0}m ago"),
        ["Notch_JustNow"] = ("baru saja", "just now"),

        // glance: now → next timeline
        ["Notch_GlanceNow"] = ("SEKARANG", "NOW"),
        ["Notch_GlanceNext"] = ("BERIKUTNYA", "NEXT"),
        ["Notch_GlanceNextTomorrow"] = ("BERIKUTNYA · BESOK", "NEXT · TOMORROW"),
        ["Notch_Since"] = ("sejak {0}", "since {0}"),
        // screen reader sentence for the glance: {0} now, {1} since, {2} next, {3} next time, {4} spoken duration
        ["Notch_GlanceSpoken"] = ("Sekarang waktu {0} sejak {1}. Berikutnya {2} pukul {3}, {4} lagi.",
                                  "It's {0} time, since {1}. Next is {2} at {3}, in {4}."),
        ["Notch_GlanceSpokenNoSince"] = ("Sekarang waktu {0}. Berikutnya {2} pukul {3}, {4} lagi.",
                                         "It's {0} time. Next is {2} at {3}, in {4}."),
        ["Notch_GlanceSpokenNext"] = ("Berikutnya {2} pukul {3}, {4} lagi.", "Next is {2} at {3}, in {4}."),
        ["Notch_SpokenHours"] = ("{0} jam", "{0} hours"),
        ["Notch_SpokenHour"] = ("{0} jam", "{0} hour"),
        ["Notch_SpokenMinutes"] = ("{0} menit", "{0} minutes"),
        ["Notch_SpokenMinute"] = ("{0} menit", "{0} minute"),
        ["Notch_PrayerTimes"] = ("Jadwal sholat", "Prayer times"),
        ["Notch_InProgress"] = ("Sedang berlangsung", "In progress"),
        ["Notch_NoMoreMeetings"] = ("Tidak ada meeting lagi hari ini", "No more meetings today"),

        // shared
        ["Notch_NoTitle"] = ("(Tanpa judul)", "(No title)"),
        ["Notch_JustAMoment"] = ("Sebentar ya", "Just a moment"),
        ["Notch_CheckInternet"] = ("Cek koneksi internet", "Check your internet connection"),
        ["Notch_Join"] = ("Join", "Join"),
        ["Notch_JoinHint"] = ("Buka link meeting", "Open meeting link"),
        ["Notch_StopAdhan"] = ("Hentikan adzan", "Stop adhan"),
        ["Notch_StopAdhanHint"] = ("Hentikan suara adzan", "Stop the adhan sound"),
        ["Notch_Stop"] = ("Hentikan", "Stop"),

        // sholat page
        ["Notch_ScheduleUnavailable"] = ("Jadwal belum tersedia", "Schedule not available yet"),
        ["Notch_Retry"] = ("Coba lagi", "Try again"),

        // agenda page
        ["Notch_Today"] = ("Hari ini", "Today"),
        ["Notch_AgendaCount"] = ("{0} agenda", "{0} events"),
        ["Notch_AgendaCountOne"] = ("{0} agenda", "{0} event"),
        ["Notch_LoadingAgenda"] = ("Memuat agenda…", "Loading agenda…"),
        ["Notch_NoAgenda"] = ("Tidak ada agenda hari ini", "Nothing on today"),
        ["Notch_TimeToFocus"] = ("Waktunya fokus", "Time to focus"),
        ["Notch_FromSource"] = ("Dari {0}", "From {0}"),

        // hadits page
        ["Notch_LoadingHadith"] = ("Memuat hadits…", "Loading hadith…"),
        ["Notch_HadithUnavailable"] = ("Hadits belum tersedia", "Hadith not available yet"),
        // dhikr on the "N minutes until prayer" card: {0} = prayer name
        ["Notch_DhikrInvite_Istighfar"] = ("Sambil menunggu {0}, yuk istighfar", "While you wait for {0}, let's seek forgiveness"),
        ["Notch_DhikrInvite_Tasbih"] = ("Sambil menunggu {0}, yuk bertasbih", "While you wait for {0}, let's glorify Allah"),
        ["Notch_DhikrInvite_Dzikir"] = ("Sambil menunggu {0}, yuk berdzikir", "While you wait for {0}, let's remember Allah"),
        ["Notch_DhikrInvite_Tahmid"] = ("Sambil menunggu {0}, yuk bertahmid", "While you wait for {0}, let's praise Allah"),
        ["Notch_DhikrInvite_Tahlil"] = ("Sambil menunggu {0}, yuk bertahlil", "While you wait for {0}, let's declare Allah's oneness"),
        ["Notch_DhikrInvite_Takbir"] = ("Sambil menunggu {0}, yuk bertakbir", "While you wait for {0}, let's proclaim Allah's greatness"),
        ["Notch_DhikrInvite_Shalawat"] = ("Sambil menunggu {0}, yuk bershalawat", "While you wait for {0}, let's send blessings on the Prophet"),

        ["Notch_ShowFullArabic"] = ("Tampilkan teks Arab lengkap", "Show full Arabic"),
        ["Notch_HideArabic"] = ("Sembunyikan", "Hide"),
        ["Notch_HabitsToday"] = ("Ibadah hari ini", "Today's worship"),
        ["Notch_HabitDoneHint"] = ("{0}: sudah", "{0}: done"),
        ["Notch_HabitMarkHint"] = ("Tandai {0} sudah", "Mark {0} as done"),

        // reminder card
        ["Notch_Done"] = ("Sudah", "Done"),
        ["Notch_Later"] = ("Nanti", "Later"),
        ["Notch_LaterHint"] = ("Ingatkan lagi 30 menit lagi", "Remind me again in 30 minutes"),
        ["Notch_UpdateOpen"] = ("Lihat rilis", "View release"),
        ["Notch_UpdateOpenHint"] = ("Buka halaman rilis di GitHub", "Open the release page on GitHub"),
        ["Notch_UpdateSkip"] = ("Lewati", "Skip"),
        ["Notch_UpdateSkipHint"] = ("Lewati versi ini", "Skip this version"),
        ["Notch_Read"] = ("Baca", "Read"),
        ["Notch_ReadHint"] = ("Buka hadits lengkap", "Open the full hadith"),

        // formatting
        ["Notch_DateFormat"] = ("dddd, d MMMM yyyy", "dddd, MMMM d, yyyy"),
        ["Notch_HijriSuffix"] = ("H", "AH"),
        ["Notch_UnitHour"] = ("j", "h"),
        ["Notch_UnitMinute"] = ("m", "m"),
        ["Notch_UnitSecond"] = ("d", "s"),

        // --sky-tour demo labels
        ["Notch_TourDawn"] = ("Fajar", "Dawn"),
        ["Notch_TourDay"] = ("Siang", "Day"),
        ["Notch_TourDusk"] = ("Sore", "Dusk"),
        ["Notch_TourNight"] = ("Malam", "Night"),
        ["Notch_TourExcited"] = ("Mau sholat", "Prayer soon"),
    };
}
