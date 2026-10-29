/* Sela landing page: language toggle, gentle reveals, release info. No third-party requests except the public GitHub API. */
(() => {
  'use strict';

  const ID = {
    skip: 'Langsung ke isi',
    navLabel: 'Bagian halaman',
    navFeatures: 'Fitur',
    navMascot: 'Maskot',
    navPrivacy: 'Privasi',
    navDownload: 'Unduh',
    navMap: 'Peta kode',
    langLabel: 'Bahasa',
    heroEyebrow: 'Gratis dan open source · untuk Windows',
    heroTitle: 'Jeda sejenak di sela kesibukan.',
    heroLede: 'Notch hitam kecil di atas layar Windows untuk jadwal sholat, adzan, pengingat dzikir dan hadits harian, serta meeting kamu.',
    ctaDownload: 'Unduh untuk Windows',
    ctaGithub: 'Lihat di GitHub',
    heroReq: 'Windows 10 dan 11 (64-bit) · Tidak perlu memasang .NET',
    altHero: 'Notch Sela terbuka di atas layar: jadwal sholat Jakarta dengan garis waktu dan sholat berikutnya yang disorot hijau',
    heroCaption: 'Notch saat dibuka. Data contoh untuk Jakarta.',

    glanceEyebrow: 'Sekali lirik',
    glanceTitle: 'Arahkan kursor untuk ringkasan. Klik untuk melihat seharian.',
    glanceLede: 'Sela duduk di atas layar sebagai pill hitam kecil. Pill itu menampilkan sholat berikutnya dan countdown yang terus berjalan, ditemani maskot bulan sabit yang ikut berubah sesuai waktu.',
    stepHoverT: 'Arahkan kursor',
    stepHover: 'ke pill untuk melihat sholat berikutnya dan meeting terdekat.',
    stepClickT: 'Klik',
    stepClick: 'untuk membuka tab Sholat, Agenda, dan Hadits. Tekan Esc atau pindahkan kursor untuk menutupnya.',
    stepDragT: 'Geser',
    stepDrag: 'ke kiri, kanan, atau ke tepi bawah layar. Sela mengingat posisinya.',
    altPrayer: 'Tab Sholat: jadwal Jakarta dengan garis waktu dan sholat berikutnya yang disorot hijau',
    panelPrayerT: 'Jadwal sholat di mana saja',
    panelPrayer: 'Kota di Indonesia memakai jadwal resmi Kemenag RI. Kota lain memakai Aladhan dengan metode perhitungan pilihan kamu.',
    altAgenda: 'Tab Agenda: kartu meeting bertanda Outlook atau Google, dengan tombol Join',
    panelAgendaT: 'Agenda hari ini',
    panelAgenda: 'Meeting dari Outlook dan Google Calendar, lengkap dengan tanda sumbernya dan tombol Join untuk Teams, Zoom, Google Meet, dan Webex.',
    altHadith: 'Tab Hadits: Arbain Nawawi dalam teks Arab dan terjemahan Indonesia, dengan centang ibadah hari ini',
    panelHadithT: 'Hadits harian',
    panelHadith: 'Dari Arbain Nawawi, dalam teks Arab beserta terjemahan Indonesia atau Inggris.',

    remEyebrow: 'Pengingat',
    remTitle: 'Muncul hanya saat diperlukan.',
    remPrayerT: 'Pengingat sholat',
    remPrayer: 'Beberapa menit sebelum waktunya dan saat masuk waktu, dengan countdown jam, menit, dan detik.',
    remAdhanT: 'Adzan',
    remAdhan: 'Berbunyi saat masuk waktu. Hentikan dari kartu pengingat, atau bisukan sementara dari menu tray.',
    remHabitT: 'Pengingat dzikir dan sholawat',
    remHabit: 'Sela bertanya sekali setelah Subuh, Dzuhur, dan Ashar. Jawab Sudah atau Nanti.',
    altRemAdhan: 'Kartu masuk waktu sholat dengan tombol Hentikan adzan',
    altRemHabit: 'Kartu yang menanyakan sholawat, dengan tombol Sudah dan Nanti',
    altRemHadith: 'Kartu hadits harian dengan cuplikan singkat dan tombol Baca',

    gridEyebrow: 'Fitur',
    gridTitle: 'Pill kecil, seharian penuh.',
    fPrayerT: 'Jadwal sholat di mana saja',
    fPrayer: 'Jadwal Kemenag RI untuk kota di Indonesia. Aladhan untuk kota lain, dengan metode seperti Muslim World League, ISNA, atau Umm al-Qura, dan bisa memakai Ashar mazhab Hanafi.',
    fRemT: 'Pengingat sholat',
    fRem: 'Beberapa menit sebelum waktunya dan saat masuk waktu, dengan countdown jam, menit, dan detik.',
    fAdhanT: 'Adzan',
    fAdhan: 'Pilihan per waktu, volume, mode bisu, dan file adzan sendiri, termasuk khusus Subuh.',
    fHabitT: 'Dzikir dan sholawat',
    fHabit: 'Pengingat lembut setelah Subuh, Dzuhur, dan Ashar. Jawab Sudah atau Nanti.',
    fHadithT: 'Hadits harian',
    fHadith: 'Arbain Nawawi, dalam teks Arab beserta terjemahan Indonesia atau Inggris.',
    fMeetT: 'Agenda meeting',
    fMeet: 'Outlook (aplikasi klasik) dan Google Calendar, lengkap dengan tanda sumbernya dan tombol Join untuk Teams, Zoom, Google Meet, dan Webex.',
    fLangT: 'Bahasa Indonesia dan Inggris',
    fLang: 'Mengikuti bahasa Windows secara default, dan bisa diganti kapan saja di Pengaturan.',
    fPlaceT: 'Bisa dipindah',
    fPlace: 'Geser notch ke kiri, kanan, atau ke tepi bawah layar. Sela mengingat posisinya.',
    fOpenT: 'Gratis dan open source',
    fOpen: 'Dirilis dengan lisensi MIT. Kamu bebas memakai, mengubah, dan membagikannya.',

    mascotEyebrow: 'Notch saat diam',
    mascotTitle: 'Bulan sabit kecil yang mengikuti langit.',
    mascotLede: 'Fajar, siang, sore, dan malam punya tampilan sendiri. Maskotnya bergerak pelan dan melirik ke arah kursor, sementara sholat berikutnya terus menghitung mundur di sebelahnya.',
    skiesLabel: 'Notch sepanjang hari',
    altDawn: 'Fajar: bulan sabit ungu, sholat berikutnya dan countdown dengan detik',
    altDay: 'Siang: matahari kecil dengan sinar, sholat berikutnya dan countdown',
    altDusk: 'Sore: bulan sabit jingga, sholat berikutnya dan countdown',
    altNight: 'Malam: bulan sabit dengan bintang, sholat berikutnya dan countdown',
    skyDawn: 'Fajar',
    skyDay: 'Siang',
    skyDusk: 'Sore',
    skyNight: 'Malam',

    motionEyebrow: '20 detik',
    motionTitle: 'Lihat cara kerjanya.',
    motionLede: 'Notch, panel jadwal sholat, meeting, adzan, dan hadits harian dalam satu video singkat.',
    motionNote: 'Video diputar tanpa suara. Nyalakan suara lewat kontrol pemutar.',
    videoLabel: 'Video peluncuran Sela: notch, panel jadwal sholat, meeting, adzan, dan hadits harian',

    lightEyebrow: 'Ringan di PC kamu',
    lightTitle: 'Tidak ada jendela yang perlu diatur. Cukup duduk di tepi layar.',
    deskFs: 'Ada aplikasi layar penuh? Notch menyingkir.',
    lOutT: 'Tidak mengganggu',
    lOut: 'Notch disembunyikan saat aplikasi layar penuh berjalan, tidak mengambil fokus, dan tidak muncul di taskbar.',
    lOffT: 'Tetap jalan saat offline',
    lOff: 'Jadwal dan hadits disimpan di perangkat. Klik Segarkan kapan saja untuk mengambil data terbaru.',
    lInstT: 'Mudah dipasang',
    lInst: 'Windows 10 atau 11 (64-bit), tanpa perlu memasang .NET. Installer memasang Sela untuk akun kamu saja, tanpa hak admin.',
    privT: 'Tanpa server. Data kamu tetap di perangkat.',
    priv: 'Sela tidak punya server dan tidak mengirim data pribadi ke mana pun. Sela hanya menghubungi:',
    h1: 'jadwal sholat dan hadits berbahasa Indonesia',
    h2: 'jadwal sholat di luar Indonesia, memakai koordinat kota yang kamu pilih',
    h3: 'pencarian kota',
    h4: 'hadits berbahasa Inggris',
    h5c: 'alamat iCal kamu',
    h5: 'alamat iCal Google yang kamu masukkan, disimpan terenkripsi dengan Windows DPAPI',

    setEyebrow: 'Pengaturan',
    setTitle: 'Atur sesukamu.',
    sLangT: 'Bahasa',
    sLang: 'Biarkan Ikuti Windows, atau pilih Indonesia atau English.',
    sLocT: 'Lokasi dan metode',
    sLoc: 'Ketik nama kota kamu, lalu pilih dari daftar. Untuk kota di luar Indonesia, pilih metode perhitungan yang sesuai dengan masjid setempat, dan nyalakan Ashar mazhab Hanafi kalau kamu mengikutinya.',
    sAdhanT: 'Adzan',
    sAdhan: 'Atur per waktu, pilih volume, atau pakai file adzan sendiri, termasuk khusus Subuh.',
    sMeetT: 'Meeting',
    sMeet: 'Nyalakan Outlook (aplikasi klasik), atau tempel alamat rahasia iCal dari Google Calendar kamu.',
    altSettings: 'Jendela pengaturan berbahasa Indonesia dengan bagian Bahasa, Lokasi, dan Sholat',

    dlTitle: 'Ambil jeda sejenak hari ini.',
    dlLede: 'Gratis dan open source. Windows 10 atau 11 (64-bit). Kamu tidak perlu memasang .NET.',
    dlInstaller: 'Unduh installer',
    dlAll: 'Semua file rilis',
    dlPortable: 'Unduh versi portable',
    dlInstT: 'Installer',
    dlInst: 'Sela dipasang untuk akun kamu saja, tanpa hak admin, dan muncul di Start Menu.',
    dlPortT: 'Portable',
    dlPort: 'Satu file yang langsung dijalankan tanpa dipasang.',
    dlNote: 'File rilis belum ditandatangani secara digital, jadi Windows SmartScreen mungkin menampilkan peringatan. Klik <b>Info lainnya</b>, lalu <b>Tetap jalankan</b>. Kamu bisa mencocokkan file dengan <code>SHA256SUMS.txt</code> di halaman rilis.',
    dlAfter: 'Setelah dijalankan, ikon Sela muncul di tray dan notch muncul di atas layar.',
    version: 'Versi',

    footTag: 'Jeda sejenak di sela kesibukan.',
    footNav: 'Tautan proyek',
    footRoadmap: 'Roadmap',
    footLicense: 'Lisensi MIT',
    footBrand: 'Brand kit',
    footPrivacy: 'Privasi',
    footCredits: 'Jadwal sholat dari Kemenag RI melalui api.myquran.com, dan dari Aladhan. Screenshot memakai data contoh.',
    title: 'Sela · Jeda sejenak di sela kesibukan',
    description: 'Sela adalah notch hitam kecil di atas layar Windows untuk jadwal sholat, adzan, pengingat dzikir dan hadits harian, serta meeting dari Outlook dan Google Calendar. Gratis dan open source.'
  };

  const KEY = 'sela-lang';
  const root = document.documentElement;
  const EN = {};               // filled from the HTML on first run
  const release = { version: '', sizeMb: 0, portable: '' };
  let lang = 'en';

  // Capture English (the HTML source of truth) once.
  const textNodes = document.querySelectorAll('[data-i18n]');
  textNodes.forEach(el => { EN[el.dataset.i18n] = el.innerHTML; });
  document.querySelectorAll('[data-i18n-alt]').forEach(el => { EN[el.dataset.i18nAlt] = el.alt; });
  document.querySelectorAll('[data-i18n-aria]').forEach(el => { EN[el.dataset.i18nAria] = el.getAttribute('aria-label'); });
  EN.title = document.title;
  const metaDesc = document.querySelector('meta[name="description"]');
  EN.description = metaDesc.content;
  EN.version = 'Version';
  EN.dlPortable = 'Download the portable version';

  const t = k => (lang === 'id' ? ID[k] : EN[k]) ?? EN[k] ?? k;

  function pickInitial() {
    let saved = null;
    try { saved = localStorage.getItem(KEY); } catch { /* storage blocked */ }
    if (saved === 'en' || saved === 'id') return saved;
    return (navigator.language || '').toLowerCase().startsWith('id') ? 'id' : 'en';
  }

  function apply(next, save) {
    lang = next;
    root.lang = next;
    textNodes.forEach(el => {
      const v = t(el.dataset.i18n);
      if (el.innerHTML !== v) el.innerHTML = v;   // dictionary is static, trusted markup
    });
    document.querySelectorAll('[data-i18n-alt]').forEach(el => { el.alt = t(el.dataset.i18nAlt); });
    document.querySelectorAll('[data-i18n-aria]').forEach(el => { el.setAttribute('aria-label', t(el.dataset.i18nAria)); });
    document.querySelectorAll('img[data-src]').forEach(img => {
      const src = `assets/img/${next}/${img.dataset.src}`;
      if (img.getAttribute('src') !== src) img.setAttribute('src', src);
    });
    document.querySelectorAll('[data-href-id]').forEach(a => {
      a.dataset.hrefEn ??= a.getAttribute('href');
      a.setAttribute('href', next === 'id' ? a.dataset.hrefId : a.dataset.hrefEn);
    });
    document.title = t('title');
    metaDesc.content = t('description');
    document.querySelectorAll('.lang button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.lang === next)));
    swapVideo(next);
    renderRelease();
    if (save) { try { localStorage.setItem(KEY, next); } catch { /* ignore */ } }
  }

  // Video: never autoplay with sound; no autoplay at all under reduced motion.
  const video = document.getElementById('brag');
  const reduce = matchMedia('(prefers-reduced-motion: reduce)');
  let videoVisible = false;

  function swapVideo(next) {
    if (!video) return;
    const src = `media/brag-${next}.mp4`;
    if (video.getAttribute('src') === src) return;
    const wasPlaying = !video.paused;
    video.poster = `media/brag-${next}.jpg`;
    video.src = src;
    video.muted = true;
    video.load();
    if (wasPlaying || (videoVisible && !reduce.matches)) video.play().catch(() => {});
  }

  if (video && 'IntersectionObserver' in window) {
    new IntersectionObserver(entries => {
      for (const e of entries) {
        videoVisible = e.isIntersecting;
        if (reduce.matches) continue;
        if (e.isIntersecting) { if (video.muted) video.play().catch(() => {}); }
        else video.pause();
      }
    }, { threshold: 0.5 }).observe(video);
  }

  // Slow, one-time reveals.
  const reveals = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !reduce.matches) {
    const io = new IntersectionObserver(entries => {
      for (const e of entries) if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    reveals.forEach(el => io.observe(el));
  } else {
    reveals.forEach(el => el.classList.add('in'));
  }

  // Release info (progressive enhancement; on failure the releases link stays).
  function renderRelease() {
    if (!release.version) return;
    const meta = `${t('version')} ${release.version} · ${release.sizeMb} MB`;
    document.querySelectorAll('[data-dl-meta]').forEach(el => { el.textContent = meta; });
    const p = document.querySelector('[data-dl-portable]');
    if (p && release.portable) {
      p.href = release.portable;
      p.textContent = t('dlPortable');
    }
  }

  async function loadRelease() {
    try {
      const ctrl = new AbortController();
      const timer = setTimeout(() => ctrl.abort(), 6000);
      const res = await fetch('https://api.github.com/repos/agusumri/sela/releases/latest', {
        headers: { Accept: 'application/vnd.github+json' }, signal: ctrl.signal, referrerPolicy: 'no-referrer'
      });
      clearTimeout(timer);
      if (!res.ok) return;
      const data = await res.json();
      const assets = Array.isArray(data.assets) ? data.assets : [];
      const ok = u => typeof u === 'string' && u.startsWith('https://github.com/agusumri/sela/releases/download/');
      const setup = assets.find(a => /^Sela-Setup-.*\.exe$/i.test(a.name) && ok(a.browser_download_url));
      if (!setup) return;
      const portable = assets.find(a => /portable\.exe$/i.test(a.name) && ok(a.browser_download_url));
      release.version = String(data.tag_name || '').replace(/^v/i, '').slice(0, 20);
      release.sizeMb = Math.max(1, Math.round(setup.size / 1048576));
      release.portable = portable ? portable.browser_download_url : '';
      document.querySelectorAll('[data-dl]').forEach(a => { a.href = setup.browser_download_url; });
      renderRelease();
    } catch { /* keep the releases page link */ }
  }

  document.querySelectorAll('.lang button').forEach(b => {
    b.addEventListener('click', () => apply(b.dataset.lang, true));
  });

  apply(pickInitial(), false);
  loadRelease();
})();
