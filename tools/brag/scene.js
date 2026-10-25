/* Sela /brag scene. Implements tools/brag/CONTRACT.md: window.ready, window.render(t), window.layoutReport.
   Every visual property is computed from t; nothing runs on the wall clock. */
(() => {
  'use strict';

  // ---------- params ----------
  const q = new URLSearchParams(location.search);
  const LANG = q.get('lang') === 'id' ? 'id' : 'en';
  const CLIPS = q.get('clips') || '';            // already a file URL with trailing slash: concatenate only
  const ASSETS = q.get('assets') || '../../';
  document.documentElement.lang = LANG;

  const FPS = 30, W = 1080, EDGE = 236;          // EDGE: page y of the screen's top edge (the notch hangs here)
  const SAFE = { x0: 60, x1: 1020, y0: 220, y1: 1500 };

  // ---------- copy (exact from brag-output/brag-plan.md) ----------
  const COPY = {
    en: {
      countLabel: 'Asr in', units: ['h', 'm', 's'],
      hook: ['Never miss a prayer at your desk.', [3]],
      tagline: 'A notch for prayer times on Windows',
      schedule: ['Your whole day, one click', [3]],
      meetings: ['Meetings and the adhan, side by side', [3]],
      daily: ['Dhikr reminders & a daily hadith', [0, 5]],
      free: 'Free & open source',
      sky: ['Dawn', 'Day', 'Dusk', 'Night'],
    },
    id: {
      countLabel: 'Ashar dalam', units: ['j', 'm', 'd'],
      hook: ['Jangan lewatkan sholat di depan layar.', [2]],
      tagline: 'Notch jadwal sholat untuk Windows',
      schedule: ['Jadwal lengkap, sekali klik', [3]],
      meetings: ['Meeting dan adzan berdampingan', [2]],
      daily: ['Pengingat dzikir & hadits harian', [1, 3]],
      free: 'Gratis & open source',
      sky: ['Fajar', 'Siang', 'Sore', 'Malam'],
    },
  }[LANG];
  const URL_TEXT = 'github.com/agusumri/sela';

  // ---------- timeline (seconds; 120 BPM, cuts at 2.5 5.5 9.5 13.5 17.0) ----------
  const T = {
    pillDrop: 0.00,
    countIn: 0.15, countOut: 2.50,
    hookIn: 0.15, hookOut: 2.95,   // 7 words settle by ~0.85 s, hold 2.1 s, gone before the tagline
    brandIn: 2.75, brandOut: 5.60,
    tagIn: 3.25, tagOut: 6.10,   // out before the panel unfolds (6.26) and the schedule caption (6.4)
    cursorIn: 5.45, click: 6.20,
    prayerOpen: 6.26, prayerClose: 9.22,
    schedIn: 6.40, schedOut: 9.28,
    agendaOpen: 9.60, agendaClose: 11.05,
    meetIn: 9.75, meetOut: 13.25,
    adhanPop: 11.50, adhanClose: 13.20,
    habitPop: 13.58, habitClose: 14.85,
    dailyIn: 13.65, dailyOut: 16.68,     // everything clears by 17.0 so the outro starts from a clean background
    hadithOpen: 15.12, hadithClose: 16.66,
    skyIn: 15.35, skyOut: 16.66,
    outro: 17.00, outroBrand: 17.20, freeIn: 17.55, urlIn: 17.90,
  };

  // ---------- easing (all pure) ----------
  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  const lerp = (a, b, p) => a + (b - a) * p;
  const easeOutCubic = p => 1 - Math.pow(1 - clamp(p), 3);
  const easeInCubic = p => Math.pow(clamp(p), 3);
  const easeInOutCubic = p => { p = clamp(p); return p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2; };
  const easeOutQuint = p => 1 - Math.pow(1 - clamp(p), 5);
  /** Damped spring step response (0 → 1, may overshoot). dt in seconds. */
  function spring(dt, freq = 1.5, zeta = 0.6) {
    if (dt <= 0) return 0;
    const w = 2 * Math.PI * freq, wd = w * Math.sqrt(1 - zeta * zeta);
    return 1 - Math.exp(-zeta * w * dt) * (Math.cos(wd * dt) + (zeta * w / wd) * Math.sin(wd * dt));
  }
  /** Envelope: quick attack, long exponential decay. */
  const pulse = (t, t0, attack = 0.22, decay = 1.1) =>
    t < t0 ? 0 : (t - t0 < attack ? easeOutCubic((t - t0) / attack) : Math.exp(-(t - t0 - attack) / decay));

  // ---------- DOM ----------
  const $ = id => document.getElementById(id);
  const stage = $('stage'), fg = $('fg');
  const el = (tag, cls, parent, html) => {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    parent.appendChild(e);
    return e;
  };

  let manifest = {};
  const pending = [];

  // Clip layers. content = [x0, y0, x1, y1] of the real UI inside a capture (physical px), measured from the frames.
  const DEF = {
    pill:   { clip: 'idle-night',     scale: 2.10, mask: 'feather-pill',   size: [438, 88],  content: [32, 0, 406, 44],  r: '0 0 999px 999px' },
    prayer: { clip: 'panel-prayer',   scale: 1.55, mask: 'feather-prayer', size: [600, 660], content: [32, 0, 568, 470], r: '0 0 44px 44px' },
    agenda: { clip: 'panel-agenda',   scale: 1.55, mask: 'feather-agenda', size: [600, 660], content: [32, 0, 568, 518], r: '0 0 44px 44px' },
    hadith: { clip: 'panel-hadith',   scale: 1.40, mask: 'feather-hadith', size: [600, 660], content: [32, 0, 568, 614], r: '0 0 44px 44px' },
    adhan:  { clip: 'reminder-adhan', scale: 1.55, mask: 'feather-card',   size: [654, 180], content: [32, 0, 622, 136], r: '0 0 40px 40px' },
    habit:  { clip: 'reminder-habit', scale: 1.55, mask: 'feather-card',   size: [654, 180], content: [32, 0, 620, 136], r: '0 0 40px 40px' },
  };
  const SKY = ['idle-dawn', 'idle-day', 'idle-dusk', 'idle-night'];
  SKY.forEach((clip, i) => { DEF['sky' + i] = { clip, scale: 0.56, mask: 'feather-pill', size: [438, 88], content: [32, 0, 406, 44], r: '0 0 999px 999px' }; });

  const layers = {};
  function buildLayer(key) {
    const d = DEF[key], m = manifest[d.clip];
    const [sw, sh] = m ? [m.width, m.height] : d.size;
    const w = sw * d.scale, h = sh * d.scale;
    const box = el('div', 'layer ' + d.mask, stage);
    box.style.width = w + 'px'; box.style.height = h + 'px';
    box.style.left = (W - w) / 2 + 'px';
    const img = el('img', null, box); img.alt = ''; img.decoding = 'sync';
    const ph = el('div', 'ph', box);
    const [cx0, cy0, cx1, cy1] = d.content;
    Object.assign(ph.style, { left: cx0 * d.scale + 'px', top: cy0 * d.scale + 'px', width: (cx1 - cx0) * d.scale + 'px', height: (cy1 - cy0) * d.scale + 'px', borderRadius: d.r });
    if (!m) box.classList.add('missing');
    layers[key] = { d, m, box, img, w, h, contentFrac: cy1 / sh, cur: '' };
  }

  /** Frame index for a clip started at t0. mode 'play' clamps on the last frame; 'tail' ping-pongs the settled tail. */
  function frameAt(L, t0, t, mode = 'play') {
    const N = L.m ? L.m.frames : 1;
    const k = Math.max(0, Math.floor((t - t0) * FPS + 1e-6));
    if (mode === 'tail') {
      const base = Math.max(1, N - 11), span = N - base;
      if (span <= 0) return N;
      const pos = k % (2 * span);
      return base + (pos <= span ? pos : 2 * span - pos);
    }
    return Math.min(N, k + 1);
  }

  /** Show a layer. reveal: 0..1 how much of the UI is unfolded from the top edge. */
  function showLayer(key, { frame, y = 0, x = 0, scale = 1, opacity = 1, reveal = 1 }) {
    const L = layers[key];
    if (opacity <= 0.001 || reveal <= 0.001) { L.box.style.visibility = 'hidden'; return; }
    L.box.style.visibility = 'visible';
    L.box.style.opacity = opacity.toFixed(4);
    L.box.style.transform = `translate(${x.toFixed(2)}px, ${y.toFixed(2)}px) scale(${scale.toFixed(4)})`;
    const shown = clamp(reveal * (L.contentFrac + 0.06));
    L.box.style.clipPath = shown >= 0.999 ? 'none' : `inset(0 0 ${((1 - shown) * 100).toFixed(3)}% 0)`;
    if (L.m && frame) {
      const src = CLIPS + L.d.clip + '/' + String(frame).padStart(4, '0') + '.png';
      if (L.cur !== src) {
        L.cur = src; L.img.src = src;
        pending.push(L.img.decode().then(() => L.box.classList.remove('missing'), () => L.box.classList.add('missing')));
      }
    }
  }
  const hideLayer = key => { layers[key].box.style.visibility = 'hidden'; };

  // ---------- text ----------
  const texts = [];
  function makeText(id, cls, text, acc = [], parent = fg) {
    const box = el('div', 'txt ' + cls, parent); box.id = id;
    const words = text.split(' ').map((w, i) => {
      if (i) box.appendChild(document.createTextNode(' '));
      return el('span', 'w' + (acc.includes(i) ? ' acc' : ''), box, null);
    });
    text.split(' ').forEach((w, i) => { words[i].textContent = w; });
    const rec = { id, box, words, text, alpha: 0 };
    texts.push(rec);
    return rec;
  }

  /** Word-by-word in (stagger, 18 px rise, ease-out), hold, then a short unified exit. Returns max word opacity. */
  function animText(rec, t, tIn, tOut = Infinity, o = {}) {
    const { stagger = 0.045, dur = 0.42, rise = 18, outDur = 0.22, dy = 0 } = o;
    if (t < tIn || t >= tOut + outDur) { rec.box.style.visibility = 'hidden'; rec.alpha = 0; return 0; }
    rec.box.style.visibility = 'visible';
    rec.box.style.transform = dy ? `translateY(${dy.toFixed(2)}px)` : '';
    const qOut = t >= tOut ? easeInCubic((t - tOut) / outDur) : 0;
    let max = 0;
    rec.words.forEach((w, i) => {
      const e = easeOutCubic((t - tIn - i * stagger) / dur);
      const a = e * (1 - qOut);
      w.style.opacity = a.toFixed(4);
      w.style.transform = `translateY(${((1 - e) * rise - qOut * 14).toFixed(2)}px)`;
      max = Math.max(max, a);
    });
    rec.alpha = max;
    return max;
  }

  // Built per language in build().
  let R = {};

  function build() {
    Object.keys(DEF).forEach(buildLayer);

    // Click ripple: page space, above the notch.
    R.ripple = el('div', null, fg); R.ripple.id = 'ripple';

    // Countdown: label + rolling digits.
    const count = el('div', 'txt', fg); count.id = 'count';
    const label = el('div', 'label', count);
    label.textContent = COPY.countLabel;
    const digits = el('div', 'digits', count);
    const slot = () => { const d = el('span', 'dg', digits); return { a: el('span', null, d), b: el('span', null, d) }; };
    const unit = (u, last) => { const s = el('span', 'unit' + (last ? ' last' : ''), digits); s.textContent = u; return s; };
    R.count = { box: count, label, digits, m1: slot(), m2: slot(), u1: unit(COPY.units[1]), s1: slot(), s2: slot(), u2: unit(COPY.units[2], true) };
    texts.push({ id: 'count', box: count, words: null, get text() { return (label.textContent + ' ' + R.count.str).trim(); }, alpha: 0 });

    R.hook = makeText('hook', 'caption', COPY.hook[0], COPY.hook[1]);
    R.hook.box.style.top = '1170px';

    // Brand block: logo, wordmark, accent bar (used for reveal and outro).
    const brand = el('div', 'txt', fg); brand.id = 'brand';
    R.logo = el('img', null, brand); R.logo.alt = 'Sela logo';
    R.logo.src = ASSETS + 'src/Sela/Assets/sela-256.png';
    pending.push(R.logo.decode().catch(() => { R.logo.style.visibility = 'hidden'; R.logoMissing = true; }));
    const wordWrap = el('div', null, brand);
    R.word = el('span', 'word', wordWrap); R.word.textContent = 'Sela';
    R.bar = el('div', 'bar', brand);
    R.brand = brand;
    texts.push({ id: 'wordmark', box: R.word, words: null, text: 'Sela', alpha: 0 });

    R.tag = makeText('tagline', 'sub', COPY.tagline);
    R.tag.box.style.top = '1062px';

    R.sched = makeText('schedule', 'caption', COPY.schedule[0], COPY.schedule[1]);
    R.sched.box.style.top = '1110px';
    R.meet = makeText('meetings', 'caption', COPY.meetings[0], COPY.meetings[1]);
    R.meet.box.style.top = '1170px';
    R.daily = makeText('daily', 'caption', COPY.daily[0], COPY.daily[1]);
    R.daily.box.style.top = '1296px';

    R.sky = COPY.sky.map((s, i) => {
      const r = makeText('sky-' + i, 'sky-label', s);
      r.box.style.left = (540 + (i - 1.5) * 236 - 116) + 'px';
      r.box.style.top = '1206px';
      return r;
    });

    R.free = makeText('free', 'caption', COPY.free);
    R.free.box.style.top = '1068px';
    R.url = makeText('url', 'url-line', URL_TEXT);
    R.url.box.id = 'url';
    R.url.box.style.top = '1190px';

    // Cursor: a plain arrow.
    R.cursor = el('div', null, fg,
      '<svg width="46" height="62" viewBox="0 0 46 62" aria-hidden="true"><path d="M5 3 L5 49 L16.5 38.5 L24.5 57 L32.5 53.5 L24.8 35.6 L40 35.6 Z" fill="#F5F5F7" stroke="#0B0D11" stroke-width="2.6" stroke-linejoin="round"/></svg>');
    R.cursor.id = 'cursor';
  }

  // ---------- scene pieces ----------
  const pad2 = n => String(n).padStart(2, '0');

  function drawCountdown(t) {
    const c = R.count, rec = texts.find(x => x.id === 'count');
    const tIn = T.countIn, tOut = T.countOut;
    if (t < tIn || t >= tOut + 0.3) { c.box.style.visibility = 'hidden'; rec.alpha = 0; return; }
    c.box.style.visibility = 'visible';
    const e = easeOutCubic((t - tIn) / 0.6);
    const qo = t >= tOut ? easeInCubic((t - tOut) / 0.28) : 0;
    const a = e * (1 - qo);
    c.box.style.opacity = a.toFixed(4);
    c.box.style.transform = `translateY(${((1 - e) * 30 - qo * 46).toFixed(2)}px) scale(${(0.97 + 0.03 * e).toFixed(4)})`;
    rec.alpha = a;

    // 25m 04s at t = 0, ticking down every whole second.
    const total = 25 * 60 + 4;
    const sec = Math.floor(t), frac = t - sec;
    const now = total - sec, prev = now + 1;
    const fmt = v => pad2(Math.floor(v / 60)) + pad2(v % 60);
    const sN = fmt(now), sP = fmt(prev);
    c.str = `${sN.slice(0, 2)}${COPY.units[1]} ${sN.slice(2)}${COPY.units[2]}`;
    const roll = easeOutCubic(frac / 0.32);
    [c.m1, c.m2, c.s1, c.s2].forEach((s, i) => {
      s.a.textContent = sN[i];
      if (sec >= 1 && sN[i] !== sP[i] && frac < 0.32) {
        s.b.textContent = sP[i];
        s.a.style.transform = `translateY(${(-(1 - roll) * 100).toFixed(2)}%)`;
        s.a.style.opacity = roll.toFixed(4);
        s.b.style.transform = `translateY(${(roll * 100).toFixed(2)}%)`;
        s.b.style.opacity = (1 - roll).toFixed(4);
        s.b.style.visibility = 'visible';
      } else {
        s.a.style.transform = ''; s.a.style.opacity = '1';
        s.b.style.visibility = 'hidden';
      }
    });
    // Seconds ones digit carries the green tick accent for a moment.
    c.s2.a.style.color = frac < 0.3 && sec >= 1 ? `rgba(${lerp(61, 245, roll) | 0},${lerp(220, 245, roll) | 0},${lerp(151, 247, roll) | 0},1)` : '';
  }

  function drawBrand(t) {
    const brandRec = texts.find(x => x.id === 'wordmark');
    let tb, tOut;
    if (t >= T.outroBrand) { tb = T.outroBrand; tOut = Infinity; }
    else if (t >= T.brandIn && t < T.brandOut + 0.36) { tb = T.brandIn; tOut = T.brandOut; }
    else { R.brand.style.visibility = 'hidden'; brandRec.alpha = 0; return; }
    R.brand.style.visibility = 'visible';
    const qo = t >= tOut ? easeInCubic((t - tOut) / 0.34) : 0;
    const dt = t - tb;
    // Logo: spring scale with a soft overshoot (the "lift").
    const ls = spring(dt, 1.25, 0.52);
    R.logo.style.opacity = (easeOutCubic(dt / 0.32) * (1 - qo)).toFixed(4);
    R.logo.style.transform = `translateY(${((1 - easeOutCubic(dt / 0.7)) * 34 - qo * 40).toFixed(2)}px) scale(${(0.8 + 0.2 * ls).toFixed(4)})`;
    // Wordmark rises a beat later.
    const we = easeOutQuint((dt - 0.14) / 0.75);
    const wa = clamp((dt - 0.14) / 0.35) * (1 - qo);
    R.word.style.opacity = wa.toFixed(4);
    R.word.style.transform = `translateY(${((1 - we) * 46 - qo * 40).toFixed(2)}px)`;
    R.word.style.letterSpacing = `${lerp(0.02, -0.04, we).toFixed(4)}em`;
    brandRec.alpha = wa;
    // Accent bar draws outwards from the middle.
    const be = easeInOutCubic((dt - 0.4) / 0.6);
    R.bar.style.opacity = (clamp((dt - 0.4) / 0.2) * (1 - qo)).toFixed(4);
    R.bar.style.transform = `translateY(${(-qo * 40).toFixed(2)}px) scaleX(${be.toFixed(4)})`;
  }

  /** Panel fold: unfolds from the notch with ease-out, folds back up with ease-in. */
  function fold(t, tOpen, tClose, inDur = 0.55, outDur = 0.3) {
    if (t < tOpen || t >= tClose + outDur) return 0;
    return easeOutCubic((t - tOpen) / inDur) * (1 - easeInCubic((t - tClose) / outDur));
  }

  function drawCursor(t) {
    const c = R.cursor, rp = R.ripple;
    const t0 = T.cursorIn, tc = T.click;
    if (t < t0 || t > 7.1) { c.style.visibility = 'hidden'; rp.style.visibility = 'hidden'; return; }
    c.style.visibility = 'visible';
    // Glide on a soft arc to the pill, then drift away after the click.
    const P0 = [830, 1320], P1 = [965, 610], P2 = [560, 292], P3 = [905, 1080];
    let x, y, a = 1;
    if (t <= tc - 0.08) {
      const p = easeInOutCubic((t - t0) / (tc - 0.08 - t0));
      x = (1 - p) * (1 - p) * P0[0] + 2 * (1 - p) * p * P1[0] + p * p * P2[0];
      y = (1 - p) * (1 - p) * P0[1] + 2 * (1 - p) * p * P1[1] + p * p * P2[1];
      a = clamp((t - t0) / 0.18);
    } else if (t < tc + 0.32) { x = P2[0]; y = P2[1]; }
    else {
      const p = easeInOutCubic((t - tc - 0.32) / 0.55);
      x = lerp(P2[0], P3[0], p); y = lerp(P2[1], P3[1], p);
      a = 1 - clamp((t - tc - 0.5) / 0.38);
    }
    // Press: quick dip and spring back.
    const press = t >= tc - 0.06 && t < tc + 0.3 ? Math.sin(Math.PI * clamp((t - tc + 0.06) / 0.24)) : 0;
    c.style.opacity = a.toFixed(4);
    c.style.transform = `translate(${(x - 5).toFixed(2)}px, ${(y - 3).toFixed(2)}px) scale(${(1 - 0.16 * press).toFixed(4)})`;
    // Ripple from the click point.
    const rpP = (t - tc) / 0.6;
    if (rpP >= 0 && rpP < 1) {
      const r = lerp(12, 96, easeOutCubic(rpP));
      rp.style.visibility = 'visible';
      Object.assign(rp.style, { left: (P2[0] - r) + 'px', top: (P2[1] - r) + 'px', width: 2 * r + 'px', height: 2 * r + 'px', opacity: ((1 - rpP) * 0.85).toFixed(4) });
    } else rp.style.visibility = 'hidden';
  }

  function drawBackground(t) {
    const ga = $('glow-a'), gb = $('glow-b'), gx = $('glow-accent');
    // Ambient green glow drifts on slow, incommensurate sines.
    const ax = 540 + 170 * Math.sin(2 * Math.PI * t / 17 + 0.4);
    const ay = 760 + 210 * Math.sin(2 * Math.PI * t / 23 + 0.6);
    const as = 1 + 0.06 * Math.sin(2 * Math.PI * t / 11);
    ga.style.transform = `translate(${(ax - 750).toFixed(2)}px, ${(ay - 750).toFixed(2)}px) scale(${as.toFixed(4)})`;
    ga.style.opacity = (0.85 + 0.15 * Math.sin(2 * Math.PI * t / 9)).toFixed(4);
    // Cool counter-glow, a touch stronger while meetings are on screen.
    const bx = 540 - 260 * Math.cos(2 * Math.PI * t / 19), by = 1380 + 110 * Math.sin(2 * Math.PI * t / 13);
    gb.style.transform = `translate(${(bx - 650).toFixed(2)}px, ${(by - 650).toFixed(2)}px)`;
    const meet = clamp((t - 9.5) / 0.6) * (1 - clamp((t - 11.3) / 0.6));
    gb.style.opacity = (0.55 + 0.6 * meet).toFixed(4);
    // Accent bloom on the big moments: reveal lift, adhan pop, outro.
    const reveal = 0.75 * pulse(t, T.brandIn + 0.05, 0.4, 1.4) * (1 - clamp((t - T.brandOut) / 0.4));
    const adhan = 0.95 * pulse(t, T.adhanPop, 0.16, 0.9) + 0.25 * clamp((t - T.adhanPop) / 0.3) * (1 - clamp((t - T.adhanClose) / 0.3));
    const outro = 0.7 * clamp((t - T.outroBrand) / 0.8);
    let gxX = 540, gxY = 760, s = 1;
    const ad = Math.max(adhan, 0);
    if (ad > reveal && ad > outro) { gxY = EDGE + 120; s = 0.62 + 0.12 * pulse(t, T.adhanPop, 0.16, 0.6); }
    gx.style.transform = `translate(${(gxX - 550).toFixed(2)}px, ${(gxY - 550).toFixed(2)}px) scale(${s.toFixed(4)})`;
    gx.style.opacity = clamp(Math.max(reveal, ad, outro)).toFixed(4);
  }

  // ---------- render ----------
  function draw(t) {
    drawBackground(t);

    // Panels / cards (drawn above the pill, which they cover).
    const pPrayer = fold(t, T.prayerOpen, T.prayerClose);
    const pAgenda = fold(t, T.agendaOpen, T.agendaClose);
    const pHadith = fold(t, T.hadithOpen, T.hadithClose, 0.6, 0.24);
    const cardPop = (t0, tClose, f, z) => {
      if (t < t0 || t >= tClose + 0.28) return null;
      const s = spring(t - t0, f, z);
      const qo = easeInCubic((t - tClose) / 0.28);
      return { scale: (0.7 + 0.3 * s) * (1 - 0.06 * qo), opacity: clamp((t - t0) / 0.1) * (1 - qo), reveal: easeOutCubic((t - t0) / 0.22) * (1 - qo) };
    };
    const aPop = cardPop(T.adhanPop, T.adhanClose, 1.7, 0.48);
    const hPop = cardPop(T.habitPop, T.habitClose, 1.4, 0.62);

    // Panels start 3 % small and spring into place while they unfold.
    pPrayer ? showLayer('prayer', { frame: frameAt(layers.prayer, T.prayerOpen, t), reveal: pPrayer, scale: 0.97 + 0.03 * spring(t - T.prayerOpen, 1.3, 0.6) }) : hideLayer('prayer');
    pAgenda ? showLayer('agenda', { frame: frameAt(layers.agenda, T.agendaOpen, t), reveal: pAgenda, scale: 0.97 + 0.03 * spring(t - T.agendaOpen, 1.3, 0.6) }) : hideLayer('agenda');
    pHadith ? showLayer('hadith', { frame: frameAt(layers.hadith, T.hadithOpen, t), reveal: pHadith, scale: 0.97 + 0.03 * spring(t - T.hadithOpen, 1.3, 0.6) }) : hideLayer('hadith');
    aPop ? showLayer('adhan', { frame: frameAt(layers.adhan, T.adhanPop, t, 'tail'), ...aPop }) : hideLayer('adhan');
    hPop ? showLayer('habit', { frame: frameAt(layers.habit, T.habitPop, t, 'tail'), ...hPop }) : hideLayer('habit');

    // The notch pill: drops in, hides under open panels, settles again for the outro.
    const cover = Math.max(pPrayer, pAgenda, pHadith, aPop ? aPop.reveal : 0, hPop ? hPop.reveal : 0);
    const pillA = 1 - clamp((cover - 0.12) / 0.2);
    if (t >= T.pillDrop && pillA > 0) {
      const outro = t >= T.outro;
      const drop = spring(t - T.pillDrop, 1.35, 0.62);
      const y = outro ? 0 : -(layers.pill.h * 0.6 + 24) * (1 - drop);
      // Click press squish, and the outro "settle" breath.
      const press = t >= T.click - 0.04 && t < T.click + 0.22 ? Math.sin(Math.PI * clamp((t - T.click + 0.04) / 0.26)) : 0;
      const settle = outro ? 1 + 0.05 * (1 - spring(t - T.outro, 1.2, 0.45)) : 1;
      const frame = outro ? frameAt(layers.pill, T.outro, t) : frameAt(layers.pill, T.pillDrop, t);
      showLayer('pill', { frame, y, scale: (1 - 0.04 * press) * settle, opacity: pillA });
    } else hideLayer('pill');

    // Sky strip under the hadith panel.
    SKY.forEach((_, i) => {
      const key = 'sky' + i, t0 = T.skyIn + i * 0.12;
      if (t < t0 || t >= T.skyOut + 0.26) { hideLayer(key); return; }
      const e = easeOutCubic((t - t0) / 0.5), qo = easeInCubic((t - T.skyOut) / 0.26);
      const L = layers[key];
      const cx = 540 + (i - 1.5) * 236;
      showLayer(key, {
        frame: frameAt(L, t0, t), x: cx - 540, y: 1146 - EDGE + (1 - e) * 22 + qo * 16,
        opacity: e * (1 - qo), scale: 0.92 + 0.08 * spring(t - t0, 1.4, 0.55),
      });
    });

    // Text.
    drawCountdown(t);
    animText(R.hook, t, T.hookIn, T.hookOut);
    drawBrand(t);
    animText(R.tag, t, T.tagIn, T.tagOut, { stagger: 0.04, rise: 18 });
    animText(R.sched, t, T.schedIn, T.schedOut);
    animText(R.meet, t, T.meetIn, T.meetOut);
    animText(R.daily, t, T.dailyIn, T.dailyOut);
    R.sky.forEach((r, i) => animText(r, t, T.skyIn + 0.1 + i * 0.12, T.skyOut, { dur: 0.4, rise: 12, outDur: 0.2 }));
    animText(R.free, t, T.freeIn, Infinity, { stagger: 0.055, dur: 0.5 });
    animText(R.url, t, T.urlIn, Infinity, { dur: 0.55, rise: 14 });

    drawCursor(t);
  }

  // ---------- self-check ----------
  function boxOf(rec) {
    const parts = rec.words ? rec.words : [rec.box];
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    const rects = rec.id === 'count' ? [R.count.label, ...R.count.digits.children] : parts;
    rects.forEach(p => {
      const r = p.getBoundingClientRect();
      if (!r.width && !r.height) return;
      x0 = Math.min(x0, r.left); y0 = Math.min(y0, r.top); x1 = Math.max(x1, r.right); y1 = Math.max(y1, r.bottom);
    });
    return x0 === Infinity ? null : { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
  }
  window.layoutReport = () => texts
    .filter(r => r.alpha > 0.02)
    .map(r => {
      const b = boxOf(r); if (!b) return null;
      const out = b.x < SAFE.x0 || b.x + b.w > SAFE.x1 || b.y < SAFE.y0 || b.y + b.h > SAFE.y1;
      const lines = r.words ? new Set(r.words.map(w => Math.round(w.offsetTop))).size : 1;
      return { id: r.id, text: r.text, x: +b.x.toFixed(1), y: +b.y.toFixed(1), w: +b.w.toFixed(1), h: +b.h.toFixed(1), opacity: +r.alpha.toFixed(3), lines, outOfSafe: out };
    })
    .filter(Boolean);
  window.overflowList = [];

  // ---------- contract ----------
  window.ready = (async () => {
    try { await document.fonts.ready; } catch { /* no font API: fall back to system fonts */ }
    try {
      const res = await fetch(CLIPS + 'manifest.json');
      manifest = res.ok ? await res.json() : {};
    } catch { manifest = {}; }
    build();
    // Fonts for every glyph we use must be in before the first capture.
    try { await document.fonts.load('600 76px "Segoe UI Variable Display"'); await document.fonts.load('400 42px "Segoe UI Variable Text"'); } catch { }
    await Promise.all(pending.splice(0));
  })();

  window.render = async (t) => {
    await window.ready;
    t = Math.max(0, Math.min(19.999, +t || 0));
    draw(t);
    await Promise.all(pending.splice(0));
    window.overflowList = window.layoutReport().filter(r => r.outOfSafe).map(r => r.id);
  };
})();
