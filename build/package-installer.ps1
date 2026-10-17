#!/usr/bin/env pwsh
# Gera dist/TudoMKV-<versão>-setup.exe: instalador auto-extraível (IExpress) com o
# TudoMKV.exe autocontido, o FFmpeg e os scripts de instalação/desinstalação.
#
# Uso: build/package-installer.ps1 -Version 0.4.6 [-FfmpegDir <pasta-bin-do-ffmpeg>] [-SkipPublish]
#
# Sem a pasta do FFmpeg, baixa a build GPL "shared" mais recente da BtbN (a mesma do
# package-windows.sh, com libdvdnav e libbluray).
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
$flat = Join-Path $dist 'TudoMKV-installer-flat'
$setupPath = Join-Path $dist "TudoMKV-$Version-setup.exe"
$ffmpegUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'

function Write-Step([string]$Text) {
    Write-Host "==> $Text"
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

# 3) O IExpress descompacta tudo achatado em uma pasta só, então o pacote é montado já
# achatado: a estrutura runtimes\ffmpeg é remontada pelo install.ps1 na hora de instalar.
Write-Step 'Montando a pasta de empacotamento'
if (Test-Path -LiteralPath $flat) {
    Remove-Item -LiteralPath $flat -Recurse -Force
}

New-Item -ItemType Directory -Path $flat -Force | Out-Null

$payload = @()
$payload += Get-ChildItem -LiteralPath $stage -Recurse -File
foreach ($name in 'install.cmd', 'install.ps1', 'uninstall.ps1') {
    $payload += Get-Item -LiteralPath (Join-Path $root "build\installer\$name")
}

foreach ($file in $payload) {
    $destination = Join-Path $flat $file.Name
    if (Test-Path -LiteralPath $destination) {
        throw "Dois arquivos com o mesmo nome no pacote: $($file.Name)"
    }

    Copy-Item -LiteralPath $file.FullName -Destination $destination
}

# 4) Diretiva do IExpress: lista os arquivos e manda rodar o install.cmd no fim.
Write-Step 'Escrevendo a diretiva do IExpress'

$files = Get-ChildItem -LiteralPath $flat -File | Sort-Object Name
$strings = [System.Text.StringBuilder]::new()
$sources = [System.Text.StringBuilder]::new()
for ($i = 0; $i -lt $files.Count; $i++) {
    [void]$strings.AppendLine("FILE$i=`"$($files[$i].Name)`"")
    [void]$sources.AppendLine("%FILE$i%=")
}

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setupPath
FriendlyName=TudoMKV $Version
AppLaunched=cmd.exe /c install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=install.cmd /S
UserQuietInstCmd=install.cmd /S
SourceFiles=SourceFiles
[Strings]
$strings
[SourceFiles]
SourceFiles0=$flat\
[SourceFiles0]
$sources
"@

$sedPath = Join-Path $dist "TudoMKV-$Version-setup.sed"
# O IExpress lê a diretiva linha a linha no formato do Windows, então quebra de linha CRLF
# sempre, independente de como este script foi salvo.
$sed = ($sed -split "\r?\n") -join "`r`n"
Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

# 5) Empacotar.
Write-Step "Gerando o instalador ($($files.Count) arquivos)"
if (Test-Path -LiteralPath $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}

$iexpress = Join-Path $env:SystemRoot 'System32\iexpress.exe'
# O IExpress é um programa gráfico: a espera é explícita e com prazo, para uma janela de
# erro não travar o empacotamento.
$process = Start-Process -FilePath $iexpress -ArgumentList '/N', '/Q', $sedPath -PassThru
if (-not $process.WaitForExit(300000)) {
    $process.Kill()
    throw "O IExpress não terminou em 5 minutos. Veja a diretiva em $sedPath"
}

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "O IExpress não gerou o instalador. Veja a diretiva em $sedPath"
}

Remove-Item -LiteralPath $flat -Recurse -Force
Remove-Item -LiteralPath $sedPath -Force

$sizeMb = [math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 1)
Write-Step "Pronto: $setupPath ($sizeMb MB)"
