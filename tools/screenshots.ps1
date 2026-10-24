<#
.SYNOPSIS
  Captures README screenshots of Sela (always with --mock) on a neutral backdrop,
  then composes docs/images/hero.png and (if ffmpeg exists) docs/images/sky-tour.gif.

.NOTES
  Privacy: a borderless dark backdrop covers the primary screen before Sela starts,
  so the real desktop / wallpaper / other windows never end up in a capture.
  Usage:  powershell -ExecutionPolicy Bypass -File tools/screenshots.ps1 [-SkipBuild]
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $PSScriptRoot
$exe    = Join-Path $root 'src\Sela\bin\Debug\net10.0-windows\Sela.exe'
$outDir = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace SelaShot -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr v);
[DllImport("user32.dll")] public static extern uint GetDpiForSystem();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern System.IntPtr FindWindow(string cls, string title);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(System.IntPtr h, int attr, out RECT r, int size);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

# Physical pixel coordinates everywhere (per-monitor v2).
[void][SelaShot.Native]::SetProcessDpiAwarenessContext([IntPtr](-4))
$scale  = [SelaShot.Native]::GetDpiForSystem() / 96.0
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

# ---- 1. wait for --mock, build ----
$appCs = Join-Path $root 'src\Sela\App.xaml.cs'
$deadline = [DateTime]::Now.AddMinutes(15)
while (-not (Select-String -Path $appCs -Pattern '--mock' -SimpleMatch -Quiet)) {
    if ([DateTime]::Now -gt $deadline) { throw '--mock flag not found in App.xaml.cs after 15 min' }
    Write-Host 'Waiting for --mock support in App.xaml.cs...'; Start-Sleep -Seconds 20
}
if (-not $SkipBuild) {
    Stop-Sela
    for ($i = 1; $i -le 6; $i++) {
        & dotnet build (Join-Path $root 'src\Sela') -c Debug --nologo -v q
        if ($LASTEXITCODE -eq 0) { break }
        if ($i -eq 6) { throw 'dotnet build failed 6 times' }
        Write-Host "Build failed (attempt $i), retrying in 30 s..."; Start-Sleep -Seconds 30
    }
}
if (-not (Test-Path $exe)) { throw "Sela.exe not found: $exe" }

# ---- 2. backdrop ----
# 1 px short of full height so Sela's full-screen detector (SHQueryUserNotificationState)
# doesn't treat the backdrop as a full-screen app and hide the notch.
$backdrop = New-Object System.Windows.Forms.Form
$backdrop.FormBorderStyle = 'None'
$backdrop.StartPosition   = 'Manual'
$backdrop.ShowInTaskbar   = $false
$backdrop.TopMost         = $false
$backdrop.Bounds          = New-Object System.Drawing.Rectangle($screen.X, $screen.Y, $screen.Width, ($screen.Height - 1))
$backdrop.BackColor       = $bgTop
$backdrop.Add_Paint({ param($s, $e) Fill-Gradient $e.Graphics $s.ClientRectangle })

function Capture([System.Drawing.Rectangle]$r, [string]$name) {
    $r = [System.Drawing.Rectangle]::Intersect($r, $screen)
    $bmp = New-Object System.Drawing.Bitmap($r.Width, $r.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Location, [System.Drawing.Point]::Empty, $r.Size)
    $g.Dispose()
    $path = Join-Path $outDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "  saved $name ($($r.Width)x$($r.Height))"
}

# Top-center region for a notch of the given DIP size, with padding (no top padding: it hugs the edge).
function Notch-Rect([double]$wDip, [double]$hDip, [double]$padDip = 28) {
    $w = [int][Math]::Round(($wDip + 2 * $padDip) * $scale)
    $h = [int][Math]::Round(($hDip + $padDip) * $scale)
    New-Object System.Drawing.Rectangle(($screen.X + [int](($screen.Width - $w) / 2)), $screen.Y, $w, $h)
}

$cursorCenter = { [void][SelaShot.Native]::SetCursorPos($screen.X + [int]($screen.Width / 2), $screen.Bottom - 40) }
$cursorCorner = { [void][SelaShot.Native]::SetCursorPos($screen.Right - 2, $screen.Bottom - 2) }

$missing = New-Object System.Collections.Generic.List[string]

function Shot([string]$name, [string[]]$appArgs, [double]$wait, [scriptblock]$cursor, [scriptblock]$region) {
    Write-Host "Capturing $name  (Sela $($appArgs -join ' '))"
    Stop-Sela
    & $cursor
    $p = Start-Process -FilePath $exe -ArgumentList (@('--mock') + $appArgs) -PassThru
    Wait-Pump $wait
    & $cursor
    Wait-Pump 0.4
    try {
        if ($p.HasExited) { throw "Sela exited early (code $($p.ExitCode))" }
        $r = & $region
        if ($null -eq $r) { throw 'capture region not found' }
        if ($r -is [System.Drawing.Rectangle]) { Capture $r $name }   # otherwise the region block saved it itself
    } catch { Write-Warning "  $name failed: $_"; $missing.Add($name) }
    Stop-Sela
}

try {
    $backdrop.Show()
    Wait-Pump 1.0

    foreach ($sky in 'night', 'day', 'dawn', 'dusk') {
        Shot "idle-$sky.png" @("--sky=$sky") 4.0 $cursorCenter { Notch-Rect 214 30 }
    }
    Shot 'panel-sholat.png'   @('--sky=night', '--panel')        4.0 $cursorCorner { Notch-Rect 344 412 }
    Shot 'panel-agenda.png'   @('--sky=night', '--panel=agenda') 4.0 $cursorCorner { Notch-Rect 344 412 }
    Shot 'reminder-adzan.png' @('--sky=night', '--test-adhan')   5.0 $cursorCorner { Notch-Rect 380 92 }
    Shot 'settings.png'       @('--sky=night', '--settings')     4.0 $cursorCorner {
        # [NullString]::Value: a plain $null would be marshalled as "" (class name "") and never match.
        $h = [IntPtr]::Zero
        for ($i = 0; $i -lt 20 -and $h -eq [IntPtr]::Zero; $i++) {
            $h = [SelaShot.Native]::FindWindow([NullString]::Value, 'Pengaturan Sela')
            if ($h -eq [IntPtr]::Zero) { Wait-Pump 0.25 }
        }
        if ($h -eq [IntPtr]::Zero) { return $null }
        $rc = New-Object SelaShot.Native+RECT
        # Extended frame bounds = visible window without the invisible resize border.
        if ([SelaShot.Native]::DwmGetWindowAttribute($h, 9, [ref]$rc, 16) -ne 0) { [void][SelaShot.Native]::GetWindowRect($h, [ref]$rc) }
        # The window is taller than the area above the (topmost) taskbar, so move it fully
        # into the work area, grab only the window itself and pad it onto a generated
        # backdrop: nothing outside the window (taskbar, screen edge) can leak in.
        $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $wr = New-Object SelaShot.Native+RECT
        [void][SelaShot.Native]::GetWindowRect($h, [ref]$wr)
        $dx = $wr.Left - $rc.Left; $dy = $wr.Top - $rc.Top                # invisible border offsets
        $winH = $rc.Bottom - $rc.Top; $winW = $rc.Right - $rc.Left
        $newTop = $work.Top + [Math]::Max(0, [int](($work.Height - $winH) / 2))
        $newLeft = $work.Left + [int](($work.Width - $winW) / 2)
        [void][SelaShot.Native]::SetWindowPos($h, [IntPtr]::Zero, ($newLeft + $dx), ($newTop + $dy), 0, 0, 0x0015)  # NOSIZE|NOZORDER|NOACTIVATE
        Wait-Pump 0.8
        [void][SelaShot.Native]::DwmGetWindowAttribute($h, 9, [ref]$rc, 16)
        $win = [System.Drawing.Rectangle]::Intersect(
            (New-Object System.Drawing.Rectangle($rc.Left, $rc.Top, ($rc.Right - $rc.Left), ($rc.Bottom - $rc.Top))), $work)
        $pad = [int](40 * $scale)
        $canvas = New-Object System.Drawing.Bitmap(($win.Width + 2 * $pad), ($win.Height + 2 * $pad), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        Fill-Gradient $g (New-Object System.Drawing.Rectangle(0, 0, $canvas.Width, $canvas.Height))
        $g.CopyFromScreen($win.Location, (New-Object System.Drawing.Point($pad, $pad)), $win.Size)
        $g.Dispose()
        $canvas.Save((Join-Path $outDir 'settings.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "  saved settings.png ($($canvas.Width)x$($canvas.Height), window $($win.Width)x$($win.Height))"
        $canvas.Dispose()
        'done'
    }
}
finally {
    Stop-Sela
    $backdrop.Close(); $backdrop.Dispose()
}

# ---- 3. hero.png (1600x640) ----
function Rounded([System.Drawing.RectangleF]$r, [float]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $rad
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90); $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90); $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

# Screenshot as a rounded card with a soft layered shadow and a hairline border.
function Draw-Card($g, [System.Drawing.Image]$img, [System.Drawing.RectangleF]$r, [float]$rad) {
    for ($i = 14; $i -ge 1; $i--) {
        $sr = New-Object System.Drawing.RectangleF(($r.X - $i), ($r.Y - $i + 10), ($r.Width + 2 * $i), ($r.Height + 2 * $i))
        $sp = Rounded $sr ($rad + $i)
        $sb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(9, 0, 0, 0))
        $g.FillPath($sb, $sp); $sb.Dispose(); $sp.Dispose()
    }
    $path = Rounded $r $rad
    $g.SetClip($path); $g.DrawImage($img, $r); $g.ResetClip()
    $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#2A3242'), 1.5)
    $g.DrawPath($pen, $path); $pen.Dispose(); $path.Dispose()
}

$panelPath = Join-Path $outDir 'panel-sholat.png'
$pillPath  = Join-Path $outDir 'idle-night.png'
if ((Test-Path $panelPath) -and (Test-Path $pillPath)) {
    Write-Host 'Composing hero.png'
    $W = 1600; $H = 640
    $hero = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($hero)
    $g.SmoothingMode      = 'AntiAlias'
    $g.InterpolationMode  = 'HighQualityBicubic'
    $g.PixelOffsetMode    = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.TextRenderingHint  = 'AntiAliasGridFit'
    Fill-Gradient $g (New-Object System.Drawing.Rectangle(0, 0, $W, $H))

    # faint green glow behind the screenshots
    $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glow.AddEllipse(980, 40, 560, 560)
    $gb = New-Object System.Drawing.Drawing2D.PathGradientBrush($glow)
    $gb.CenterColor = [System.Drawing.Color]::FromArgb(38, 61, 220, 151)
    $gb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 61, 220, 151))
    $g.FillPath($gb, $glow); $gb.Dispose(); $glow.Dispose()

    # text block
    $family = 'Segoe UI Variable Display'
    $test = New-Object System.Drawing.Font($family, 10)
    if ($test.Name -ne $family) { $family = 'Segoe UI Semibold' }; $test.Dispose()
    $titleFont = New-Object System.Drawing.Font($family, 132, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    if ($family -eq 'Segoe UI Variable Display') {
        # Semibold weight lives in the variable font's named instance.
        $sb = New-Object System.Drawing.Font('Segoe UI Variable Display Semib', 132, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        if ($sb.Name -eq 'Segoe UI Variable Display Semib') { $titleFont.Dispose(); $titleFont = $sb } else { $sb.Dispose() }
    }
    $tagFont = New-Object System.Drawing.Font('Segoe UI Variable Text', 34, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    if ($tagFont.Name -ne 'Segoe UI Variable Text') { $tagFont.Dispose(); $tagFont = New-Object System.Drawing.Font('Segoe UI', 34, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel) }

    $green = [System.Drawing.ColorTranslator]::FromHtml('#3DDC97')
    $x = 120
    $gbr = New-Object System.Drawing.SolidBrush($green)
    $g.FillRectangle($gbr, $x + 6, 200, 56, 6)                      # accent line
    $titleSize = $g.MeasureString('Sela', $titleFont)
    $g.DrawString('Sela', $titleFont, [System.Drawing.Brushes]::White, $x - 4, 222)
    $g.FillEllipse($gbr, ($x + $titleSize.Width - 14), 222 + $titleSize.Height * 0.70, 20, 20)  # accent dot after the word
    $tagBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#9C9CA3'))
    $g.DrawString('Jeda sejenak di sela kesibukan', $tagFont, $tagBrush, $x + 4, 222 + $titleSize.Height + 4)
    $gbr.Dispose(); $tagBrush.Dispose(); $titleFont.Dispose(); $tagFont.Dispose()

    # right side: pill on top, panel below, scaled to fit 560 px height
    $panel = [System.Drawing.Image]::FromFile($panelPath)
    $pill  = [System.Drawing.Image]::FromFile($pillPath)
    $gap = 22.0; $avail = $H - 80.0
    $s = [Math]::Min(1.0, ($avail - $gap) / ($pill.Height + $panel.Height))
    $pw = $panel.Width * $s; $ph = $panel.Height * $s
    $lw = $pill.Width * $s;  $lh = $pill.Height * $s
    $cx = 1250.0
    $top = ($H - ($lh + $gap + $ph)) / 2
    Draw-Card $g $pill  (New-Object System.Drawing.RectangleF(($cx - $lw / 2), $top, $lw, $lh)) 14
    Draw-Card $g $panel (New-Object System.Drawing.RectangleF(($cx - $pw / 2), ($top + $lh + $gap), $pw, $ph)) 18
    $panel.Dispose(); $pill.Dispose(); $g.Dispose()
    $hero.Save((Join-Path $outDir 'hero.png'), [System.Drawing.Imaging.ImageFormat]::Png); $hero.Dispose()
} else { Write-Warning 'hero.png skipped: panel-sholat.png or idle-night.png missing'; $missing.Add('hero.png') }

# ---- 4. optional sky-tour.gif ----
$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
$skies = 'dawn', 'day', 'dusk', 'night' | ForEach-Object { Join-Path $outDir "idle-$_.png" }
if ($ffmpeg -and -not ($skies | Where-Object { -not (Test-Path $_) })) {
    Write-Host 'Building sky-tour.gif'
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('sela-gif-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        for ($i = 0; $i -lt 4; $i++) { Copy-Item $skies[$i] (Join-Path $tmp "f$i.png") }
        & $ffmpeg.Source -hide_banner -loglevel error -y -framerate 0.8 -i (Join-Path $tmp 'f%d.png') `
            -vf 'split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=sierra2_4a' -loop 0 (Join-Path $outDir 'sky-tour.gif')
        if ($LASTEXITCODE -ne 0) { $missing.Add('sky-tour.gif') }
    } finally { Remove-Item -Recurse -Force $tmp }
}

# ---- 5. validation ----
function Test-Image([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($path)
    $w = $bmp.Width; $h = $bmp.Height
    $dark = 0; $green = 0; $bright = 0; $n = 0; $edgeMax = 0; $min = 765; $max = 0
    for ($y = 0; $y -lt $h; $y += 2) {
        for ($x = 0; $x -lt $w; $x += 2) {
            $c = $bmp.GetPixel($x, $y); $n++
            $sum = $c.R + $c.G + $c.B
            if ($sum -lt $min) { $min = $sum }; if ($sum -gt $max) { $max = $sum }
            if ($c.R -lt 12 -and $c.G -lt 12 -and $c.B -lt 14) { $dark++ }
            if ([Math]::Abs($c.R - 61) -lt 40 -and [Math]::Abs($c.G - 220) -lt 40 -and [Math]::Abs($c.B - 151) -lt 45) { $green++ }
            if ($sum -gt 690) { $bright++ }
            # outer ring (6 px) must be backdrop, except the top edge of top-anchored notch shots
            $edge = ($x -lt 6 -or $x -ge $w - 6 -or $y -ge $h - 6 -or ($y -lt 6 -and $path -match 'settings'))
            if ($edge -and $sum -gt $edgeMax) { $edgeMax = $sum }
        }
    }
    $bmp.Dispose()
    [pscustomobject]@{
        File = Split-Path -Leaf $path; Size = "${w}x$h"; KB = [Math]::Round((Get-Item $path).Length / 1KB, 1)
        NotUniform = ($max - $min) -gt 60; DarkPct = [Math]::Round(100.0 * $dark / $n, 1)
        GreenPx = $green; BrightPct = [Math]::Round(100.0 * $bright / $n, 2); EdgeMaxLum = [int]($edgeMax / 3)
    }
}
Write-Host ''
Get-ChildItem $outDir -Filter *.png | ForEach-Object { Test-Image $_.FullName } | Format-Table -AutoSize | Out-String | Write-Host
Get-ChildItem $outDir -Filter *.gif | ForEach-Object { Write-Host ("{0}  {1:N1} KB" -f $_.Name, ($_.Length / 1KB)) }
if ($missing.Count) { Write-Warning ('Not captured: ' + ($missing -join ', ')) }
