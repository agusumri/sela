<#
  Renders the Sela brand kit into docs/brand/ from src/Sela/Assets/SelaIcon.xaml (vector only, 1:1 pixels):
    logo/    sela-logo-{512,1024,2048,4096}.png, sela-mark-{1024,4096}.png, sela-logo.svg
    lockup/  sela-lockup-horizontal-{dark,light}.png
    social/  github-social-preview.png, instagram-{post,story}-{en,id}.png, profile-avatar.png,
             x-header.png, linkedin-banner.png
  WPF needs an STA thread:  powershell -STA -ExecutionPolicy Bypass -File tools/make-brand-kit.ps1
  -LayoutReport <file.json> also writes the ink bounds of every logo/text element (used for QA).
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$LayoutReport
)
$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $scriptDir '..\docs\brand' }

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $argList = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-OutDir', $OutDir)
    if ($LayoutReport) { $argList += @('-LayoutReport', $LayoutReport) }
    & powershell.exe @argList
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$inv = [Globalization.CultureInfo]::InvariantCulture
foreach ($d in 'logo', 'lockup', 'social') { New-Item -ItemType Directory -Force -Path (Join-Path $OutDir $d) | Out-Null }
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$xamlPath = (Resolve-Path -LiteralPath (Join-Path $scriptDir '..\src\Sela\Assets\SelaIcon.xaml')).Path

$stream = [IO.File]::OpenRead($xamlPath)
try { $dict = [Windows.Markup.XamlReader]::Load($stream) } finally { $stream.Dispose() }
$logo = $dict['SelaLogo']
if (-not $logo) { throw "SelaLogo not found in $xamlPath" }

# SelaLogo children: 0 bounds, 1 tile, 2 notch pill, 3 glow, 4 crescent, 5 sensor dot.
$parts = $logo.Drawing.Children
if ($parts.Count -ne 6) { throw "Unexpected SelaLogo structure ($($parts.Count) drawings); update make-brand-kit.ps1" }

# ---------------------------------------------------------------- palette / type
$C_BG0 = '#0E1116'; $C_BG1 = '#1B2230'; $C_TEXT = '#F5F5F7'; $C_SECOND = '#9C9CA3'; $C_ACCENT = '#3DDC97'
$C_LIGHT_BG = '#F7F7F8'; $C_DARK_TEXT = '#0E1116'
$TAG_EN = 'A short pause between everything else'
$TAG_ID = 'Jeda sejenak di sela kesibukan'
$URL = 'github.com/agusumri/sela'
$FEATURES = "Prayer times $([char]0x00B7) Adhan $([char]0x00B7) Meetings $([char]0x2014) for Windows"

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
function Face([string]$family, $weight) {
    New-Object Windows.Media.Typeface (New-Object Windows.Media.FontFamily "$family, Segoe UI"),
        ([Windows.FontStyles]::Normal), $weight, ([Windows.FontStretches]::Normal)
}
$F_HEAD = Face 'Segoe UI Variable Display' ([Windows.FontWeights]::SemiBold)
$F_BODY = Face 'Segoe UI Variable Text' ([Windows.FontWeights]::Normal)
$F_BODY_SB = Face 'Segoe UI Variable Text' ([Windows.FontWeights]::SemiBold)

# ---------------------------------------------------------------- layout log (QA)
$script:layout = New-Object System.Collections.ArrayList
$script:current = ''
function Note([string]$kind, [string]$name, [Windows.Rect]$r) {
    [void]$script:layout.Add([ordered]@{ file = $script:current; kind = $kind; name = $name
        x = [Math]::Round($r.X, 1); y = [Math]::Round($r.Y, 1); right = [Math]::Round($r.Right, 1); bottom = [Math]::Round($r.Bottom, 1) })
}

# ---------------------------------------------------------------- output
function Save-Png([Windows.Media.Visual]$visual, [int]$w, [int]$h, [string]$rel, [bool]$opaque) {
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $src = [Windows.Media.Imaging.BitmapSource]$rtb
    # Opaque art is stored as 24-bit RGB: smaller and guaranteed alpha-free.
    if ($opaque) { $src = New-Object Windows.Media.Imaging.FormatConvertedBitmap $rtb, ([Windows.Media.PixelFormats]::Bgr24), $null, 0 }
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Interlace = [Windows.Media.Imaging.PngInterlaceOption]::Off
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($src))
    $path = Join-Path $OutDir $rel
    $fs = [IO.File]::Create($path)
    try { $enc.Save($fs) } finally { $fs.Dispose() }
    Write-Host "Wrote $rel"
}
function Scene([string]$rel, [int]$w, [int]$h, [bool]$opaque, [scriptblock]$draw) {
    $script:current = $rel -replace '\\', '/'
    $v = New-Object Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    & $draw $dc $w $h
    $dc.Close()
    Save-Png $v $w $h $rel $opaque
}

# ---------------------------------------------------------------- building blocks
# Night-sky gradient + soft green glow at (gx, gy) + a few faint stars kept away from the glow.
function Draw-Sky($dc, [double]$w, [double]$h, [double]$gx, [double]$gy, [double]$gr, [int]$stars = 10) {
    $bg = New-Object Windows.Media.LinearGradientBrush (C $C_BG0), (C $C_BG1), (Pt 0 0), (Pt 0.35 1)
    $dc.DrawRectangle($bg, $null, (Rect 0 0 $w $h))
    $rnd = New-Object Random 7
    $u = [Math]::Min($w, $h) / 400.0
    $placed = 0; $tries = 0
    while ($placed -lt $stars -and $tries -lt 500) {
        $tries++
        $x = $rnd.NextDouble() * $w; $y = $rnd.NextDouble() * $h * 0.55
        if ([Math]::Sqrt(($x - $gx) * ($x - $gx) + ($y - $gy) * ($y - $gy)) -lt $gr * 1.15) { continue }
        $a = 0x26 + $rnd.Next(0x22)
        $dc.DrawEllipse((Brush ('#{0:X2}FFFFFF' -f $a)), $null, (Pt $x $y), ((0.5 + $rnd.NextDouble() * 0.5) * $u), ((0.5 + $rnd.NextDouble() * 0.5) * $u))
        $placed++
    }
    $dc.DrawEllipse((Radial '#1C3DDC97' '#003DDC97'), $null, (Pt $gx $gy), ($gr * 1.6), ($gr * 1.6))
    $dc.DrawEllipse((Radial '#3A3DDC97' '#003DDC97'), $null, (Pt $gx $gy), $gr, $gr)
}

# Logo tile centred at (cx, cy); S is the 256-unit canvas size (tile is 240/256 of it).
function Draw-Logo($dc, [double]$cx, [double]$cy, [double]$S, [bool]$light = $false) {
    $shadow = if ($light) { '#38000000' } else { '#AA000000' }
    $dc.DrawEllipse((Radial $shadow '#00000000'), $null, (Pt $cx ($cy + 0.42 * $S)), (0.46 * $S), (0.11 * $S))
    $dc.DrawImage($logo, (Rect ($cx - $S / 2) ($cy - $S / 2) $S $S))
    $t = $S * 8 / 256
    Note 'logo' 'tile' (Rect ($cx - $S / 2 + $t) ($cy - $S / 2 + $t) ($S - 2 * $t) ($S - 2 * $t))
}

function New-Text([string]$s, $face, [double]$size, [string]$hex, [double]$maxW = 0) {
    $ft = New-Object Windows.Media.FormattedText $s, $inv, ([Windows.FlowDirection]::LeftToRight), $face, $size, (Brush $hex), 1.0
    if ($maxW -gt 0) { $ft.MaxTextWidth = $maxW; $ft.TextAlignment = [Windows.TextAlignment]::Center }
    $ft
}
function Ink($ft) { $ft.BuildGeometry((Pt 0 0)).Bounds }

# Vertical stack of text lines / accent rules, spaced by ink (what the eye sees, not line boxes).
# Items: @{ ft = <FormattedText>; gap = <space above> }  or  @{ rule = <width>; gap = ... }
function Measure-Stack($items) {
    $w = 0.0; $h = 0.0
    foreach ($it in $items) {
        $h += $it.gap
        if ($it.rule) { $iw = $it.rule; $ih = $it.rule / 9.0 } else { $r = Ink $it.ft; $iw = $r.Width; $ih = $r.Height }
        $w = [Math]::Max($w, $iw); $h += $ih
    }
    @{ W = $w; H = $h }
}
function Draw-Stack($dc, $items, [double]$x, [double]$top, [string]$align, [double]$blockW) {
    $y = $top; $i = 0
    foreach ($it in $items) {
        $y += $it.gap
        if ($it.rule) {
            $rw = $it.rule; $rh = $rw / 9.0
            $rx = if ($align -eq 'center') { $x + ($blockW - $rw) / 2 } else { $x }
            $dc.DrawRoundedRectangle((Brush $C_ACCENT), $null, (Rect $rx $y $rw $rh), ($rh / 2), ($rh / 2))
            $y += $rh
        } else {
            $r = Ink $it.ft
            $ox = if ($align -eq 'center') { $x + ($blockW - $r.Width) / 2 - $r.X } else { $x - $r.X }
            $dc.DrawText($it.ft, (Pt $ox ($y - $r.Y)))
            Note 'text' ("line{0}" -f $i) (Rect ($ox + $r.X) $y $r.Width $r.Height)
            $y += $r.Height
        }
        $i++
    }
}

# Logo on the left, text stack on the right, both vertically centred on cy.
# Returns nothing; tileLeft = x of the visible tile's left edge.
function Draw-Horizontal($dc, [double]$tileLeft, [double]$cy, [double]$S, [double]$gap, $items, [bool]$light = $false) {
    $t = $S * 8 / 256
    $cx = $tileLeft - $t + $S / 2
    Draw-Logo $dc $cx $cy $S $light
    $m = Measure-Stack $items
    Draw-Stack $dc $items ($tileLeft + $S - 2 * $t + $gap) ($cy - $m.H / 2) 'left' $m.W
}
function Horizontal-Width([double]$S, [double]$gap, $items) { $S * 240 / 256 + $gap + (Measure-Stack $items).W }

# ---------------------------------------------------------------- logo + mark
foreach ($n in 512, 1024, 2048, 4096) {
    Scene "logo\sela-logo-$n.png" $n $n $false { param($dc, $w, $h)
        $dc.DrawImage($logo, (Rect 0 0 $w $h)); Note 'logo' 'tile' (Rect ($w * 8 / 256) ($h * 8 / 256) ($w * 240 / 256) ($h * 240 / 256)) }
}

# Mark = pill (+rim), glow, crescent, dot. Pill outer bounds incl. rim: x 30.5..225.5, y 80.5..175.5, centred on 128,128.
$mark = New-Object Windows.Media.DrawingGroup
foreach ($i in 2..5) { [void]$mark.Children.Add($parts[$i]) }
$mark.Freeze()
foreach ($n in 1024, 4096) {
    Scene "logo\sela-mark-$n.png" $n $n $false { param($dc, $w, $h)
        $k = ($w * 0.88) / 195.0
        $tg = New-Object Windows.Media.TransformGroup
        $tg.Children.Add((New-Object Windows.Media.TranslateTransform -128, -128))
        $tg.Children.Add((New-Object Windows.Media.ScaleTransform $k, $k))
        $tg.Children.Add((New-Object Windows.Media.TranslateTransform ($w / 2), ($h / 2)))
        $dc.PushTransform($tg); $dc.DrawDrawing($mark); $dc.Pop()
        Note 'logo' 'mark' (Rect ($w / 2 - 97.5 * $k) ($h / 2 - 47.5 * $k) (195 * $k) (95 * $k))
    }
}

# SVG: direct translation of SelaLogo. WPF relative (bounding-box) gradients map to SVG objectBoundingBox,
# except the crescent, whose box is the CombinedGeometry's bounds -> expressed with a gradientTransform.
function N([double]$v) { $v.ToString('0.###', $inv) }
$cb = $parts[4].Geometry.Bounds
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 256 256">
  <title>Sela</title>
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#222222"/>
      <stop offset="1" stop-color="#0B0B0B"/>
    </linearGradient>
    <linearGradient id="rim" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#3A3A3A"/>
      <stop offset="1" stop-color="#151515"/>
    </linearGradient>
    <radialGradient id="glow" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0" stop-color="#3DDC97" stop-opacity="0.333"/>
      <stop offset="1" stop-color="#3DDC97" stop-opacity="0"/>
    </radialGradient>
    <linearGradient id="moon" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="1" y2="1"
                    gradientTransform="matrix($(N $cb.Width) 0 0 $(N $cb.Height) $(N $cb.X) $(N $cb.Y))">
      <stop offset="0" stop-color="#8AF5C6"/>
      <stop offset="0.55" stop-color="#3DDC97"/>
      <stop offset="1" stop-color="#22B477"/>
    </linearGradient>
    <mask id="cut" maskUnits="userSpaceOnUse" x="0" y="0" width="256" height="256">
      <rect width="256" height="256" fill="#fff"/>
      <circle cx="127" cy="118" r="25" fill="#000"/>
    </mask>
  </defs>
  <rect x="8" y="8" width="240" height="240" rx="56" fill="url(#tile)"/>
  <rect x="32" y="82" width="192" height="92" rx="46" fill="#000" stroke="url(#rim)" stroke-width="3"/>
  <ellipse cx="114" cy="128" rx="46" ry="40" fill="url(#glow)"/>
  <circle cx="114" cy="128" r="31" fill="url(#moon)" mask="url(#cut)"/>
  <circle cx="176" cy="128" r="9" fill="#3DDC97"/>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $OutDir 'logo\sela-logo.svg'), ($svg -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))
Write-Host 'Wrote logo\sela-logo.svg'

# ---------------------------------------------------------------- lockups (3000x1000)
foreach ($variant in 'dark', 'light') {
    Scene "lockup\sela-lockup-horizontal-$variant.png" 3000 1000 $true { param($dc, $w, $h)
        $light = $variant -eq 'light'
        $S = 620.0; $gap = 110.0
        $items = @(@{ ft = (New-Text 'Sela' $F_HEAD 420 $(if ($light) { $C_DARK_TEXT } else { $C_TEXT })); gap = 0 })
        $left = ($w - (Horizontal-Width $S $gap $items)) / 2
        $lcx = $left - $S * 8 / 256 + $S / 2
        if ($light) { $dc.DrawRectangle((Brush $C_LIGHT_BG), $null, (Rect 0 0 $w $h)) }
        else { Draw-Sky $dc $w $h $lcx ($h / 2) 520 14 }
        Draw-Horizontal $dc $left ($h / 2) $S $gap $items $light
    }
}

# ---------------------------------------------------------------- GitHub social preview (1280x640, safe 1200x600)
Scene 'social\github-social-preview.png' 1280 640 $true { param($dc, $w, $h)
    $S = 300.0; $gap = 64.0
    $items = @(
        @{ ft = (New-Text 'Sela' $F_HEAD 132 $C_TEXT); gap = 0 },
        @{ rule = 34; gap = 26 },
        @{ ft = (New-Text $TAG_EN $F_BODY 34 $C_TEXT); gap = 26 },
        @{ ft = (New-Text $FEATURES $F_BODY 23 $C_SECOND); gap = 18 },
        @{ ft = (New-Text $URL $F_BODY_SB 23 $C_ACCENT); gap = 30 }
    )
    $left = ($w - (Horizontal-Width $S $gap $items)) / 2
    Draw-Sky $dc $w $h ($left - $S * 8 / 256 + $S / 2) ($h / 2) 250 9
    Draw-Horizontal $dc $left ($h / 2) $S $gap $items
}

# ---------------------------------------------------------------- Instagram feed post (1080x1350) and story (1080x1920)
function Draw-Vertical($dc, [double]$w, [double]$h, [double]$logoCy, [double]$S, [double]$textTop, $items, $urlFt, [double]$urlBottom, [double]$glowR) {
    Draw-Sky $dc $w $h ($w / 2) $logoCy $glowR 12
    Draw-Logo $dc ($w / 2) $logoCy $S
    $m = Measure-Stack $items
    Draw-Stack $dc $items (($w - $m.W) / 2) $textTop 'center' $m.W
    $r = Ink $urlFt
    $ux = ($w - $r.Width) / 2 - $r.X
    $dc.DrawText($urlFt, (Pt $ux ($urlBottom - $r.Bottom)))
    Note 'text' 'url' (Rect ($ux + $r.X) ($urlBottom - $r.Height) $r.Width $r.Height)
}
foreach ($lang in 'en', 'id') {
    $tag = if ($lang -eq 'en') { $TAG_EN } else { $TAG_ID }
    Scene "social\instagram-post-$lang.png" 1080 1350 $true { param($dc, $w, $h)
        $items = @(
            @{ ft = (New-Text 'Sela' $F_HEAD 156 $C_TEXT); gap = 0 },
            @{ rule = 44; gap = 34 },
            @{ ft = (New-Text $tag $F_BODY 50 $C_TEXT 760); gap = 40 }
        )
        Draw-Vertical $dc $w $h 420 460 730 $items (New-Text $URL $F_BODY_SB 30 $C_ACCENT) 1250 380
    }
    Scene "social\instagram-story-$lang.png" 1080 1920 $true { param($dc, $w, $h)
        $items = @(
            @{ ft = (New-Text 'Sela' $F_HEAD 176 $C_TEXT); gap = 0 },
            @{ rule = 50; gap = 40 },
            @{ ft = (New-Text $tag $F_BODY 56 $C_TEXT 800); gap = 46 }
        )
        Draw-Vertical $dc $w $h 640 520 1000 $items (New-Text $URL $F_BODY_SB 34 $C_ACCENT) 1420 430
    }
}

# ---------------------------------------------------------------- profile avatar (1080x1080, survives a round crop)
# Rounded-tile corner reaches 146.5/256 of S from the centre; S=760 -> 435 px, well inside the 540 px circle.
Scene 'social\profile-avatar.png' 1080 1080 $true { param($dc, $w, $h)
    Draw-Sky $dc $w $h ($w / 2) ($h / 2) 470 0
    $S = 760.0
    $dc.DrawImage($logo, (Rect (($w - $S) / 2) (($h - $S) / 2) $S $S))
    Note 'logo' 'tile' (Rect (($w - $S) / 2 + $S * 8 / 256) (($h - $S) / 2 + $S * 8 / 256) ($S * 240 / 256) ($S * 240 / 256))
}

# ---------------------------------------------------------------- X header (1500x500) and LinkedIn banner (1584x396)
# Both platforms overlay the profile photo at the bottom-left: everything starts right of x=440.
Scene 'social\x-header.png' 1500 500 $true { param($dc, $w, $h)
    $S = 240.0; $left = 460.0
    $items = @(
        @{ ft = (New-Text 'Sela' $F_HEAD 104 $C_TEXT); gap = 0 },
        @{ rule = 28; gap = 22 },
        @{ ft = (New-Text $TAG_EN $F_BODY 30 $C_TEXT); gap = 22 }
    )
    Draw-Sky $dc $w $h ($left - $S * 8 / 256 + $S / 2) ($h / 2) 230 10
    Draw-Horizontal $dc $left ($h / 2) $S 52 $items
}
Scene 'social\linkedin-banner.png' 1584 396 $true { param($dc, $w, $h)
    $S = 200.0; $left = 480.0
    $items = @(
        @{ ft = (New-Text 'Sela' $F_HEAD 88 $C_TEXT); gap = 0 },
        @{ rule = 24; gap = 18 },
        @{ ft = (New-Text $TAG_EN $F_BODY 27 $C_TEXT); gap = 18 }
    )
    Draw-Sky $dc $w $h ($left - $S * 8 / 256 + $S / 2) ($h / 2) 200 10
    Draw-Horizontal $dc $left ($h / 2) $S 46 $items
}

if ($LayoutReport) {
    $script:layout | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $LayoutReport -Encoding UTF8
    Write-Host "Layout report: $LayoutReport"
}
