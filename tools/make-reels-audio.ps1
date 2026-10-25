<#
.SYNOPSIS
  Synthesizes Sela's original Reels soundtrack (CC0) and muxes it into the Reels videos.

.DESCRIPTION
  Everything is generated here: frame drums (dum/tak), riq jingles, claps, a Karplus-Strong
  oud, a breathy ney and a D/A drone, in maqam Hijaz on D, with accents on each scene cut of
  tools/make-reels.ps1. Rendering is deterministic (fixed seed).

  Output: dist/reels/sela-reels-soundtrack.wav (48 kHz / 16-bit, loudnorm -14 LUFS, TP <= -1 dBTP)
          dist/reels/sela-reels-<lang>.mp4 (video copied from sela-reels-<lang>-silent.mp4 + AAC)
          dist/reels/SOUNDTRACK-LICENSE.txt

.EXAMPLE
  ./tools/make-reels-audio.ps1
  ./tools/make-reels-audio.ps1 -Bpm 116 -SkipMux
#>
[CmdletBinding()]
param(
    [double]$Bpm = 112,
    [switch]$SkipMux,
    [string[]]$Languages = @('en', 'id'),
    [string]$WorkRoot = (Join-Path $env:LOCALAPPDATA 'Temp\sela-reels\audio')
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $RepoRoot 'dist\reels'
$Inv = [Globalization.CultureInfo]::InvariantCulture

# Scene timing mirrors Get-Storyboard in tools/make-reels.ps1: scene k starts at the sum of
# the previous scenes' Dur (each scene carries +XFade that the next xfade eats).
$SceneDurs = @(2.5, 3.5, 3.5, 3.0, 3.0, 3.5, 2.5, 3.0)
$Cuts = @(); $acc = 0.0
for ($k = 1; $k -lt $SceneDurs.Count; $k++) { $acc += $SceneDurs[$k - 1]; $Cuts += $acc }
$Total = ($SceneDurs | Measure-Object -Sum).Sum   # 24.5 s

$Source = @'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace SelaAudio {

public class Rng {
    uint s;
    public Rng(uint seed) { s = seed == 0 ? 1u : seed; }
    public double Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s / 4294967296.0; }
    public double Bi() { return Next() * 2.0 - 1.0; }
}

public class Biquad {
    double b0, b1, b2, a1, a2, z1, z2;
    static Biquad Make(double b0, double b1, double b2, double a0, double a1, double a2) {
        Biquad q = new Biquad();
        q.b0 = b0 / a0; q.b1 = b1 / a0; q.b2 = b2 / a0; q.a1 = a1 / a0; q.a2 = a2 / a0;
        return q;
    }
    public static Biquad BandPass(double sr, double f, double Q) {
        double w = 2 * Math.PI * f / sr, al = Math.Sin(w) / (2 * Q), c = Math.Cos(w);
        return Make(al, 0, -al, 1 + al, -2 * c, 1 - al);
    }
    public static Biquad LowPass(double sr, double f, double Q) {
        double w = 2 * Math.PI * f / sr, al = Math.Sin(w) / (2 * Q), c = Math.Cos(w);
        return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
    }
    public static Biquad HighPass(double sr, double f, double Q) {
        double w = 2 * Math.PI * f / sr, al = Math.Sin(w) / (2 * Q), c = Math.Cos(w);
        return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
    }
    public double Process(double x) {
        double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
}

class Comb {
    double[] buf; int idx; double store, fb, damp;
    public Comb(int size, double fb, double damp) { buf = new double[size]; this.fb = fb; this.damp = damp; }
    public double Process(double x) {
        double y = buf[idx];
        store = y * (1 - damp) + store * damp;
        buf[idx] = x + store * fb;
        if (++idx >= buf.Length) idx = 0;
        return y;
    }
}

class Allpass {
    double[] buf; int idx;
    public Allpass(int size) { buf = new double[size]; }
    public double Process(double x) {
        double b = buf[idx];
        buf[idx] = x + b * 0.5;
        if (++idx >= buf.Length) idx = 0;
        return b - x;
    }
}

public class Synth {
    public const int SR = 48000;
    int N;
    double[] L, R, Send;
    Rng rng = new Rng(20261025);
    double total;

    public Synth(double seconds) {
        total = seconds;
        N = (int)Math.Round(seconds * SR);
        L = new double[N]; R = new double[N]; Send = new double[N];
    }

    void Add(int i, double v, double pan, double send) {
        if (i < 0 || i >= N) return;
        double a = (pan + 1) * Math.PI / 4;
        L[i] += v * Math.Cos(a); R[i] += v * Math.Sin(a); Send[i] += v * send;
    }

    static int Idx(double t) { return (int)Math.Round(t * SR); }

    // Frame-drum low stroke: pitched membrane with fast drop + noise slap.
    public void Dum(double t, double amp, double pan) {
        int i0 = Idx(t), n = (int)(0.5 * SR);
        Biquad lp = Biquad.LowPass(SR, 1200, 0.7);
        double ph = 0;
        for (int k = 0; k < n; k++) {
            double tt = (double)k / SR;
            double f = 58 + 52 * Math.Exp(-tt / 0.035);
            ph += 2 * Math.PI * f / SR;
            double env = Math.Exp(-tt / 0.17) * (1 - Math.Exp(-tt / 0.0015));
            double v = Math.Sin(ph) * env + 0.2 * Math.Sin(1.59 * ph) * Math.Exp(-tt / 0.06);
            v += 0.45 * lp.Process(rng.Bi()) * Math.Exp(-tt / 0.012);
            Add(i0 + k, amp * v, pan, 0.12);
        }
    }

    // Frame-drum rim stroke: filtered noise + short high tone.
    public void Tak(double t, double amp, double pan) {
        int i0 = Idx(t), n = (int)(0.16 * SR);
        Biquad bp = Biquad.BandPass(SR, 2600, 0.9);
        for (int k = 0; k < n; k++) {
            double tt = (double)k / SR;
            double v = 1.3 * bp.Process(rng.Bi()) * Math.Exp(-tt / 0.022);
            v += 0.45 * Math.Sin(2 * Math.PI * 620 * tt) * Math.Exp(-tt / 0.035);
            v += 0.15 * Math.Sin(2 * Math.PI * 1730 * tt) * Math.Exp(-tt / 0.012);
            Add(i0 + k, amp * v, pan, 0.18);
        }
    }

    // Riq jingles: band-passed noise micro-bursts + faint metallic ring.
    public void Riq(double t, double amp, double pan, double decay) {
        int i0 = Idx(t), n = (int)((decay * 6 + 0.02) * SR);
        Biquad hp = Biquad.HighPass(SR, 5000, 0.7), bp = Biquad.BandPass(SR, 8500, 1.6);
        double[] offs = { 0, 0.003, 0.0072 };
        for (int k = 0; k < n; k++) {
            double tt = (double)k / SR, env = 0;
            for (int j = 0; j < offs.Length; j++) {
                double d = tt - offs[j];
                if (d >= 0) env += Math.Exp(-d / decay) * (j == 0 ? 1.0 : 0.7);
            }
            double v = 1.6 * bp.Process(hp.Process(rng.Bi())) * env;
            v += 0.08 * (Math.Sin(2 * Math.PI * 6230 * tt) + Math.Sin(2 * Math.PI * 7410 * tt) + Math.Sin(2 * Math.PI * 9120 * tt)) * Math.Exp(-tt / (decay * 1.5));
            Add(i0 + k, amp * v, pan, 0.2);
        }
    }

    // Hand clap: layered short noise bursts with a tail.
    public void Clap(double t, double amp, double pan) {
        int i0 = Idx(t), n = (int)(0.25 * SR);
        Biquad bp = Biquad.BandPass(SR, 1400, 1.1);
        for (int k = 0; k < n; k++) {
            double tt = (double)k / SR, env = 0;
            for (int j = 0; j < 3; j++) { double d = tt - j * 0.009; if (d >= 0 && d < 0.009) env += Math.Exp(-d / 0.004); }
            double d4 = tt - 0.027;
            if (d4 >= 0) env += Math.Exp(-d4 / 0.07);
            Add(i0 + k, amp * 1.5 * bp.Process(rng.Bi()) * env, pan, 0.3);
        }
    }

    // Oud-like pluck: Karplus-Strong with fractional (vibrato-modulated) delay + body resonances.
    public void Oud(double t, double f, double dur, double amp, double pan, double t60) {
        int i0 = Idx(t), n = (int)((dur + 0.15 + (t60 > 2 ? 2.0 : 0.3)) * SR);
        int size = 4096, mask = size - 1;
        double[] buf = new double[size];
        int lexc = (int)(SR / f);
        double[] exc = new double[lexc];
        Biquad elp = Biquad.LowPass(SR, 3200, 0.7);
        for (int k = 0; k < lexc; k++) exc[k] = elp.Process(rng.Bi());
        int pp = Math.Max(1, lexc / 5);
        double g = Math.Exp(Math.Log(0.001) / (f * t60));
        Biquad b1 = Biquad.BandPass(SR, 210, 1.5), b2 = Biquad.BandPass(SR, 470, 2.5), hp = Biquad.HighPass(SR, 80, 0.7);
        for (int k = 0; k < n; k++) {
            double tt = (double)k / SR;
            double vib = 0.0025 * Math.Sin(2 * Math.PI * 5.5 * tt) * Math.Min(1.0, tt / 0.35);
            double D = SR / (f * (1 + vib)) - 0.5;
            double y = g * 0.5 * (Read(buf, mask, k - D) + Read(buf, mask, k - D - 1));
            if (k < lexc) y += exc[k] - (k >= pp ? exc[k - pp] : 0);
            buf[k & mask] = y;
            double env = tt > dur + 0.05 ? Math.Exp(-(tt - dur - 0.05) / 0.09) : 1.0;
            if (t60 > 2) env = tt > dur ? Math.Exp(-(tt - dur) / 0.5) : 1.0;
            double o = hp.Process(0.6 * y + 1.4 * b1.Process(y) + 0.9 * b2.Process(y));
            Add(i0 + k, amp * 0.6 * o * env, pan, 0.25);
        }
    }

    static double Read(double[] buf, int mask, double pos) {
        if (pos < 0) return 0;
        int i = (int)pos; double fr = pos - i;
        return buf[i & mask] * (1 - fr) + buf[(i + 1) & mask] * fr;
    }

    // Ney-like lead: sine stack + band-passed breath, slow attack, scoop and vibrato.
    public void Ney(double t, double f, double dur, double amp, double pan) {
        int i0 = Idx(t), n = (int)((dur + 0.4) * SR);
        Biquad bp = Biquad.BandPass(SR, f * 2, 2.0), hp = Biquad.HighPass(SR, 3000, 0.7);
        double ph = 0;
        for (int k = 0; k < n; k++) {
            double tt = (double)k / SR;
            double depth = 0.006 * Math.Max(0, Math.Min(1, (tt - 0.15) / 0.3));
            double fr = f * (1 - 0.017 * Math.Exp(-tt / 0.04)) * (1 + depth * Math.Sin(2 * Math.PI * 5.2 * tt));
            ph += 2 * Math.PI * fr / SR;
            double env = (1 - Math.Exp(-tt / 0.06)) * (tt > dur ? Math.Exp(-(tt - dur) / 0.08) : 1.0);
            double tone = Math.Sin(ph) + 0.3 * Math.Sin(2 * ph) + 0.1 * Math.Sin(3 * ph);
            double nz = rng.Bi();
            double breath = 1.4 * bp.Process(nz) + 0.06 * hp.Process(nz);
            Add(i0 + k, amp * env * (tone + breath), pan, 0.4);
        }
    }

    // D2 + A2 drone, slightly detuned between channels for width.
    public void Drone(double t0, double t1, double fadeIn, double fadeOut, double amp) {
        double[] roots = { 73.42, 110.0 };
        int a = Idx(t0), b = Math.Min(N, Idx(t1));
        for (int k = a; k < b; k++) {
            double tt = (double)k / SR - t0, rem = t1 - (double)k / SR;
            double env = Math.Min(1, tt / fadeIn) * Math.Min(1, rem / fadeOut);
            env = env * env * (0.85 + 0.15 * Math.Sin(2 * Math.PI * 0.25 * tt));
            double l = 0, r = 0;
            for (int j = 0; j < roots.Length; j++)
                for (int h = 1; h <= 6; h++) {
                    double w = (j == 0 ? 1.0 : 0.7) / Math.Pow(h, 1.4);
                    l += w * Math.Sin(2 * Math.PI * roots[j] * h * tt);
                    r += w * Math.Sin(2 * Math.PI * (roots[j] + 0.25) * h * tt);
                }
            L[k] += amp * env * l; R[k] += amp * env * r;
            Send[k] += amp * env * 0.15 * (l + r);
        }
    }

    // ---- Arrangement -------------------------------------------------------------
    static readonly double[] Hijaz = { 293.66, 311.13, 369.99, 392.00, 440.00, 466.16, 523.25, 587.33 };

    static double Freq(int deg) { return Hijaz[deg % 8] * (deg >= 8 ? 2 : 1); }

    public void Compose(double bpm, double[] cuts) {
        double beat = 60.0 / bpm, e = beat / 2;
        double start = cuts[0], end = cuts[cuts.Length - 1];

        // Intro: drone fade-in, riq shimmer roll, soft ney call.
        Drone(0, total - 0.1, 2.3, 2.5, 0.035);
        double rt = 0.35;
        while (rt < start - 0.04) {
            double p = rt / start;
            Riq(rt, 0.03 + 0.4 * p * p * p, (Math.Round(rt * 100) % 2 == 0) ? -0.3 : 0.3, 0.02 + 0.02 * p);
            rt += 0.13 - 0.095 * p;
        }
        Ney(0.55, 440.0, 0.9, 0.06, 0);
        Ney(1.5, 466.16, 0.35, 0.06, 0);
        Ney(1.85, 440.0, 0.55, 0.07, 0);

        // Groove: maqsum (D T . T D . T .) in eighths.
        for (int k = 0; ; k++) {
            double t = start + k * e;
            if (t >= end - 0.02) break;
            int pos = k % 8;
            bool lift = t >= cuts[2] - 0.01;   // claps / ghost notes from the 9.5 s cut
            bool fill = false;
            for (int c = 1; c < cuts.Length; c++) if (t >= cuts[c] - 0.5 && t < cuts[c] - 0.02) fill = true;

            if (pos == 0) Dum(t, 0.9, -0.05);
            if (pos == 4) Dum(t, 0.75, 0.05);
            if (fill) continue;
            if (pos == 1 || pos == 6) Tak(t, 0.55, 0.25);
            if (pos == 3) Tak(t, 0.5, 0.2);
            if (pos == 7 && lift) Tak(t, 0.22, 0.3);
            if (pos % 2 == 1) Riq(t, 0.33, -0.28, 0.03);
            else if (lift) Riq(t, 0.13, -0.22, 0.02);
            if (t >= cuts[3] - 0.01) Riq(t + e / 2, 0.1, -0.3, 0.018);
            if (lift && (pos == 2 || pos == 6)) { Clap(t, 0.32, -0.2); Clap(t + 0.004, 0.24, 0.22); }
        }

        // Fills before each cut and an accent on each cut.
        for (int c = 0; c < cuts.Length; c++) {
            double ct = cuts[c];
            if (c > 0) {
                for (int j = 0; j < 5; j++) Tak(ct - 0.5 + j * 0.1, 0.25 + 0.08 * j, (j % 2 == 0) ? 0.25 : -0.15);
                for (int j = 0; j < 9; j++) Riq(ct - 0.5 + j * 0.05, 0.08 + 0.035 * j, -0.28, 0.02);
            }
            Riq(ct, 0.45, -0.25, 0.08);
            Riq(ct + 0.006, 0.3, 0.25, 0.07);
            if (c > 0 && c < cuts.Length - 1) Dum(ct, 0.55, 0);
        }

        // Melody phrases (Hijaz degrees: 0=D 1=Eb 2=F# 3=G 4=A 5=Bb 6=C 7=D').
        int[][] degs = {
            new int[] { 4, 4, 5, 4, 3, 2, 3,   2, 1, 0, 2, 3, 4 },          // motif
            new int[] { 6, 5, 4, 3, 4, 5, 4,   3, 2, 3, 2, 1, 1, 0 },       // answer
            new int[] { 0, 2, 4, 7, 6, 5, 4,   5, 4, 3, 2, 3, 4 },          // climb
            new int[] { 3, 2, 1, 2, 1, 0 }                                  // cadence
        };
        double[][] lens = {
            new double[] { .5, .5, .5, .5, .5, .5, 1,   .5, .5, 1, .5, .5, 1 },
            new double[] { .5, .5, .5, .5, 1, .5, .5,   .5, .5, .5, .5, .5, .5, 1 },
            new double[] { .5, .5, .5, .5, 1, .5, .5,   .5, .5, .5, .5, 1, 1 },
            new double[] { .5, .5, .5, .5, .5, 1 }
        };
        int[] order = { 0, 1, 0, 2, 3 };
        double pt = start;
        for (int p = 0; p < order.Length; p++) {
            int[] d = degs[order[p]]; double[] ln = lens[order[p]];
            bool ney = pt >= 11.0;
            double nt = pt, beatsIn = 0;
            for (int i = 0; i < d.Length; i++) {
                double dur = ln[i] * beat, f = Freq(d[i]);
                if (nt >= end - 0.05) break;
                Oud(nt, f, dur, 0.5, 0, 1.4);
                if (ln[i] >= 1) Oud(nt + e, f, e, 0.3, 0, 1.4);       // tremolo re-pick on long notes
                if (ney) Ney(nt, f * 2, Math.Min(dur, end - nt) - 0.03, 0.11, 0);
                if (beatsIn % 4 == 0) Oud(nt, 146.83, beat * 2, 0.28, 0.1, 1.6);   // low D pedal per bar
                nt += dur; beatsIn += ln[i];
            }
            pt += 8 * beat;
        }

        // End card: final dum + Hijaz cadence strum (D F# A D), held ney D.
        Dum(end, 1.1, 0);
        double[] chord = { 73.42, 146.83, 185.00, 220.00, 293.66 };
        for (int i = 0; i < chord.Length; i++) Oud(end + 0.028 * i, chord[i], 1.9, i == 0 ? 0.35 : 0.42, (i - 2) * 0.08, 3.0);
        Ney(end + 0.05, 587.33, 1.6, 0.08, 0);
    }

    // ---- Mix ---------------------------------------------------------------------
    public void Mix(string path) {
        double sc = SR / 44100.0;
        int[] cs = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        int[] aps = { 556, 441, 341, 225 };
        Comb[] cl = new Comb[cs.Length], cr = new Comb[cs.Length];
        for (int i = 0; i < cs.Length; i++) {
            cl[i] = new Comb((int)(cs[i] * sc), 0.72, 0.35);
            cr[i] = new Comb((int)((cs[i] + 23) * sc), 0.72, 0.35);
        }
        Allpass[] al = new Allpass[aps.Length], ar = new Allpass[aps.Length];
        for (int i = 0; i < aps.Length; i++) { al[i] = new Allpass((int)(aps[i] * sc)); ar[i] = new Allpass((int)((aps[i] + 23) * sc)); }

        double[] wl = new double[N], wr = new double[N];
        double ew = 0, ed = 0;
        for (int k = 0; k < N; k++) {
            double x = Send[k] * 0.015, l = 0, r = 0;
            for (int i = 0; i < cs.Length; i++) { l += cl[i].Process(x); r += cr[i].Process(x); }
            for (int i = 0; i < aps.Length; i++) { l = al[i].Process(l); r = ar[i].Process(r); }
            wl[k] = l; wr[k] = r;
            ew += l * l + r * r; ed += L[k] * L[k] + R[k] * R[k];
        }
        double wg = ew > 0 ? 0.3 * Math.Sqrt(ed / ew) : 0;   // reverb ~ -10.5 dB below dry

        Biquad hl = Biquad.HighPass(SR, 32, 0.7), hr = Biquad.HighPass(SR, 32, 0.7);
        double peak = 0;
        for (int k = 0; k < N; k++) {
            double t = (double)k / SR;
            double fade = t < total - 1.3 ? 1.0 : (t >= total - 0.1 ? 0.0 : 0.5 + 0.5 * Math.Cos(Math.PI * (t - (total - 1.3)) / 1.2));
            L[k] = hl.Process(L[k] + wg * wl[k]) * fade;
            R[k] = hr.Process(R[k] + wg * wr[k]) * fade;
            peak = Math.Max(peak, Math.Max(Math.Abs(L[k]), Math.Abs(R[k])));
        }
        // Normalize, then soft-clip transients (drive) to tame crest factor for loudnorm.
        double drive = 3.2, norm = drive / peak, tn = Math.Tanh(drive);
        using (BinaryWriter w = new BinaryWriter(File.Create(path))) {
            int bytes = N * 2 * 4;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)3); w.Write((short)2);
            w.Write(SR); w.Write(SR * 8); w.Write((short)8); w.Write((short)32);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            for (int k = 0; k < N; k++) {
                w.Write((float)(0.5 * Math.Tanh(L[k] * norm) / tn));
                w.Write((float)(0.5 * Math.Tanh(R[k] * norm) / tn));
            }
        }
    }

    // ---- Verification: 10 ms RMS envelope around each cut, from a 16-bit stereo WAV ---
    public static string Onsets(string path, double[] cuts) {
        byte[] b = File.ReadAllBytes(path);
        int p = 12, data = -1, len = 0;
        while (p + 8 <= b.Length) {
            string id = Encoding.ASCII.GetString(b, p, 4);
            int sz = BitConverter.ToInt32(b, p + 4);
            if (id == "data") { data = p + 8; len = Math.Min(sz, b.Length - data); break; }
            p += 8 + sz + (sz & 1);
        }
        if (data < 0) throw new Exception("no data chunk");
        int frames = len / 4, hop = SR / 100;
        int nf = frames / hop;
        double[] env = new double[nf];
        for (int f = 0; f < nf; f++) {
            double s = 0;
            for (int k = f * hop; k < (f + 1) * hop; k++) {
                double m = (BitConverter.ToInt16(b, data + k * 4) + BitConverter.ToInt16(b, data + k * 4 + 2)) / 65536.0;
                s += m * m;
            }
            env[f] = 20 * Math.Log10(Math.Sqrt(s / hop) + 1e-9);
        }
        StringBuilder sb = new StringBuilder();
        foreach (double c in cuts) {
            int cf = (int)Math.Round(c * 100);
            double pre = -200, post = -200;
            for (int f = cf - 8; f < cf - 2; f++) if (f >= 0 && f < nf) pre = Math.Max(pre, env[f]);
            for (int f = cf; f < cf + 6; f++) if (f >= 0 && f < nf) post = Math.Max(post, env[f]);
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "cut {0,5:0.00}s  pre(-80..-20ms) {1,6:0.0} dB  post(0..60ms) {2,6:0.0} dB  jump {3,5:+0.0;-0.0} dB\n", c, pre, post, post - pre);
        }
        return sb.ToString();
    }
}
}
'@

function Invoke-FFmpeg([string[]]$FfArgs) {
    & ffmpeg -hide_banner -v error -nostats -y @FfArgs
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed ($LASTEXITCODE): $($FfArgs -join ' ')" }
}

# ffmpeg writes filter reports to stderr; collect them as text without tripping 'Stop'.
function Get-FFmpegLog([string[]]$FfArgs) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & ffmpeg -hide_banner -nostats -y @FfArgs 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $old }
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed ($LASTEXITCODE): $($FfArgs -join ' ')" }
    $out -join "`n"
}

if (-not ('SelaAudio.Synth' -as [type])) { Add-Type -TypeDefinition $Source -Language CSharp }

New-Item -ItemType Directory -Force -Path $OutDir, $WorkRoot | Out-Null
$raw = Join-Path $WorkRoot 'soundtrack-raw.wav'
$wav = Join-Path $OutDir 'sela-reels-soundtrack.wav'

# Groove starts at the first cut; the end card starts at the last.
Write-Host ("Rendering {0}s at {1} BPM, cuts: {2}" -f $Total, $Bpm, (($Cuts | ForEach-Object { $_.ToString('0.##', $Inv) }) -join ', '))
$synth = New-Object SelaAudio.Synth ([double]$Total)
$synth.Compose($Bpm, [double[]]$Cuts)
$synth.Mix($raw)

# Two-pass loudnorm: -14 LUFS integrated, true peak <= -1 dBTP (target -1.5 for AAC headroom).
$log = Get-FFmpegLog @('-i', $raw, '-af', 'loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json', '-f', 'null', '-')
$json = $log.Substring($log.LastIndexOf('{')) ; $json = $json.Substring(0, $json.IndexOf('}') + 1)
$m = $json | ConvertFrom-Json
$ln = "loudnorm=I=-14:TP=-1.5:LRA=11:measured_I=$($m.input_i):measured_TP=$($m.input_tp):measured_LRA=$($m.input_lra)" +
      ":measured_thresh=$($m.input_thresh):offset=$($m.target_offset):linear=true:print_format=json"
$log2 = Get-FFmpegLog @('-i', $raw, '-af', $ln, '-ar', '48000', '-ac', '2', '-c:a', 'pcm_s16le', $wav)
$j2 = $log2.Substring($log2.LastIndexOf('{')); $j2 = ($j2.Substring(0, $j2.IndexOf('}') + 1)) | ConvertFrom-Json
Write-Host ("loudnorm: input {0} LUFS / {1} dBTP -> {2} ({3})" -f $m.input_i, $m.input_tp, $j2.output_i, $j2.normalization_type)

[IO.File]::WriteAllText((Join-Path $OutDir 'SOUNDTRACK-LICENSE.txt'), @"
Original composition generated by tools/make-reels-audio.ps1 for Sela. Released under CC0 1.0 (public domain).
https://creativecommons.org/publicdomain/zero/1.0/

An upbeat, nasheed-style instrumental in maqam Hijaz on D (about $Bpm BPM, 24.5 s) synthesized
entirely in code with no samples or third-party recordings: frame drums (daf/rebana "dum" and
"tak") playing a maqsum rhythm, riq jingles and claps, a Karplus-Strong oud carrying a two-bar
Hijaz motif with answers, a breathy ney doubling it an octave up in the second half, and a soft
D/A drone, with fills and accents on each scene cut and a D-F#-A cadence on the end card.
"@, (New-Object System.Text.UTF8Encoding($false)))

if (-not $SkipMux) {
    foreach ($lang in $Languages) {
        $final = Join-Path $OutDir "sela-reels-$lang.mp4"
        $silent = Join-Path $OutDir "sela-reels-$lang-silent.mp4"
        if (-not (Test-Path -LiteralPath $silent)) {
            if (-not (Test-Path -LiteralPath $final)) { throw "Missing $final (run tools/make-reels.ps1 first)" }
            Move-Item -LiteralPath $final -Destination $silent
        }
        Invoke-FFmpeg @('-i', $silent, '-i', $wav, '-map', '0:v:0', '-map', '1:a:0', '-c:v', 'copy',
            '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-ac', '2', '-shortest', '-movflags', '+faststart', $final)
        Write-Host "[$lang] wrote $final"
    }
}

Write-Host "`nOnsets at scene cuts:"
Write-Host ([SelaAudio.Synth]::Onsets($wav, [double[]]$Cuts))
