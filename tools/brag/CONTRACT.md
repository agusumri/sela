# /brag render contract (Sela launch video)

Two parts, owned by different lanes. Keep this interface stable.

## scene.html (visual lane)

- Lives at `tools/brag/scene.html` (plus any `tools/brag/*.css` / `*.js` it loads by relative path).
- Opened by the harness as `file:///.../tools/brag/scene.html?lang=en&clips=<fileURL>&assets=<fileURL>`
  - `lang`: `en` or `id`.
  - `clips`: file URL of `brag-output/work/clips/<lang>/` (trailing slash). Inside it, one folder per clip,
    each with frames `0001.png`, `0002.png`, … at 30 fps starting at the clip's onset (first frame where the
    app is visible). Folders: `idle-night idle-dawn idle-day idle-dusk panel-prayer panel-agenda panel-hadith
    reminder-adhan reminder-habit`. `clips/<lang>/manifest.json` = `{ "<name>": { "frames": N, "width": W, "height": H } }`.
  - `assets`: file URL of the repo root (trailing slash), e.g. for `src/Sela/Assets/sela-256.png`.
- Page size 1080×1920 CSS px, `html,body{margin:0;overflow:hidden;background:#0E1116}`.
- Must define `window.render = async (t) => { ... }` — draws the frame for time `t` seconds (0 ≤ t < 20) as a pure
  function of `t`, awaits all images it shows (`img.decode()`), and resolves when the frame is ready.
  No wall-clock CSS animations or transitions; everything is computed from `t`.
- Must define `window.ready = Promise` that resolves after fonts (`document.fonts.ready`) and the manifest are loaded.
- Optional: `window.layoutReport = () => [{ id, text, x, y, w, h }]` for every visible text element, so the
  harness can check overflow and the safe area.

## Harness (render lane)

- `tools/make-brag.ps1 [-Languages en,id] [-SkipExtract] [-Frames 600] [-Only <t,...>]`
  1. Extract clip frames (onset-trimmed, 30 fps PNG) to `brag-output/work/clips/<lang>/<name>/` + manifest.
  2. Launch headless Chrome/Edge, open scene.html, `await window.ready`, then for each frame `f`:
     `await window.render(f/30)` → `Page.captureScreenshot` → `brag-output/work/frames/<lang>/<f:0000>.png`.
     `-Only` renders just the listed times to `brag-output/work/stills/<lang>/t<time>.png` (for checks).
  3. Encode `brag-output/work/brag-<lang>-video.mp4` (1080×1920, 30/1, 600 frames, H.264 yuv420p, CRF 18, +faststart, no audio).
  4. Always close the browser and ffmpeg it started.

## Timeline (from brag-plan.md, 120 BPM, cuts on beats)
0–2.5 hook · 2.5–5.5 reveal · 5.5–9.5 schedule · 9.5–13.5 meetings + adhan · 13.5–17.0 dhikr + hadith · 17.0–20.0 outro.
Sound events the visuals should land on: cursor click at 6.2 s, adhan card pop at 11.5 s.
