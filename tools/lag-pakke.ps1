# Lager nedlastingspakken for GitHub Releases: appen med innebygd .NET, slik at brukeren ikke trenger .NET eller Node.js.
# Bygger fra en ren eksport av siste commit, saa pakken tilsvarer kildekoden i taggen (GPL) og laasefilene ikke endres.
# Bruk: powershell -NoProfile -ExecutionPolicy Bypass -File tools\lag-pakke.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe feilet med kode $LASTEXITCODE." }
}

if (git status --porcelain) { throw 'Arbeidskopien har endringer som ikke er committet. Pakken skal tilsvare en commit.' }
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$name = "UU-dokumenter-$version-win-x64"
$dist = Join-Path $root 'dist'
$work = Join-Path $dist 'arbeid'
$src = Join-Path $work 'kilde'
$pkg = Join-Path $work 'UU-dokumenter'
$zip = Join-Path $dist "$name.zip"

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $src, $pkg | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Output "[1/4] Eksporterer siste commit..."
Run 'git' @('archive', '--format=zip', '-o', (Join-Path $work 'kilde.zip'), 'HEAD')
Expand-Archive (Join-Path $work 'kilde.zip') $src

Write-Output "[2/4] Bygger brukergrensesnittet..."
Push-Location (Join-Path $src 'web')
Run 'npm.cmd' @('ci', '--no-audit', '--no-fund')
Run 'npm.cmd' @('run', 'build')
Pop-Location

Write-Output "[3/4] Bygger appen med innebygd .NET..."
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:SatelliteResourceLanguages=en', '-p:DebugType=none', '-v', 'q', '--nologo')
Run 'dotnet' (@('publish', (Join-Path $src 'src\UuDok.Web'), '-o', (Join-Path $pkg 'publish')) + $common)
Run 'dotnet' (@('publish', (Join-Path $src 'src\UuDok.WinHelper'), '-o', (Join-Path $pkg 'publish\helper')) + $common)
foreach ($f in 'publish\UuDok.Web.exe', 'publish\coreclr.dll', 'publish\helper\UuDok.WinHelper.exe', 'publish\helper\coreclr.dll', 'publish\wwwroot\index.html') {
    if (-not (Test-Path (Join-Path $pkg $f))) { throw "Mangler $f i pakken." }
}

Write-Output "[4/4] Pakker..."
New-Item -ItemType Directory -Force (Join-Path $pkg 'tools'), (Join-Path $pkg 'assets'), (Join-Path $pkg 'dokumentasjon') | Out-Null
Copy-Item (Join-Path $src 'start.bat'), (Join-Path $src 'stopp.bat'), (Join-Path $src 'LICENSE') $pkg
Copy-Item (Join-Path $src 'tools\pakke\lag-skrivebordsikon.bat') $pkg
Copy-Item (Join-Path $src 'tools\lag-snarvei.ps1') (Join-Path $pkg 'tools')
Copy-Item (Join-Path $src 'assets\uudok.ico') (Join-Path $pkg 'assets')
foreach ($d in 'BRUKERVEILEDNING', 'INSTALLASJON', 'AVHENGIGHETER', 'SIKKERHET-OG-PERSONVERN', 'BEGRENSNINGER') {
    Copy-Item (Join-Path $src "docs\$d.md") (Join-Path $pkg 'dokumentasjon')
}
$lesMeg = [IO.File]::ReadAllText((Join-Path $src 'tools\pakke\LES-MEG.txt')).Replace('{VERSJON}', $version)
[IO.File]::WriteAllText((Join-Path $pkg 'LES-MEG.txt'), $lesMeg, (New-Object Text.UTF8Encoding $true))

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pkg, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $name.zip" -Encoding ascii
Remove-Item $src -Recurse -Force

Write-Output "Ferdig: $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
Write-Output "SHA-256: $hash"
