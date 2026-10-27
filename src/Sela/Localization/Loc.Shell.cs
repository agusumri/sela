namespace Sela.Localization;

// Owned by the shell lane: tray menu, app-level messages, and strings shared by several windows.
public sealed partial class Loc
{
    private static readonly Dictionary<string, (string Id, string En)> ShellStrings = new(StringComparer.Ordinal)
    {
        // Tray menu
        ["Shell_TraySchedule"] = ("Jadwal & agenda", "Schedule & agenda"),
        ["Shell_TrayHadith"] = ("Hadits hari ini", "Today's hadith"),
        ["Shell_TrayResetNotch"] = ("Kembalikan posisi notch", "Reset notch position"),
        ["Shell_TraySettings"] = ("Pengaturan", "Settings"),
        ["Shell_TrayTestNotify"] = ("Tes notifikasi", "Test notification"),
        ["Shell_TrayMuteAdhan"] = ("Bisukan adzan", "Mute adhan"),
        ["Shell_TrayStopAdhan"] = ("Hentikan adzan", "Stop adhan"),
        ["Shell_TrayRefresh"] = ("Segarkan", "Refresh"),
        ["Shell_TrayQuit"] = ("Keluar", "Quit"),
        // {0} = prayer name, {1} = time (HH:mm)
        ["Shell_TrayTooltip"] = ("Sela — {0} {1}", "Sela — {0} {1}"),
        // first run: Sela turned on start-with-Windows
        ["Shell_AutostartNoticeTitle"] = ("Sela berjalan saat Windows dinyalakan", "Sela starts with Windows"),
        ["Shell_AutostartNoticeBody"] = ("Supaya pengingat sholat tetap jalan setelah restart. Matikan di Pengaturan > Mulai bersama Windows.",
                                         "So prayer reminders keep working after a restart. Turn it off in Settings > Start with Windows."),

        // Demo / test reminders. {0} = prayer name, {1} = city
        ["Shell_PrayerNowTitle"] = ("Waktu {0}", "Time for {0}"),
        ["Shell_PrayerNowBody"] = ("Sudah masuk waktu {0} untuk {1}", "It's now time for {0} in {1}"),
        ["Shell_DemoSoonTitle"] = ("10 menit lagi {0}", "{0} in 10 minutes"),
        ["Shell_DemoSoonBody"] = ("Bersiap untuk sholat {0}", "Get ready for {0} prayer"),
        // {0} = start, {1} = end
        ["Shell_DemoMeetingBody"] = ("{0}–{1} · mulai 5 menit lagi · Microsoft Teams", "{0}–{1} · starts in 5 minutes · Microsoft Teams"),
        // new-version card: {0} = new version, {1} = running version
        ["Shell_UpdateTitle"] = ("Sela {0} tersedia", "Sela {0} is available"),
        ["Shell_UpdateBody"] = ("Kamu memakai {1}", "You're on {1}"),
        ["Shell_TestHabitTitle"] = ("Sudah sholawat hari ini?", "Have you sent salawat today?"),
        ["Shell_TestHabitBody"] = ("Luangkan sebentar untuk bersholawat", "Take a moment to send salawat"),
    };
}
