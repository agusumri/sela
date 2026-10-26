<#
.SYNOPSIS
  Builds Sela release artifacts into dist\: portable exe, installer, SHA256SUMS.txt.
  -Step runs one stage, so CI can code-sign the exe between Publish and Installer and sign the
  installer before Checksums (see .github/workflows/release.yml). The default runs every stage.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 1.0.1
  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 1.0.1 -Step Installer
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('All', 'Publish', 'Installer', 'Checksums')]
    [string]$Step = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\Sela\Sela.csproj'
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'

function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path -LiteralPath $c)) { return $c } }
    throw 'ISCC.exe (Inno Setup 6) not found. Install: winget install --id JRSoftware.InnoSetup -e --scope user'
}

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No <Version> in $csproj; pass -Version." }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must be MAJOR.MINOR.PATCH." }
Write-Host "Building Sela $Version ($Step)" -ForegroundColor Cyan

$exe = Join-Path $publish 'Sela.exe'
$portable = Join-Path $dist "Sela-$Version-portable.exe"
$setup = Join-Path $dist "Sela-Setup-$Version.exe"
$all = $Step -eq 'All'

# Find Inno Setup before the slow stages, so a missing compiler fails fast.
if ($all -or $Step -eq 'Installer') { $iscc = Find-Iscc }

if ($all -or $Step -eq 'Publish') {
    if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
    New-Item -ItemType Directory -Path $dist | Out-Null

    Write-Host '== Tests' -ForegroundColor Cyan
    Invoke-Native dotnet @('test', (Join-Path $root 'tests\Sela.Core.Tests'), '-c', 'Release')

    Write-Host '== Publish' -ForegroundColor Cyan
    Invoke-Native dotnet @('publish', $csproj, '-p:PublishProfile=win-x64', "-p:Version=$Version", "-p:PublishDir=$publish\")
    if (-not (Test-Path -LiteralPath $exe)) { throw "Publish did not produce $exe" }
    Copy-Item -LiteralPath $exe -Destination $portable -Force
}

if ($all -or $Step -eq 'Installer') {
    # Packs dist\publish\Sela.exe as it is now: in CI that is the signed exe.
    if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe; run -Step Publish first." }
    Write-Host '== Installer' -ForegroundColor Cyan
    Invoke-Native $iscc @('/Q', "/DAppVersion=$Version", (Join-Path $root 'installer\sela.iss'))
    if (-not (Test-Path -LiteralPath $setup)) { throw "Installer not produced: $setup" }
}

if (-not ($all -or $Step -eq 'Checksums')) { return }
foreach ($f in @($portable, $setup)) { if (-not (Test-Path -LiteralPath $f)) { throw "Missing $f" } }

Write-Host '== Checksums' -ForegroundColor Cyan
$lines = foreach ($f in @($portable, $setup)) {
    $hash = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $f)"
}
# sha256sum-compatible format, LF endings, no BOM.
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), (($lines -join "`n") + "`n"))

Get-ChildItem -LiteralPath $dist -File | Format-Table Name, @{ n = 'MB'; e = { '{0:N1}' -f ($_.Length / 1MB) } } -AutoSize
Get-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt')
