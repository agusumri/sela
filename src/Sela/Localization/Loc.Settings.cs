namespace Sela.Localization;

// Owned by the settings lane: SettingsWindow.*.
public sealed partial class Loc
{
    private static readonly Dictionary<string, (string Id, string En)> SettingsStrings = new(StringComparer.Ordinal)
    {
        // ---- window chrome ----
        ["Settings_Title"] = ("Pengaturan Sela", "Sela Settings"),
        ["Settings_Header"] = ("Pengaturan", "Settings"),
        ["Settings_Logo"] = ("Logo Sela", "Sela logo"),
        ["Settings_Close"] = ("Tutup", "Close"),
        ["Settings_CloseTip"] = ("Tutup (Esc)", "Close (Esc)"),
        ["Settings_Cancel"] = ("Batal", "Cancel"),
        ["Settings_Save"] = ("Simpan", "Save"),

        // ---- language ----
        ["Settings_Language"] = ("Bahasa", "Language"),
        ["Settings_LanguageGroup"] = ("Bahasa tampilan", "Display language"),
        ["Settings_LangSystem"] = ("Ikuti Windows", "Follow Windows"),
        ["Settings_LangIndonesian"] = ("Indonesia", "Indonesia"),
        ["Settings_LangEnglish"] = ("English", "English"),

        // ---- location ----
        ["Settings_Location"] = ("Lokasi", "Location"),
        ["Settings_SearchPlaceholder"] = ("Cari kota (Indonesia atau seluruh dunia)…", "Search for a city (Indonesia or worldwide)…"),
        ["Settings_SearchName"] = ("Cari kota", "Search for a city"),
        ["Settings_ResultsName"] = ("Hasil pencarian kota", "City search results"),
        ["Settings_Searching"] = ("Mencari…", "Searching…"),
        ["Settings_NotFound"] = ("Kota tidak ditemukan", "No city found"),
        ["Settings_SearchFailed"] = ("Gagal mencari. Cek koneksi internet.", "Search failed. Check your internet connection."),
        ["Settings_CityChanged"] = ("Lokasi diganti ke {0}", "Location changed to {0}"),
        ["Settings_KemenagChip"] = ("Kemenag", "Kemenag"),
        ["Settings_SourceKemenag"] = ("Jadwal dari Kemenag RI via myquran.com", "Times from Kemenag RI via myquran.com"),
        ["Settings_SourceAladhan"] = ("Jadwal dari Aladhan.com", "Times from Aladhan.com"),
        ["Settings_Method"] = ("Metode perhitungan", "Calculation method"),
        ["Settings_MethodAuto"] = ("Otomatis (sesuai lokasi)", "Automatic (based on location)"),
        ["Settings_Hanafi"] = ("Ashar mazhab Hanafi", "Asr: Hanafi method"),
        ["Settings_HanafiHint"] = ("Waktu Ashar lebih akhir (bayangan 2×)", "Later Asr (shadow length 2×)"),

        // ---- prayers ----
        ["Settings_Prayers"] = ("Sholat", "Prayers"),
        ["Settings_Obligatory"] = ("Waktu wajib", "Obligatory prayers"),
        ["Settings_Extra"] = ("Tambahan", "Additional times"),
        ["Settings_PrayerSoon"] = ("Ingatkan sebelum waktu sholat", "Remind me before prayer time"),
        ["Settings_Off"] = ("Mati", "Off"),
        ["Settings_Min5"] = ("5 menit", "5 min"),
        ["Settings_Min10"] = ("10 menit", "10 min"),
        ["Settings_Min15"] = ("15 menit", "15 min"),

        // ---- adhan ----
        ["Settings_Adhan"] = ("Adzan", "Adhan"),
        ["Settings_AdhanToggle"] = ("Putar adzan saat masuk waktu sholat", "Play the adhan when prayer time begins"),
        ["Settings_Mute"] = ("Bisukan sementara", "Mute for now"),
        ["Settings_MuteHint"] = ("Adzan tidak bunyi, pengingat tetap muncul", "The adhan stays silent, reminders still appear"),
        ["Settings_PlayFor"] = ("Putar untuk", "Play for"),
        ["Settings_Volume"] = ("Volume", "Volume"),
        ["Settings_VolumeName"] = ("Volume adzan", "Adhan volume"),
        ["Settings_Sound"] = ("Suara", "Sound"),
        ["Settings_AdhanSubuh"] = ("Adzan Subuh", "Fajr adhan"),
        ["Settings_Default"] = ("Bawaan", "Default"),
        ["Settings_UseDefaultAdhan"] = ("Pakai suara adzan bawaan", "Use the default adhan sound"),
        ["Settings_UseDefaultSubuh"] = ("Pakai suara adzan Subuh bawaan", "Use the default Fajr adhan sound"),
        ["Settings_PickFile"] = ("Pilih file…", "Choose file…"),
        ["Settings_PickAdhanName"] = ("Pilih file suara adzan", "Choose an adhan sound file"),
        ["Settings_PickSubuhName"] = ("Pilih file suara adzan Subuh", "Choose a Fajr adhan sound file"),
        ["Settings_PickAdhanTitle"] = ("Pilih suara adzan", "Choose adhan sound"),
        ["Settings_PickSubuhTitle"] = ("Pilih suara adzan Subuh", "Choose Fajr adhan sound"),
        ["Settings_AudioFilter"] = ("Audio (*.mp3;*.wav;*.wma;*.m4a;*.aac)|*.mp3;*.wav;*.wma;*.m4a;*.aac",
                                    "Audio files (*.mp3;*.wav;*.wma;*.m4a;*.aac)|*.mp3;*.wav;*.wma;*.m4a;*.aac"),
        ["Settings_PlaySample"] = ("Putar contoh", "Play sample"),
        ["Settings_Stop"] = ("Hentikan", "Stop"),
        ["Settings_PlayAdhanSample"] = ("Putar contoh adzan", "Play adhan sample"),
        ["Settings_StopAdhanSample"] = ("Hentikan contoh adzan", "Stop adhan sample"),
        ["Settings_PlaySubuhSample"] = ("Putar contoh adzan Subuh", "Play Fajr adhan sample"),
        ["Settings_StopSubuhSample"] = ("Hentikan contoh adzan Subuh", "Stop Fajr adhan sample"),

        // ---- daily worship ----
        ["Settings_Habits"] = ("Ibadah harian", "Daily worship"),
        ["Settings_HabitToggle"] = ("Ingatkan dzikir dan sholawat", "Remind me of dhikr and salawat"),
        ["Settings_AfterSubuh"] = ("setelah Subuh", "after Fajr"),
        ["Settings_AfterDzuhur"] = ("siang, setelah Dzuhur", "midday, after Dhuhr"),
        ["Settings_AfterAshar"] = ("setelah Ashar", "after Asr"),
        ["Settings_HabitHint"] = ("Sela bertanya sekali di tiap waktu. Pilih Sudah atau Nanti.",
                                  "Sela asks once at each time. Choose Done or Later."),
        ["Settings_Hadith"] = ("Tampilkan hadits harian", "Show a daily hadith"),
        ["Settings_HadithHint"] = ("Satu hadits Arbain Nawawi tiap pagi, dari myquran.com",
                                   "One hadith from an-Nawawi's Forty each morning"),
        ["Settings_PrayerDhikr"] = ("Dzikir singkat sebelum sholat", "Short dhikr before prayer"),
        ["Settings_PrayerDhikrHint"] = ("Istighfar, tasbih, tahmid, tahlil, takbir, atau shalawat di pengingat sebelum waktu sholat",
                                        "Istighfar, tasbih, tahmid, tahlil, takbir or salawat on the reminder before each prayer"),

        // ---- meetings ----
        ["Settings_Meeting"] = ("Meeting", "Meetings"),
        ["Settings_Outlook"] = ("Outlook (aplikasi klasik)", "Outlook (classic app)"),
        ["Settings_Google"] = ("Google Calendar", "Google Calendar"),
        ["Settings_GooglePlaceholder"] = ("Tempel alamat rahasia iCal", "Paste the secret iCal address"),
        ["Settings_GoogleName"] = ("Alamat rahasia iCal Google Calendar", "Google Calendar secret iCal address"),
        ["Settings_ShowAddress"] = ("Tampilkan alamat", "Show address"),
        ["Settings_HideAddress"] = ("Sembunyikan alamat", "Hide address"),
        ["Settings_GoogleError"] = ("Alamat harus diawali https:// atau webcal://", "The address must start with https:// or webcal://"),
        ["Settings_GoogleHelp"] = (
            "Buka Google Calendar di web → Setelan → pilih kalender → Integrasikan kalender → salin 'Alamat rahasia dalam format iCal'. Alamat ini rahasia, disimpan terenkripsi di perangkat ini.",
            "Open Google Calendar on the web → Settings → pick a calendar → Integrate calendar → copy 'Secret address in iCal format'. This address is private and stored encrypted on this device."),
        ["Settings_MeetingDefault"] = ("Ingatkan meeting tanpa pengingat", "Remind me of meetings without a reminder"),

        // ---- general ----
        ["Settings_General"] = ("Umum", "General"),
        ["Settings_Autostart"] = ("Mulai bersama Windows", "Start with Windows"),
        ["Settings_Idle"] = ("Tampilkan notch saat diam", "Show the notch when idle"),
    };
}
