<#
.SYNOPSIS
  Render the /brag launch video (see tools/brag/CONTRACT.md).
.DESCRIPTION
  1. Extracts onset-trimmed 30 fps PNG frames from the recorded UI clips
     ($env:LOCALAPPDATA\Temp\sela-reels\clips\<lang>\<name>.mp4) into brag-output/work/clips/<lang>/<name>/ + manifest.json.
  2. Renders tools/brag/scene.html frame by frame in headless Chrome/Edge via tools/brag/render.mjs.
  3. Encodes brag-output/work/brag-<lang>-video.mp4 (skipped with -Only).
.EXAMPLE
  ./tools/make-brag.ps1
  ./tools/make-brag.ps1 -Languages en -SkipExtract -Only 0,5.5,12 -Report
#>
[CmdletBinding()]
param(
  [ValidateSet('en', 'id')][string[]]$Languages = @('en', 'id'),
  [switch]$SkipExtract,
  [ValidateRange(1, 100000)][int]$Frames = 600,
  [double[]]$Only,
  [switch]$Report
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo 'brag-output\work'
$srcClips = Join-Path $env:LOCALAPPDATA 'Temp\sela-reels\clips'
$clipNames = 'idle-night', 'idle-dawn', 'idle-day', 'idle-dusk', 'panel-prayer', 'panel-agenda', 'panel-hadith', 'reminder-adhan', 'reminder-habit'
$minOnset = 1.2
$inv = [Globalization.CultureInfo]::InvariantCulture

foreach ($tool in 'ffmpeg', 'ffprobe', 'node') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool not found on PATH" }
}

# Windows PowerShell 5.1 turns native stderr into terminating errors under 'Stop'; rely on exit codes instead.
function Invoke-Native([string]$exe, [string[]]$argv) {
  $ErrorActionPreference = 'Continue'
  & $exe @argv 2>&1 | ForEach-Object { "$_" }
  if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

# Onset = first frame whose scene-change score vs. the previous frame exceeds 0.02
# (the static gradient backdrop scores ~0), clamped to >= $minOnset.
function Get-Onset([string]$mp4) {
  $ErrorActionPreference = 'Continue'
  $log = & ffmpeg -hide_banner -nostats -i $mp4 -an -vf "select='gt(scene,0.02)',showinfo" -frames:v 1 -f null - 2>&1 | Out-String
  if ($LASTEXITCODE -ne 0) { throw "ffmpeg onset scan failed for $mp4" }
  $m = [regex]::Match($log, 'pts_time:\s*([0-9.]+)')
  if (-not $m.Success) { Write-Warning "no onset detected in $mp4; using $minOnset s"; return $minOnset }
  return [Math]::Max($minOnset, [double]::Parse($m.Groups[1].Value, $inv))
}

if (-not $SkipExtract) {
  foreach ($lang in $Languages) {
    $outLang = Join-Path $work "clips\$lang"
    $manifest = [ordered]@{}
    foreach ($name in $clipNames) {
      $mp4 = Join-Path $srcClips "$lang\$name.mp4"
      if (-not (Test-Path -LiteralPath $mp4)) { throw "missing clip $mp4" }
      $onset = Get-Onset $mp4
      $dir = Join-Path $outLang $name
      if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
      New-Item -ItemType Directory -Path $dir -Force | Out-Null
      # -ss before -i is frame-accurate (decodes from the previous keyframe); fps=30 resamples the ~29.8 fps source.
      Invoke-Native ffmpeg @('-hide_banner', '-loglevel', 'error', '-y', '-ss', $onset.ToString($inv), '-i', $mp4,
        '-an', '-vf', 'fps=30', '-start_number', '1', (Join-Path $dir '%04d.png'))
      $pngs = @(Get-ChildItem -LiteralPath $dir -Filter '*.png')
      $probe = (& ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 $mp4) -split ','
      $manifest[$name] = [ordered]@{ frames = $pngs.Count; width = [int]$probe[0]; height = [int]$probe[1] }
      Write-Host ("[{0}] {1,-15} onset {2,5:N3} s -> {3} frames {4}x{5}" -f $lang, $name, $onset, $pngs.Count, $probe[0], $probe[1])
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outLang 'manifest.json') -Encoding ascii
  }
}

$renderer = Join-Path $PSScriptRoot 'brag\render.mjs'
foreach ($lang in $Languages) {
  if (-not (Test-Path -LiteralPath (Join-Path $work "clips\$lang\manifest.json"))) { throw "no extracted clips for $lang; run without -SkipExtract" }
  $argv = @($renderer, '--lang', $lang, '--frames', "$Frames")
  if ($Only) { $argv += @('--only', (($Only | ForEach-Object { $_.ToString($inv) }) -join ',')) }
  if ($Report) { $argv += '--report' }
  Invoke-Native node $argv

  if ($Only) { continue }
  $mp4 = Join-Path $work "brag-$lang-video.mp4"
  Invoke-Native ffmpeg @('-hide_banner', '-loglevel', 'error', '-y', '-framerate', '30', '-start_number', '0',
    '-i', (Join-Path $work "frames\$lang\%04d.png"), '-c:v', 'libx264', '-preset', 'slow', '-crf', '18',
    '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-r', '30', '-an', $mp4)
  Write-Host "[$lang] encoded $mp4"
}
