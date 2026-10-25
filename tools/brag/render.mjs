// Headless render driver for the /brag launch video (see CONTRACT.md).
// Usage: node render.mjs --lang en [--frames 600] [--only 0,5.5,12] [--report]
// No npm deps: uses Node's built-in WebSocket/fetch and the Chrome DevTools Protocol.
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync, appendFileSync } from 'node:fs';
import { randomBytes } from 'node:crypto';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '..', '..');
const work = join(repo, 'brag-output', 'work');
const W = 1080, H = 1920, FPS = 30;

// ---- args ----
const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf('--' + name); return i >= 0 ? args[i + 1] : def; };
const lang = opt('lang', 'en');
if (!/^(en|id)$/.test(lang)) throw new Error(`bad --lang ${lang}`);
const frames = parseInt(opt('frames', '600'), 10);
const only = opt('only', null)?.split(',').filter(Boolean).map(Number);
if (only?.some(Number.isNaN)) throw new Error('bad --only list');
const report = args.includes('--report');

const browserPath = [
  process.env.BRAG_BROWSER,
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
].find(p => p && existsSync(p));
if (!browserPath) throw new Error('Chrome/Edge not found');

const tmpRoot = join(process.env.LOCALAPPDATA ?? process.env.TEMP, 'Temp');
const profile = join(tmpRoot, 'sela-brag-chrome-' + randomBytes(6).toString('hex'));

let child = null, ws = null, cleaned = false;
function cleanup() {
  if (cleaned) return; cleaned = true;
  try { ws?.close(); } catch {}
  if (child?.pid && child.exitCode === null) {
    // Kill only the tree we spawned (never the user's own browser windows).
    spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
  }
  for (let i = 0; i < 20; i++) {
    try { rmSync(profile, { recursive: true, force: true }); break; }
    catch { Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 250); }
  }
}
for (const sig of ['SIGINT', 'SIGTERM', 'SIGBREAK']) process.on(sig, () => { cleanup(); process.exit(130); });
process.on('exit', cleanup);

// ---- CDP client ----
let nextId = 1;
const pending = new Map();
const listeners = [];
function send(method, params = {}, sessionId, timeoutMs = 60000) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params, sessionId }));
  return new Promise((res, rej) => {
    const timer = setTimeout(() => { pending.delete(id); rej(new Error(`CDP timeout: ${method}`)); }, timeoutMs);
    pending.set(id, { res, rej, timer, method });
  });
}
function waitEvent(method, sessionId, timeoutMs = 60000) {
  return new Promise((res, rej) => {
    const timer = setTimeout(() => rej(new Error(`timeout waiting for ${method}`)), timeoutMs);
    listeners.push({ method, sessionId, fn: p => { clearTimeout(timer); res(p); } });
  });
}
async function evaluate(expression, sessionId) {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId);
  if (r.exceptionDetails) {
    const d = r.exceptionDetails;
    throw new Error(`page error in "${expression}": ${d.exception?.description ?? d.text}`);
  }
  return r.result?.value;
}

async function main() {
  mkdirSync(profile, { recursive: true });
  child = spawn(browserPath, [
    '--headless=new', '--remote-debugging-port=0', '--hide-scrollbars', '--force-device-scale-factor=1',
    '--allow-file-access-from-files', '--disable-gpu-vsync', '--mute-audio', `--window-size=${W},${H}`,
    '--no-first-run', '--no-default-browser-check', `--user-data-dir=${profile}`, 'about:blank',
  ], { stdio: 'ignore' });

  // Chrome writes "<port>\n<browser ws path>" once it is listening.
  const portFile = join(profile, 'DevToolsActivePort');
  const t0 = Date.now();
  while (!existsSync(portFile) || readFileSync(portFile, 'utf8').split('\n').length < 2) {
    if (child.exitCode !== null) throw new Error(`browser exited (${child.exitCode})`);
    if (Date.now() - t0 > 30000) throw new Error('DevToolsActivePort not written within 30 s');
    await new Promise(r => setTimeout(r, 100));
  }
  const [port, wsPath] = readFileSync(portFile, 'utf8').trim().split(/\r?\n/);
  ws = new WebSocket(`ws://127.0.0.1:${port}${wsPath}`);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = () => rej(new Error('CDP connect failed')); });
  ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id); clearTimeout(p.timer);
      m.error ? p.rej(new Error(`${p.method}: ${m.error.message}`)) : p.res(m.result);
    } else if (m.method) {
      for (let i = listeners.length - 1; i >= 0; i--) {
        const l = listeners[i];
        if (l.method === m.method && l.sessionId === m.sessionId) { listeners.splice(i, 1); l.fn(m.params); }
      }
    }
  };

  const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
  const { sessionId: s } = await send('Target.attachToTarget', { targetId, flatten: true });
  await send('Page.enable', {}, s);
  await send('Runtime.enable', {}, s);
  await send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false }, s);

  const clipsUrl = pathToFileURL(join(work, 'clips', lang)).href + '/';
  const assetsUrl = pathToFileURL(repo).href + '/';
  const sceneUrl = pathToFileURL(join(here, 'scene.html')).href +
    `?lang=${lang}&clips=${encodeURIComponent(clipsUrl)}&assets=${encodeURIComponent(assetsUrl)}`;
  const loaded = waitEvent('Page.loadEventFired', s);
  const nav = await send('Page.navigate', { url: sceneUrl }, s);
  if (nav.errorText) throw new Error(`navigate failed: ${nav.errorText}`);
  await loaded;
  await evaluate('window.ready', s);

  const times = only ?? Array.from({ length: frames }, (_, f) => f / FPS);
  const outDir = join(work, only ? 'stills' : 'frames', lang);
  if (!only) rmSync(outDir, { recursive: true, force: true });
  mkdirSync(outDir, { recursive: true });
  const layoutFile = join(work, 'stills', lang, 'layout.jsonl');
  if (report) { mkdirSync(dirname(layoutFile), { recursive: true }); writeFileSync(layoutFile, ''); }

  const start = Date.now();
  for (let i = 0; i < times.length; i++) {
    const t = times[i];
    await evaluate(`window.render(${t})`, s);
    if (report) {
      const items = await evaluate('window.layoutReport?.() ?? null', s);
      appendFileSync(layoutFile, JSON.stringify({ t, items }) + '\n');
    }
    const { data } = await send('Page.captureScreenshot', {
      format: 'png', clip: { x: 0, y: 0, width: W, height: H, scale: 1 }, captureBeyondViewport: false, fromSurface: true,
    }, s);
    const name = only ? `t${t}.png` : String(i).padStart(4, '0') + '.png';
    writeFileSync(join(outDir, name), Buffer.from(data, 'base64'));
    if ((i + 1) % 60 === 0 || i + 1 === times.length) {
      const ms = (Date.now() - start) / (i + 1);
      console.log(`[${lang}] ${i + 1}/${times.length} frames, ${ms.toFixed(0)} ms/frame`);
    }
  }
  const total = Date.now() - start;
  console.log(`[${lang}] done: ${times.length} frames in ${(total / 1000).toFixed(1)} s (${(total / times.length).toFixed(0)} ms/frame) -> ${outDir}`);
  await send('Browser.close').catch(() => {});
}

try { await main(); }
catch (e) { console.error(`render.mjs: ${e.message}`); process.exitCode = 1; }
finally { cleanup(); }
