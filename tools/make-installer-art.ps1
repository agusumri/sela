<#
  Renders the Inno Setup wizard art for Sela from src/Sela/Assets/SelaIcon.xaml plus plain WPF drawing:
    installer/art/wizard-<W>x<H>.png   WizardImageFile (left panel of Welcome / Finished pages)
    installer/art/small-<N>.png        WizardSmallImageFile (top-right corner of inner pages, transparent)
  Sizes are the image-area sizes Inno Setup 6.6+ uses at 100..250% DPI (default font/WizardSizePercent),
  so Setup picks an exact match instead of stretching.
  WPF needs an STA thread:  powershell -STA -ExecutionPolicy Bypass -File tools/make-installer-art.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $scriptDir '..\installer\art' }

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -OutDir $OutDir
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$xamlPath = (Resolve-Path -LiteralPath (Join-Path $scriptDir '..\src\Sela\Assets\SelaIcon.xaml')).Path

$stream = [IO.File]::OpenRead($xamlPath)
try { $dict = [Windows.Markup.XamlReader]::Load($stream) } finally { $stream.Dispose() }
$logo = $dict['SelaLogo']
if (-not $logo) { throw "SelaLogo not found in $xamlPath" }

function C([string]$hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Brush([string]$hex) { $b = New-Object Windows.Media.SolidColorBrush (C $hex); $b.Freeze(); $b }
function Pt([double]$x, [double]$y) { New-Object Windows.Point $x, $y }
function Rect([double]$x, [double]$y, [double]$w, [double]$h) { New-Object Windows.Rect $x, $y, $w, $h }
function Radial([string]$inner, [string]$outer) {
    $b = New-Object Windows.Media.RadialGradientBrush
    $b.GradientStops.Add((New-Object Windows.Media.GradientStop (C $inner), 0))
    $b.GradientStops.Add((New-Object Windows.Media.GradientStop (C $outer), 1))
    $b.Freeze(); $b
}
function Crescent([double]$cx, [double]$cy, [double]$r) {
    # Same proportions as the logo crescent (outer r=31, cut r=25 offset +13,-10).
    $k = $r / 31.0
    New-Object Windows.Media.CombinedGeometry ([Windows.Media.GeometryCombineMode]::Exclude),
        (New-Object Windows.Media.EllipseGeometry (Pt $cx $cy), $r, $r),
        (New-Object Windows.Media.EllipseGeometry (Pt ($cx + 13 * $k) ($cy - 10 * $k)), (25 * $k), (25 * $k))
}

function Save-Png([Windows.Media.Visual]$visual, [int]$w, [int]$h, [string]$path) {
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $fs = [IO.File]::Create($path)
    try { $enc.Save($fs) } finally { $fs.Dispose() }
}

# Wordmark typeface: Segoe UI Variable Display (Windows 11), falling back to Segoe UI.
$typeface = New-Object Windows.Media.Typeface (New-Object Windows.Media.FontFamily 'Segoe UI Variable Display, Segoe UI'),
    ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::SemiBold), ([Windows.FontStretches]::Normal)

# ---- Left panel. Designed on a 202-unit-wide canvas (the 100% size) and scaled uniformly. ----
function Render-Wizard([int]$w, [int]$h) {
    $s = $w / 202.0
    $H = $h / $s                      # canvas height in design units (~386)
    $cx = 101.0; $cy = 132.0          # logo centre

    $v = New-Object Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform $s, $s))

    # Night-sky gradient, same pair as the store screenshots.
    $bg = New-Object Windows.Media.LinearGradientBrush (C '#0E1116'), (C '#1B2230'), (Pt 0 0), (Pt 0.35 1)
    $dc.DrawRectangle($bg, $null, (Rect 0 0 202 $H))

    # A few faint stars in the upper sky, kept clear of the logo.
    $stars = @(
        @(22, 28, 0.9, '#66FFFFFF'), @(58, 16, 0.6, '#44FFFFFF'), @(150, 22, 0.8, '#55FFFFFF'),
        @(181, 52, 0.6, '#3DFFFFFF'), @(16, 86, 0.6, '#33FFFFFF'), @(188, 112, 0.7, '#40FFFFFF'),
        @(30, 196, 0.5, '#2EFFFFFF'), @(174, 206, 0.6, '#33FFFFFF'), @(118, 10, 0.5, '#3DFFFFFF')
    )
    foreach ($st in $stars) { $dc.DrawEllipse((Brush $st[3]), $null, (Pt $st[0] $st[1]), $st[2], $st[2]) }

    # Soft green glow behind the mark, plus a wider, fainter wash.
    $dc.DrawEllipse((Radial '#203DDC97' '#003DDC97'), $null, (Pt $cx $cy), 150, 150)
    $dc.DrawEllipse((Radial '#463DDC97' '#003DDC97'), $null, (Pt $cx $cy), 92, 92)

    # Thin halo ring around the tile.
    $ring = New-Object Windows.Media.Pen (Brush '#2A3DDC97'), 0.8
    $dc.DrawEllipse($null, $ring, (Pt $cx $cy), 78, 78)
    $dc.DrawEllipse((Brush '#993DDC97'), $null, (Pt ($cx + 78 * [Math]::Cos(-0.9)) ($cy + 78 * [Math]::Sin(-0.9))), 1.6, 1.6)

    # Shadow under the tile, then the app mark itself (128 units, tile is 120 of that).
    $dc.DrawEllipse((Radial '#AA000000' '#00000000'), $null, (Pt $cx ($cy + 54)), 58, 14)
    $dc.DrawImage($logo, (Rect ($cx - 64) ($cy - 64) 128 128))

    # Wordmark.
    $ft = New-Object Windows.Media.FormattedText 'Sela', ([Globalization.CultureInfo]::InvariantCulture),
        ([Windows.FlowDirection]::LeftToRight), $typeface, 38, (Brush '#F2F5F7'), 1.0
    $dc.DrawText($ft, (Pt ($cx - $ft.Width / 2) 214))

    # Short accent rule under the wordmark.
    $dc.DrawRoundedRectangle((Brush '#3DDC97'), $null, (Rect ($cx - 11) 268 22 2.4), 1.2, 1.2)

    # Notch pill motif near the bottom: true-black pill, faint rim, tiny crescent and sensor dot.
    $pw = 78.0; $ph = 22.0; $py = $H - 52
    $rim = New-Object Windows.Media.Pen (Brush '#2E3440'), 0.8
    $dc.DrawRoundedRectangle((Brush '#000000'), $rim, (Rect ($cx - $pw / 2) $py $pw $ph), ($ph / 2), ($ph / 2))
    $moon = New-Object Windows.Media.LinearGradientBrush (C '#8AF5C6'), (C '#22B477'), (Pt 0 0), (Pt 1 1)
    $dc.DrawGeometry($moon, $null, (Crescent ($cx - $pw / 2 + 14) ($py + $ph / 2) 6.2))
    $dc.DrawRoundedRectangle((Brush '#33FFFFFF'), $null, (Rect ($cx - 10) ($py + $ph / 2 - 1) 24 2), 1, 1)
    $dc.DrawEllipse((Brush '#3DDC97'), $null, (Pt ($cx + $pw / 2 - 12) ($py + $ph / 2)), 2.2, 2.2)

    $dc.Pop()
    $dc.Close()
    $v
}

# ---- Corner image: the app mark on transparency. ----
function Render-Small([int]$n) {
    $v = New-Object Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    $dc.DrawImage($logo, (Rect 0 0 $n $n))
    $dc.Close()
    $v
}

$wizardSizes = @(@(202, 386), @(269, 515), @(336, 643), @(403, 772), @(430, 824), @(498, 953), @(534, 1022))
$smallSizes  = 58, 77, 97, 116, 124, 143, 159

Get-ChildItem -LiteralPath $OutDir -Filter '*.png' | Where-Object { $_.Name -like 'wizard-*' -or $_.Name -like 'small-*' } | Remove-Item
foreach ($sz in $wizardSizes) {
    $p = Join-Path $OutDir ("wizard-{0}x{1}.png" -f $sz[0], $sz[1])
    Save-Png (Render-Wizard $sz[0] $sz[1]) $sz[0] $sz[1] $p
    Write-Host "Wrote $p"
}
foreach ($n in $smallSizes) {
    $p = Join-Path $OutDir ("small-{0}.png" -f $n)
    Save-Png (Render-Small $n) $n $n $p
    Write-Host "Wrote $p"
}
