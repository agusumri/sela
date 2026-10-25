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

        // glance
        ["Notch_NextPrayer"] = ("Sholat berikutnya", "Next prayer"),
        ["Notch_NextPrayerTomorrow"] = ("Sholat berikutnya · besok", "Next prayer · tomorrow"),
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
        ["Notch_ShowFullArabic"] = ("Tampilkan teks Arab lengkap", "Show full Arabic"),
        ["Notch_HideArabic"] = ("Sembunyikan", "Hide"),
        ["Notch_HabitsToday"] = ("Ibadah hari ini", "Today's worship"),
        ["Notch_HabitDoneHint"] = ("{0}: sudah", "{0}: done"),
        ["Notch_HabitMarkHint"] = ("Tandai {0} sudah", "Mark {0} as done"),

        // reminder card
        ["Notch_Done"] = ("Sudah", "Done"),
        ["Notch_Later"] = ("Nanti", "Later"),
        ["Notch_LaterHint"] = ("Ingatkan lagi 30 menit lagi", "Remind me again in 30 minutes"),
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
