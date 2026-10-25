<#
.SYNOPSIS
  Builds the 9:16 Instagram Reels launch videos for Sela (one per language).

.DESCRIPTION
  Each storyboard scene is rendered to an intermediate MP4 (1080x1920, 30 fps),
  then all scenes are joined with soft xfade transitions. App shots come from
  recorded clips when present, otherwise from the docs/images stills with a slow
  1.00 -> 1.04 push-in.

  Output: dist/reels/sela-reels-<lang>.mp4 and sela-reels-<lang>-cover.jpg

.EXAMPLE
  ./tools/make-reels.ps1                 # both languages
  ./tools/make-reels.ps1 -Languages id   # Indonesian only
  ./tools/make-reels.ps1 -ForceStills    # ignore clips (pipeline test)
#>
[CmdletBinding()]
param(
    [string[]]$Languages = @('en', 'id'),
    [string]$ClipsRoot = (Join-Path $env:LOCALAPPDATA 'Temp\sela-reels\clips'),
    [string]$WorkRoot = (Join-Path $env:LOCALAPPDATA 'Temp\sela-reels\work'),
    [switch]$ForceStills
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $RepoRoot 'dist\reels'
$StillsRoot = Join-Path $RepoRoot 'docs\images'

# ---- Look -------------------------------------------------------------------
$W = 1080; $H = 1920; $Fps = 30
$XFade = 0.45          # scene-to-scene transition
$InnerFade = 0.3       # shot-to-shot crossfade inside one scene
$ClipTrim = 1.2        # skip the app's opening frames
$CaptionY = 1290       # headline top (caption band 1250-1500)
$ShotMaxBottom = 1215  # shots end above the caption
$ShotCenterY = 820     # "slightly above middle"
$FontHead = 'C\:/Windows/Fonts/seguisb.ttf'
$FontBody = 'C\:/Windows/Fonts/segoeui.ttf'
$Utf8 = New-Object System.Text.UTF8Encoding($false)

# Per-shot layout: target width and feather (in source px) for left/right and bottom
# edges, so the clip's own backdrop melts into the composed background.
$ShotKinds = @{
    idle     = @{ Width = 960; FeatherX = 50; FeatherB = 36 }
    reminder = @{ Width = 940; FeatherX = 36; FeatherB = 34 }
    panel    = @{ Width = 880; FeatherX = 26; FeatherB = 26 }
}

$Copy = @{
    en = @{
        Tagline = 'A short pause between everything else'
        S2 = 'Prayer times, always in view'
        S3 = 'Your full schedule, one click away'
        S4 = 'Meetings from Outlook & Google'
        S5 = 'The adhan, right on time'
        S6 = 'Dhikr check-ins & a daily hadith'
        S7 = 'A mascot that follows the day'
        End = 'Free & open source for Windows'
    }
    id = @{
        Tagline = 'Jeda sejenak di sela kesibukan'
        S2 = 'Jadwal sholat selalu terlihat'
        S3 = 'Jadwal lengkap, sekali klik'
        S4 = 'Meeting dari Outlook & Google'
        S5 = 'Adzan tepat waktu'
        S6 = 'Pengingat dzikir & hadits harian'
        S7 = 'Maskot yang ikut waktu'
        End = 'Gratis & open source untuk Windows'
    }
}
$Url = 'github.com/agusumri/sela'

# ---- Helpers ------------------------------------------------------------------
function Invoke-FFmpeg([string[]]$FfArgs) {
    & ffmpeg -hide_banner -v error -nostats -y @FfArgs
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed ($LASTEXITCODE): $($FfArgs -join ' ')" }
}

function Format-Num([double]$v) { $v.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }

# Largest font size <= $Size that keeps the line within $MaxWidth px.
function Get-FitSize([string]$Text, [string]$Family, [int]$Size, [int]$MaxWidth = 940) {
    $bmp = New-Object System.Drawing.Bitmap 1, 1
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $font = New-Object System.Drawing.Font($Family, [single]$Size, [System.Drawing.GraphicsUnit]::Pixel)
        $w = $g.MeasureString($Text, $font, 10000, [System.Drawing.StringFormat]::GenericTypographic).Width
        $font.Dispose()
        if ($w -le $MaxWidth) { return $Size }
        return [int][math]::Floor($Size * $MaxWidth / $w)
    } finally { $g.Dispose(); $bmp.Dispose() }
}

# Bottom row of real content in a panel still (anything that differs from the backdrop).
function Get-ContentBottom([string]$Png) {
    $b = [System.Drawing.Bitmap]::FromFile($Png)
    try {
        for ($y = $b.Height - 1; $y -ge 0; $y--) {
            $eb = 22 + 16 * $y / $b.Height
            for ($x = 0; $x -lt $b.Width; $x += 3) {
                $p = $b.GetPixel($x, $y)
                if ([math]::Abs($p.B - $eb) -gt 12 -or [math]::Abs($p.G - $p.B + 5) -gt 12) { return $y }
            }
        }
        return $b.Height - 1
    } finally { $b.Dispose() }
}

# Seconds until the app shot is actually visible (first frame with near-black notch
# pixels), plus a short lead; never earlier than $ClipTrim.
function Get-ClipStart([string]$Path) {
    $sw = 64
    $tmp = Join-Path $WorkRoot ("onset-" + [IO.Path]::GetFileNameWithoutExtension($Path) + "-" + (Split-Path -Leaf (Split-Path -Parent $Path)) + ".gray")
    Invoke-FFmpeg @('-i', $Path, '-vf', "fps=20,scale=${sw}:-2:flags=area,format=gray", '-f', 'rawvideo', $tmp)
    $bytes = [IO.File]::ReadAllBytes($tmp); Remove-Item -LiteralPath $tmp
    $sz = Get-VideoSize $Path
    $sh = [int](2 * [math]::Round($sz.H * $sw / $sz.W / 2))
    $frame = $sw * $sh
    for ($f = 0; ($f + 1) * $frame -le $bytes.Length; $f++) {
        $dark = 0
        for ($i = $f * $frame; $i -lt ($f + 1) * $frame; $i++) { if ($bytes[$i] -lt 8) { $dark++ } }
        if ($dark -gt $frame * 0.05) { return [math]::Max($ClipTrim, $f / 20.0 + 0.25) }
    }
    $ClipTrim
}

function Get-VideoSize([string]$Path) {
    $o = & ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 $Path
    $p = ($o -split ',')
    @{ W = [int]$p[0]; H = [int]$p[1] }
}

function New-Background([string]$Path) {
    # Vertical #0E1116 -> #1B2230 gradient, soft #3DDC97 radial glow, faint static grain.
    $glow = 'exp(-(pow((X-540)/430\,2)+pow((Y-800)/420\,2)))*0.17'
    $n = '(random(0)-0.5)*1.6'
    $r = "14+13*Y/H+(61-(14+13*Y/H))*$glow+$n"
    $gg = "17+17*Y/H+(220-(17+17*Y/H))*$glow+$n"
    $bb = "22+26*Y/H+(151-(22+26*Y/H))*$glow+$n"
    $script = "color=c=black:s=${W}x${H}:d=1,format=gbrp,geq=r='$r':g='$gg':b='$bb'[o]"
    $f = "$Path.txt"; [IO.File]::WriteAllText($f, $script, $Utf8)
    Invoke-FFmpeg @('-/filter_complex', $f, '-map', '[o]', '-frames:v', '1', $Path)
}

function New-Mask([int]$MW, [int]$MH, [double]$Fx, [double]$Fb, [string]$Path) {
    # Hard top edge (the app hangs from the top), smoothstep feather elsewhere.
    $v = "clip(min(min(X\,W-1-X)/$(Format-Num $Fx)\,(H-1-Y)/$(Format-Num $Fb))\,0\,1)"
    $script = "color=c=black:s=${MW}x${MH}:d=1,format=gray,geq=lum='255*$v*$v*(3-2*$v)'[o]"
    $f = "$Path.txt"; [IO.File]::WriteAllText($f, $script, $Utf8)
    Invoke-FFmpeg @('-/filter_complex', $f, '-map', '[o]', '-frames:v', '1', $Path)
}

# drawtext with an ease-out 24 px rise + fade, text read from a UTF-8 file.
function Get-TextFilter([string]$Work, [string]$Id, [string]$Text, [string]$Font, [int]$Size,
                        [string]$Color, [int]$Y, [double]$T0, [double]$Dur = 0.5) {
    $file = "$Id.txt"
    [IO.File]::WriteAllText((Join-Path $Work $file), $Text, $Utf8)
    $p = "clip((t-$(Format-Num $T0))/$(Format-Num $Dur)\,0\,1)"
    "drawtext=fontfile='$Font':textfile='$file':expansion=none:fontsize=${Size}:fontcolor=$Color" +
    ":x=(w-text_w)/2:y='$Y+24*pow(1-$p\,3)':alpha='$p'"
}

# ---- Scene rendering ------------------------------------------------------------
$script:Usage = @{}

function Resolve-Source([string]$Lang, [string]$Name) {
    $clip = Join-Path $ClipsRoot "$Lang\$Name.mp4"
    if (-not $ForceStills -and (Test-Path -LiteralPath $clip)) {
        $script:Usage["$Lang/$Name"] = 'clip'; return @{ Path = $clip; IsClip = $true }
    }
    $still = Join-Path $StillsRoot "$Lang\$Name.png"
    if (-not (Test-Path -LiteralPath $still)) { throw "No clip or still for $Lang/$Name" }
    $script:Usage["$Lang/$Name"] = 'still'
    @{ Path = $still; IsClip = $false }
}

function Render-Scene([string]$Lang, [string]$Work, [int]$Index, [hashtable]$Scene, [double]$Dur, [string]$Bg, [string]$Out) {
    $inputs = @('-loop', '1', '-framerate', "$Fps", '-t', (Format-Num $Dur), '-i', $Bg)
    $chains = New-Object System.Collections.Generic.List[string]
    $chains.Add("[0:v]format=gbrp,trim=duration=$(Format-Num $Dur)[base0]")
    $base = 'base0'; $inIdx = 1
    $shots = @($Scene.Shots)

    for ($i = 0; $i -lt $shots.Count; $i++) {
        $shot = $shots[$i]
        $kind = $ShotKinds[$shot.Kind]
        $src = Resolve-Source $Lang $shot.Name
        $s = [double]$shot.Start
        $e = if ($i -eq $shots.Count - 1) { $Dur } else { [double]$shot.End }
        $isLast = $i -eq $shots.Count - 1
        $len = $e - $s + $(if ($isLast) { 0 } else { $InnerFade })

        # Source geometry; panels are cropped just below their content.
        if ($src.IsClip) { $sz = Get-VideoSize $src.Path } else {
            $img = [System.Drawing.Image]::FromFile($src.Path); $sz = @{ W = $img.Width; H = $img.Height }; $img.Dispose()
        }
        $cropH = $sz.H
        if ($shot.Kind -eq 'panel') {
            $still = Join-Path $StillsRoot "$Lang\$($shot.Name).png"
            $cropH = [math]::Min($sz.H, (Get-ContentBottom $still) + 30)
            $cropH -= $cropH % 2
        }
        $tw = $kind.Width
        $scale = $tw / $sz.W
        $th = [int](2 * [math]::Round($cropH * $scale / 2))
        $cy = [math]::Min($ShotCenterY, $ShotMaxBottom - $th / 2)
        $top = [int][math]::Round($cy - $th / 2)

        $mask = Join-Path $Work "mask-${tw}x${th}-$($shot.Kind).png"
        if (-not (Test-Path -LiteralPath $mask)) {
            New-Mask $tw $th ($kind.FeatherX * $scale) ($kind.FeatherB * $scale) $mask
        }

        $vi = $inIdx; $mi = $inIdx + 1; $inIdx += 2
        if ($src.IsClip) {
            $ss = Get-ClipStart $src.Path
            $script:Usage["$Lang/$($shot.Name)"] = "clip (from $(Format-Num $ss)s)"
            $inputs += @('-ss', (Format-Num $ss), '-t', (Format-Num ($len + 0.5)), '-i', $src.Path)
            $fit = "fps=$Fps,crop=iw:${cropH}:0:0,scale=${tw}:${th}:flags=lanczos"
        } else {
            $inputs += @('-loop', '1', '-framerate', "$Fps", '-t', (Format-Num $len), '-i', $src.Path)
            $frames = [int][math]::Ceiling($len * $Fps)
            $fit = "crop=iw:${cropH}:0:0,scale=$($tw*2):$($th*2):flags=lanczos," +
                   "zoompan=z='1+0.04*on/$frames':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=1:s=${tw}x${th}:fps=$Fps"
        }
        $inputs += @('-loop', '1', '-framerate', "$Fps", '-t', (Format-Num $len), '-i', $mask)

        $fades = @()
        if ($i -gt 0) { $fades += "fade=t=in:st=$(Format-Num $s):d=$(Format-Num $InnerFade):alpha=1" }
        # Same-size successor covers this shot fully, so keep it opaque (no mid-fade dip).
        if (-not $isLast -and $shots[$i + 1].Kind -ne $shot.Kind) { $fades += "fade=t=out:st=$(Format-Num $e):d=$(Format-Num $InnerFade):alpha=1" }
        $fadeStr = if ($fades) { ',' + ($fades -join ',') } else { '' }

        $chains.Add("[${vi}:v]$fit,setsar=1,tpad=stop_mode=clone:stop_duration=$(Format-Num $len)," +
                    "trim=duration=$(Format-Num $len),setpts=PTS-STARTPTS,format=gbrap[v$i]")
        $chains.Add("[${mi}:v]format=gray,trim=duration=$(Format-Num $len),setpts=PTS-STARTPTS[m$i]")
        $chains.Add("[v$i][m$i]alphamerge,setpts=PTS+$(Format-Num $s)/TB$fadeStr[s$i]")
        $chains.Add("[$base][s$i]overlay=x=(W-w)/2:y=${top}:eof_action=pass:format=gbrp[base$($i+1)]")
        $base = "base$($i+1)"
    }

    $texts = @()
    $n = 0
    foreach ($t in $Scene.Texts) {
        $texts += Get-TextFilter $Work "s$Index-t$n" $t.Text $t.Font $t.Size $t.Color $t.Y $t.T0
        $n++
    }
    $tail = if ($texts) { ($texts -join ',') + ',' } else { '' }
    $chains.Add("[$base]${tail}format=yuv420p,trim=duration=$(Format-Num $Dur)[out]")

    $f = Join-Path $Work "scene$Index.filter.txt"
    [IO.File]::WriteAllText($f, ($chains -join ";`n"), $Utf8)
    Invoke-FFmpeg ($inputs + @('-/filter_complex', $f, '-map', '[out]', '-r', "$Fps",
        '-c:v', 'libx264', '-preset', 'medium', '-crf', '10', '-pix_fmt', 'yuv420p', '-an', $Out))
}

function Get-Storyboard([string]$Lang) {
    $c = $Copy[$Lang]
    function Head([string]$Text, [double]$T0 = 0.3) {
        @{ Text = $Text; Font = $FontHead; Size = (Get-FitSize $Text 'Segoe UI Semibold' 66); Color = 'white'; Y = $CaptionY; T0 = $T0 }
    }
    $sky = 2.5 + $XFade
    $step = $sky / 4
    @(
        @{ Dur = 2.5; Shots = @(); Texts = @(
            @{ Text = 'Sela'; Font = $FontHead; Size = 140; Color = 'white'; Y = 760; T0 = 0.15 }
            @{ Text = $c.Tagline; Font = $FontBody; Size = (Get-FitSize $c.Tagline 'Segoe UI' 42); Color = '0x9C9CA3'; Y = 960; T0 = 0.5 }) }
        @{ Dur = 3.5; Shots = @(@{ Name = 'idle-night'; Kind = 'idle'; Start = 0 }); Texts = @(Head $c.S2) }
        @{ Dur = 3.5; Shots = @(@{ Name = 'panel-prayer'; Kind = 'panel'; Start = 0 }); Texts = @(Head $c.S3) }
        @{ Dur = 3.0; Shots = @(@{ Name = 'panel-agenda'; Kind = 'panel'; Start = 0 }); Texts = @(Head $c.S4) }
        @{ Dur = 3.0; Shots = @(@{ Name = 'reminder-adhan'; Kind = 'reminder'; Start = 0 }); Texts = @(Head $c.S5) }
        @{ Dur = 3.5; Shots = @(
            @{ Name = 'reminder-habit'; Kind = 'reminder'; Start = 0; End = 1.6 }
            @{ Name = 'panel-hadith'; Kind = 'panel'; Start = 1.6 }); Texts = @(Head $c.S6) }
        @{ Dur = 2.5; Shots = @(
            @{ Name = 'idle-dawn'; Kind = 'idle'; Start = 0; End = $step }
            @{ Name = 'idle-day'; Kind = 'idle'; Start = $step; End = 2 * $step }
            @{ Name = 'idle-dusk'; Kind = 'idle'; Start = 2 * $step; End = 3 * $step }
            @{ Name = 'idle-night'; Kind = 'idle'; Start = 3 * $step }); Texts = @(Head $c.S7) }
        @{ Dur = 3.0; Shots = @(); Texts = @(
            @{ Text = 'Sela'; Font = $FontHead; Size = 140; Color = 'white'; Y = 700; T0 = 0.15 }
            @{ Text = $c.End; Font = $FontHead; Size = (Get-FitSize $c.End 'Segoe UI Semibold' 58); Color = 'white'; Y = 900; T0 = 0.45 }
            @{ Text = $Url; Font = $FontBody; Size = 44; Color = '0x3DDC97'; Y = 1000; T0 = 0.75 }) }
    )
}

# ---- Main -----------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $OutDir, $WorkRoot | Out-Null
$bg = Join-Path $WorkRoot 'bg.png'
New-Background $bg

foreach ($lang in $Languages) {
    if (-not $Copy.ContainsKey($lang)) { throw "Unknown language '$lang'" }
    $work = Join-Path $WorkRoot $lang
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    Push-Location -LiteralPath $work   # drawtext textfile= paths are relative
    try {
        $board = Get-Storyboard $lang
        $files = @(); $durs = @()
        for ($k = 0; $k -lt $board.Count; $k++) {
            # Every scene but the last carries the overlap eaten by the next xfade.
            $d = $board[$k].Dur + $(if ($k -lt $board.Count - 1) { $XFade } else { 0 })
            $out = Join-Path $work ("scene{0}.mp4" -f ($k + 1))
            Write-Host ("[{0}] scene {1} ({2}s)" -f $lang, ($k + 1), (Format-Num $d))
            Render-Scene $lang $work ($k + 1) $board[$k] $d $bg $out
            $files += $out; $durs += $d
        }

        $inputs = @(); foreach ($f in $files) { $inputs += @('-i', $f) }
        $graph = New-Object System.Collections.Generic.List[string]
        $prev = '0:v'; $offset = 0.0
        for ($k = 1; $k -lt $files.Count; $k++) {
            $offset += $durs[$k - 1] - $XFade
            $graph.Add("[$prev][${k}:v]xfade=transition=fade:duration=$(Format-Num $XFade):offset=$(Format-Num $offset)[x$k]")
            $prev = "x$k"
        }
        $graph.Add("[$prev]format=yuv420p[out]")
        $gf = Join-Path $work 'final.filter.txt'
        [IO.File]::WriteAllText($gf, ($graph -join ";`n"), $Utf8)

        $final = Join-Path $OutDir "sela-reels-$lang.mp4"
        Invoke-FFmpeg ($inputs + @('-/filter_complex', $gf, '-map', '[out]', '-r', "$Fps", '-c:v', 'libx264',
            '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-an', '-movflags', '+faststart', $final))

        # Cover: prayer-panel scene with its caption fully in.
        $cover = Join-Path $OutDir "sela-reels-$lang-cover.jpg"
        Invoke-FFmpeg @('-ss', '2.0', '-i', $files[2], '-frames:v', '1', '-q:v', '2', $cover)

        # Scene start times in the final cut (for verification).
        $starts = @(0.0); $acc = 0.0
        for ($k = 1; $k -lt $durs.Count; $k++) { $acc += $durs[$k - 1] - $XFade; $starts += $acc }
        [IO.File]::WriteAllText((Join-Path $work 'scene-starts.txt'), (($starts | ForEach-Object { Format-Num $_ }) -join "`n"), $Utf8)
        Write-Host "[$lang] wrote $final"
    } finally { Pop-Location }
}

Write-Host "`nSources:"
$script:Usage.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host ("  {0,-22} {1}" -f $_.Name, $_.Value) }
