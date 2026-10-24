<#
  Renders src/Sela/Assets/SelaIcon.xaml with WPF and writes:
    src/Sela/Assets/Sela.ico      (PNG-compressed entries: 16,20,24,32,48,64,128,256)
    src/Sela/Assets/sela-256.png
  WPF needs an STA thread:  powershell -STA -ExecutionPolicy Bypass -File tools/make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$AssetsDir
)
$ErrorActionPreference = 'Stop'
if (-not $AssetsDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $AssetsDir = Join-Path $scriptDir '..\src\Sela\Assets'
}

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    # Re-launch under STA instead of failing.
    & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -AssetsDir $AssetsDir
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$AssetsDir = (Resolve-Path -LiteralPath $AssetsDir).Path
$xamlPath = Join-Path $AssetsDir 'SelaIcon.xaml'

$stream = [IO.File]::OpenRead($xamlPath)
try { $dict = [Windows.Markup.XamlReader]::Load($stream) } finally { $stream.Dispose() }

$large = $dict['SelaLogo']
$small = $dict['SelaLogoSmall']
if (-not $large -or -not $small) { throw "SelaLogo / SelaLogoSmall not found in $xamlPath" }

function Render-Png([Windows.Media.ImageSource]$image, [int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    # Crisper edges at tiny sizes; fant scaling is irrelevant for vectors.
    [Windows.Media.RenderOptions]::SetEdgeMode($visual, [Windows.Media.EdgeMode]::Unspecified)
    $dc = $visual.RenderOpen()
    $dc.DrawImage($image, (New-Object Windows.Rect 0, 0, $size, $size))
    $dc.Close()

    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $img = if ($s -le 24) { $small } else { $large }
    , (Render-Png $img $s)
}

# ICO layout: ICONDIR (6 bytes) + n * ICONDIRENTRY (16 bytes) + PNG payloads.
$icoPath = Join-Path $AssetsDir 'Sela.ico'
$fs = [IO.File]::Create($icoPath)
$w = New-Object IO.BinaryWriter $fs
try {
    $w.Write([UInt16]0)              # reserved
    $w.Write([UInt16]1)              # type: icon
    $w.Write([UInt16]$sizes.Count)   # image count

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $pngs[$i].Length
        $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256
        $w.Write([byte]$dim)         # width
        $w.Write([byte]$dim)         # height
        $w.Write([byte]0)            # palette colors
        $w.Write([byte]0)            # reserved
        $w.Write([UInt16]1)          # color planes
        $w.Write([UInt16]32)         # bits per pixel
        $w.Write([UInt32]$len)       # payload size
        $w.Write([UInt32]$offset)    # payload offset
        $offset += $len
    }
    foreach ($p in $pngs) { $w.Write($p) }
}
finally { $w.Dispose(); $fs.Dispose() }

[IO.File]::WriteAllBytes((Join-Path $AssetsDir 'sela-256.png'), $pngs[$sizes.IndexOf(256)])

Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes, $($sizes.Count) entries)"
Write-Host "Wrote $(Join-Path $AssetsDir 'sela-256.png')"
