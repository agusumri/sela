<#
.SYNOPSIS
  Records short MP4 clips of Sela (always with --mock) on a neutral backdrop for Instagram Reels,
  once per language, into %LOCALAPPDATA%\Temp\sela-reels\clips\<lang>\, then validates them.

.NOTES
  Same privacy approach as tools/screenshots.ps1: a topmost dark backdrop covers the primary
  screen before Sela starts, so the real desktop never ends up in a clip.
  Usage:  powershell -ExecutionPolicy Bypass -File tools/record-reels-clips.ps1 [-Languages en,id]
#>
[CmdletBinding()]
param([ValidateSet('en', 'id')][string[]]$Languages = @('en', 'id'))

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$exe     = Join-Path $root 'src\Sela\bin\Debug\net10.0-windows\Sela.exe'
$outRoot = Join-Path $env:LOCALAPPDATA 'Temp\sela-reels\clips'   # long path (the 8.3 short path has failed before)
$logDir  = Join-Path $env:LOCALAPPDATA 'Temp\sela-reels\logs'
if (-not (Test-Path $exe)) { throw "Sela.exe not found: $exe" }
$ffmpeg  = (Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source
New-Item -ItemType Directory -Force -Path $outRoot, $logDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace SelaReels -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr v);
[DllImport("user32.dll")] public static extern uint GetDpiForSystem();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
'@

# Physical pixel coordinates everywhere (per-monitor v2).
[void][SelaReels.Native]::SetProcessDpiAwarenessContext([IntPtr](-4))
$scale  = [SelaReels.Native]::GetDpiForSystem() / 96.0
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bgTop  = [System.Drawing.ColorTranslator]::FromHtml('#0E1116')
$bgBot  = [System.Drawing.ColorTranslator]::FromHtml('#1B2230')
Write-Host ("Screen {0}x{1} physical, scale {2}" -f $screen.Width, $screen.Height, $scale)

function Stop-Sela { Get-Process Sela -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 600 }

# Pump the backdrop's message loop while waiting.
function Wait-Pump([double]$seconds) {
    $end = [DateTime]::Now.AddSeconds($seconds)
    while ([DateTime]::Now -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
}

function Fill-Gradient($g, [System.Drawing.Rectangle]$r) {
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($r, $bgTop, $bgBot, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($brush, $r); $brush.Dispose()
}

# Top-center region for a notch of the given DIP size, with padding (no top padding: it hugs the edge).
# Width/height rounded up to even numbers for H.264 / yuv420p.
function Notch-Rect([double]$wDip, [double]$hDip, [double]$padDip = 28) {
    $w = [int][Math]::Round(($wDip + 2 * $padDip) * $scale); if ($w % 2) { $w++ }
    $h = [int][Math]::Round(($hDip + $padDip) * $scale);     if ($h % 2) { $h++ }
    New-Object System.Drawing.Rectangle(($screen.X + [int](($screen.Width - $w) / 2)), $screen.Y, $w, $h)
}

$cursorCenter = { [void][SelaReels.Native]::SetCursorPos($screen.X + [int]($screen.Width / 2), $screen.Bottom - 40) }
$cursorCorner = { [void][SelaReels.Native]::SetCursorPos($screen.Right - 2, $screen.Bottom - 2) }

# 1 px short of full height so Sela's full-screen detector doesn't hide the notch.
$backdrop = New-Object System.Windows.Forms.Form
$backdrop.FormBorderStyle = 'None'
$backdrop.StartPosition   = 'Manual'
$backdrop.ShowInTaskbar   = $false
$backdrop.TopMost         = $true
$backdrop.Bounds          = New-Object System.Drawing.Rectangle($screen.X, $screen.Y, $screen.Width, ($screen.Height - 1))
$backdrop.BackColor       = $bgTop
$backdrop.Add_Paint({ param($s, $e) Fill-Gradient $e.Graphics $s.ClientRectangle })

$langArgs = @{ en = @('--lang=en', '--mock-world'); id = @('--lang=id') }
$clips = @(
    @{ Name = 'idle-night';     Args = @('--sky=night');                   W = 280; H = 40;  Sec = 5; Cursor = $cursorCenter }
    @{ Name = 'idle-dawn';      Args = @('--sky=dawn');                    W = 280; H = 40;  Sec = 4; Cursor = $cursorCenter }
    @{ Name = 'idle-day';       Args = @('--sky=day');                     W = 280; H = 40;  Sec = 4; Cursor = $cursorCenter }
    @{ Name = 'idle-dusk';      Args = @('--sky=dusk');                    W = 280; H = 40;  Sec = 4; Cursor = $cursorCenter }
    @{ Name = 'panel-prayer';   Args = @('--sky=night', '--panel');        W = 344; H = 412; Sec = 5; Cursor = $cursorCorner }
    @{ Name = 'panel-agenda';   Args = @('--sky=night', '--panel=agenda'); W = 344; H = 412; Sec = 5; Cursor = $cursorCorner }
    @{ Name = 'panel-hadith';   Args = @('--sky=night', '--panel=hadits'); W = 344; H = 412; Sec = 5; Cursor = $cursorCorner }
    @{ Name = 'reminder-adhan'; Args = @('--sky=night', '--test-adhan');   W = 380; H = 92;  Sec = 5; Cursor = $cursorCorner }
    @{ Name = 'reminder-habit'; Args = @('--sky=night', '--test-habit');   W = 380; H = 92;  Sec = 5; Cursor = $cursorCorner }
)

$failed = New-Object System.Collections.Generic.List[string]

function Record([string]$lang, $clip) {
    $dir  = Join-Path $outRoot $lang
    $out  = Join-Path $dir "$($clip.Name).mp4"
    $log  = Join-Path $logDir "$lang-$($clip.Name).log"
    $r    = Notch-Rect $clip.W $clip.H
    $appArgs = @('--mock') + $langArgs[$lang] + $clip.Args
    Write-Host ("Recording {0}/{1}.mp4  region x={2} y={3} {4}x{5}, {6}s  (Sela {7})" -f $lang, $clip.Name, $r.X, $r.Y, $r.Width, $r.Height, $clip.Sec, ($appArgs -join ' '))
    Stop-Sela
    & $clip.Cursor
    $ffArgs = @('-hide_banner', '-loglevel', 'warning', '-y', '-f', 'gdigrab', '-framerate', '30',
        '-offset_x', $r.X, '-offset_y', $r.Y, '-video_size', "$($r.Width)x$($r.Height)", '-draw_mouse', '0',
        '-i', 'desktop', '-t', $clip.Sec, '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '14', '-pix_fmt', 'yuv420p',
        "`"$out`"")
    $ff = Start-Process -FilePath $ffmpeg -ArgumentList $ffArgs -PassThru -WindowStyle Hidden -RedirectStandardError $log
    [void]$ff.Handle   # cache the handle so ExitCode is available later
    Wait-Pump 0.3
    $sela = Start-Process -FilePath $exe -ArgumentList $appArgs -PassThru
    $deadline = [DateTime]::Now.AddSeconds($clip.Sec + 15)
    while (-not $ff.HasExited -and [DateTime]::Now -lt $deadline) { Wait-Pump 0.1 }
    if (-not $ff.HasExited) { $ff.Kill(); [void]$ff.WaitForExit(3000); Write-Warning "  ffmpeg timed out" }
    $ff.WaitForExit()
    $code = $ff.ExitCode
    if ($sela.HasExited) { Write-Warning "  Sela exited early (code $($sela.ExitCode))" }
    Stop-Sela
    Write-Host "  ffmpeg exit code $code"
    if ($code -ne 0 -or -not (Test-Path $out)) { $failed.Add("$lang/$($clip.Name)"); Get-Content $log -ErrorAction SilentlyContinue | Write-Host }
}

try {
    $backdrop.Show()
    Wait-Pump 1.0
    foreach ($lang in $Languages) {
        New-Item -ItemType Directory -Force -Path (Join-Path $outRoot $lang) | Out-Null
        foreach ($clip in $clips) { Record $lang $clip }
    }
}
finally {
    Stop-Sela
    Get-Process ffmpeg -ErrorAction SilentlyContinue | Stop-Process -Force
    $backdrop.Close(); $backdrop.Dispose()
}

# ---- validation ----
function Test-Clip([string]$path, [double]$expectSec) {
    $info = & $ffprobe -v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate:format=duration -of json $path | Out-String | ConvertFrom-Json
    $w = [int]$info.streams[0].width; $h = [int]$info.streams[0].height
    $fr = $info.streams[0].avg_frame_rate -split '/'; $fps = [Math]::Round([double]$fr[0] / [double]$fr[1], 2)
    $dur = [Math]::Round([double]$info.format.duration, 2)

    $png = [IO.Path]::ChangeExtension($path, '.t3.png')
    & $ffmpeg -hide_banner -loglevel error -y -ss 3 -i $path -frames:v 1 $png
    $bmp = New-Object System.Drawing.Bitmap($png)
    $min = 765; $max = 0; $darkMid = 0; $foreign = 0
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $c = $bmp.GetPixel($x, $y); $sum = $c.R + $c.G + $c.B
            if ($sum -lt $min) { $min = $sum }; if ($sum -gt $max) { $max = $sum }
            if ($x -ge $bmp.Width / 3 -and $x -lt 2 * $bmp.Width / 3 -and $c.R -lt 12 -and $c.G -lt 12 -and $c.B -lt 14) { $darkMid++ }
            # outer 6 px ring (left, right, bottom; the notch hugs the top edge) must be the blue-tinted backdrop
            $edge = ($x -lt 6 -or $x -ge $bmp.Width - 6 -or $y -ge $bmp.Height - 6)
            if ($edge -and -not ($c.B -ge $c.R + 4 -and $c.B -ge $c.G)) { $foreign++ }
        }
    }
    $bmp.Dispose(); Remove-Item $png
    $even = ($w % 2 -eq 0) -and ($h % 2 -eq 0)
    $durOk = [Math]::Abs($dur - $expectSec) -le 0.5
    $fpsOk = [Math]::Abs($fps - 30) -le 1
    $notUniform = ($max - $min) -gt 60
    [pscustomobject]@{
        Clip = Split-Path -Leaf $path; Size = "${w}x$h"; Even = $even; Fps = $fps; Duration = $dur; DurOk = $durOk
        NotUniform = $notUniform; DarkMidPx = $darkMid; ForeignEdgePx = $foreign
        Edge = $(if ($foreign -eq 0) { 'pass' } else { 'FAIL' })
        Pass = ($even -and $fpsOk -and $durOk -and $notUniform -and $darkMid -gt 0 -and $foreign -eq 0)
    }
}

foreach ($lang in $Languages) {
    Write-Host ''; Write-Host "== $lang =="
    $rows = foreach ($clip in $clips) {
        $p = Join-Path (Join-Path $outRoot $lang) "$($clip.Name).mp4"
        if (Test-Path $p) { Test-Clip $p $clip.Sec } else { [pscustomobject]@{ Clip = "$($clip.Name).mp4"; Pass = $false; Edge = 'missing' } }
    }
    $rows | Format-Table -AutoSize | Out-String | Write-Host
}
$left = @(Get-Process Sela, ffmpeg -ErrorAction SilentlyContinue)
Write-Host ("Leftover Sela/ffmpeg processes: {0}" -f $left.Count)
if ($failed.Count) { Write-Warning ('Failed: ' + ($failed -join ', ')) }
