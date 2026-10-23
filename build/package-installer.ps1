#!/usr/bin/env pwsh
# Gera dist/TudoMKV-<versão>-setup.exe: instalador NSIS (build/installer/TudoMKV.nsi) com o
# TudoMKV.exe autocontido, o FFmpeg e o desinstalador. Instala por usuário, sem administrador.
#
# Uso: build/package-installer.ps1 -Version 0.4.6 [-FfmpegDir <pasta-bin-do-ffmpeg>] [-SkipPublish]
#
# Sem a pasta do FFmpeg, baixa a build GPL "shared" mais recente da BtbN (a mesma do
# package-windows.sh, com libdvdnav e libbluray).
#
# Precisa do NSIS instalado (https://nsis.sourceforge.io), que traz o makensis.exe.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$FfmpegDir = '',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'TudoMKV'
$nsi = Join-Path $root 'build\installer\TudoMKV.nsi'
$setupPath = Join-Path $dist "TudoMKV-$Version-setup.exe"
$ffmpegUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'

function Write-Step([string]$Text) {
    Write-Host "==> $Text"
}

# O makensis costuma ficar fora do PATH, então procura nos lugares de sempre antes de desistir.
function Find-Makensis {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'),
        (Join-Path $env:ProgramFiles 'NSIS\makensis.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\NSIS\makensis.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command 'makensis.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw 'Não encontrei o makensis.exe. Instale o NSIS (https://nsis.sourceforge.io).'
}

# O VIProductVersion do NSIS exige as quatro partes do número da versão (0.5.11 vira 0.5.11.0).
function ConvertTo-ViVersion([string]$Text) {
    $parts = @($Text -split '[^0-9]+' | Where-Object { $_ -ne '' })
    while ($parts.Count -lt 4) {
        $parts += '0'
    }

    return ($parts[0..3] -join '.')
}

# 1) Programa (mesma publicação do executável: autocontido, arquivo único, comprimido).
if (-not $SkipPublish) {
    Write-Step "Publicando o TudoMKV $Version"
    & dotnet publish (Join-Path $root 'src\TudoMKV.App\TudoMKV.App.csproj') `
        -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        "-p:Version=$Version" `
        "-p:InformationalVersion=$Version" `
        -o $stage
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish falhou com o código $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $stage 'TudoMKV.exe'))) {
    throw "Não encontrei o TudoMKV.exe em $stage. Rode sem -SkipPublish ou publique antes."
}

# Símbolos de depuração das bibliotecas nativas (Skia, HarfBuzz) não servem ao usuário,
# como no package-windows.sh.
Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File | Remove-Item -Force

# 2) FFmpeg (ffmpeg.exe, ffprobe.exe e as DLLs que eles carregam).
$ffmpegTarget = Join-Path $stage 'runtimes\ffmpeg'
New-Item -ItemType Directory -Path $ffmpegTarget -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($FfmpegDir)) {
    Write-Step 'Baixando o FFmpeg'
    $download = Join-Path $dist 'ffmpeg-download'
    $zip = Join-Path $download 'ffmpeg.zip'
    New-Item -ItemType Directory -Path $download -Force | Out-Null
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $download -Force
    $FfmpegDir = (Get-ChildItem -LiteralPath $download -Recurse -Filter 'ffmpeg.exe' |
            Select-Object -First 1).DirectoryName
}

if ([string]::IsNullOrWhiteSpace($FfmpegDir)) {
    throw 'Não encontrei o ffmpeg.exe para empacotar.'
}

foreach ($file in Get-ChildItem -LiteralPath $FfmpegDir -File) {
    if ($file.Name -in 'ffmpeg.exe', 'ffprobe.exe' -or $file.Extension -eq '.dll') {
        Copy-Item -LiteralPath $file.FullName -Destination $ffmpegTarget -Force
    }
}

# A licença do FFmpeg fica um nível acima do bin.
$ffmpegLicense = Join-Path (Split-Path -Parent $FfmpegDir) 'LICENSE.txt'
if (Test-Path -LiteralPath $ffmpegLicense) {
    Copy-Item -LiteralPath $ffmpegLicense -Destination (Join-Path $ffmpegTarget 'LICENSE-ffmpeg.txt') -Force
}

Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'build\LEIA-ME.txt') -Destination (Join-Path $stage 'LEIA-ME.txt') -Force

# 3) Empacotar: o NSIS copia a estrutura de dist\TudoMKV como ela está (o FFmpeg já vai
# para runtimes\ffmpeg) e comprime em LZMA.
$makensis = Find-Makensis
$viVersion = ConvertTo-ViVersion $Version

Write-Step "Gerando o instalador com o NSIS ($makensis)"
if (Test-Path -LiteralPath $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}

& $makensis /V2 "/DVERSION=$Version" "/DVI_VERSION=$viVersion" "/DSOURCE=$stage" "/DOUTFILE=$setupPath" $nsi
if ($LASTEXITCODE -ne 0) {
    throw "O makensis falhou com o código $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "O makensis não gerou o instalador em $setupPath."
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 1)
Write-Step "Pronto: $setupPath ($sizeMb MB)"
