#Requires -Version 5.1
<#
.SYNOPSIS
    Arma el instalador de PBI Layer (PBILayer-Setup-<versión>.exe) y su archivo de hash.

.DESCRIPTION
    1. Corre los tests (salvo -SkipTests).
    2. Publica la app self-contained para win-x64 (salvo -SkipPublish).
    3. Genera el icono de la herramienta externa y compila el instalador con Inno Setup.
    4. Calcula el SHA-256 y lo deja en installer_output\<instalador>.sha256: es el archivo que la app
       usa para verificar la descarga cuando se actualiza sola.

    La versión sale de <Version> en PBIExplorer.csproj: es el único lugar donde se cambia.
#>
param(
    [switch]$SkipTests,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# ---- Versión ----
$csproj = [xml](Get-Content 'PBIExplorer.csproj')
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "La versión del .csproj ('$version') no tiene el formato X.Y.Z." }
Write-Host "== PBI Layer $version ==" -ForegroundColor Cyan

# ---- Inno Setup ----
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'No se encontró Inno Setup 6 (ISCC.exe). Instalalo con: winget install JRSoftware.InnoSetup' }

# ---- Tests ----
if (-not $SkipTests) {
    Write-Host '-- Tests' -ForegroundColor Cyan
    dotnet test PBIExplorer.Tests --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Los tests fallaron: no se arma el instalador.' }
}

# ---- Publicación ----
if (-not $SkipPublish) {
    Write-Host '-- Publicación (self-contained, win-x64)' -ForegroundColor Cyan
    # Carpeta limpia: si no, quedan archivos viejos (idiomas, DLL que ya no se usan) metidos en el instalador.
    if (Test-Path publish) { Remove-Item publish -Recurse -Force }
    dotnet publish PBIExplorer.csproj -c Release -r win-x64 --self-contained true -o publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falló.' }
}
if (-not (Test-Path 'publish\PBILayer.exe')) { throw 'No hay publish\PBILayer.exe: corré sin -SkipPublish.' }

# ---- Icono de la herramienta externa (se ve en la cinta de Power BI Desktop) ----
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'Assets\icon-256.png'))
$small = New-Object Drawing.Bitmap 48, 48
$g = [Drawing.Graphics]::FromImage($small)
$g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
$g.DrawImage($source, 0, 0, 48, 48)
$g.Dispose(); $source.Dispose()
$ms = New-Object IO.MemoryStream
$small.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $small.Dispose()
$iconData = 'image/png;base64,' + [Convert]::ToBase64String($ms.ToArray())
Set-Content 'installer\icon-data.inc' ('#define IconData "' + $iconData + '"') -Encoding ASCII

# ---- Instalador ----
Write-Host '-- Instalador (Inno Setup)' -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" '/Q' 'installer\PBILayer.iss'
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falló.' }

$setup = Join-Path $PSScriptRoot "installer_output\PBILayer-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "No se generó $setup." }

# ---- Hash para verificar la descarga ----
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$setup.sha256" "$hash  $(Split-Path $setup -Leaf)`n" -Encoding ASCII -NoNewline

Write-Host ''
Write-Host "Listo: $setup" -ForegroundColor Green
Write-Host ("  Tamaño : {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
Write-Host "  SHA-256: $hash"
Write-Host ''
Write-Host 'Para publicar: creá una release en GitHub con la etiqueta v' -NoNewline
Write-Host $version -NoNewline -ForegroundColor Yellow
Write-Host ' y subí el .exe y el .sha256 (o empujá la etiqueta y lo hace el workflow release.yml).'
