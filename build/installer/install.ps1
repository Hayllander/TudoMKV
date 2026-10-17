# Instala o TudoMKV para o usuário atual, sem precisar de administrador.
#
# Roda a partir da pasta temporária em que o pacote auto-extraível (IExpress) descompactou
# os arquivos. Como o IExpress achata tudo em uma única pasta, a estrutura runtimes\ffmpeg
# é remontada aqui: os arquivos do FFmpeg voltam para a subpasta e o resto fica na raiz.
#
# Parâmetros:
#   -Silent  instala sem mostrar nenhuma janela (usado pela desinstalação silenciosa).
[CmdletBinding()]
param(
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'

$source = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\TudoMKV'
$exe = Join-Path $source 'TudoMKV.exe'
$ffmpegTarget = Join-Path $target 'runtimes\ffmpeg'
$shortcutName = 'TudoMKV.lnk'

# Arquivos do programa em si: ficam soltos na pasta de instalação.
$programFiles = 'TudoMKV.exe', 'LEIA-ME.txt', 'LICENSE.txt'
# O que não é do programa e não é script de instalação é FFmpeg (ffmpeg.exe, ffprobe.exe,
# as DLLs e a licença). Segue a mesma lista que o package-windows.sh copia.
$installerFiles = 'install.cmd', 'install.ps1', 'uninstall.ps1'

function Show-Message([string]$Text, [string]$Title, [string]$Icon = 'Information') {
    if ($Silent) {
        return
    }

    Add-Type -AssemblyName System.Windows.Forms
    $kind = [System.Windows.Forms.MessageBoxButtons]::OK
    if ($Icon -eq 'Error') {
        [System.Windows.Forms.MessageBox]::Show($Text, $Title, $kind, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
    else {
        [System.Windows.Forms.MessageBox]::Show($Text, $Title, $kind, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $exe)) {
    Show-Message "Não encontrei o TudoMKV.exe na pasta de instalação." 'TudoMKV' 'Error'
    exit 1
}

# Gravar por cima de um TudoMKV aberto falharia pela metade.
if (Get-Process -Name 'TudoMKV' -ErrorAction SilentlyContinue) {
    Show-Message "Feche o TudoMKV antes de instalar esta versão." 'TudoMKV' 'Error'
    exit 1
}

$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.0.0'
}

# 1) Programa e FFmpeg
New-Item -ItemType Directory -Path $target -Force | Out-Null
New-Item -ItemType Directory -Path $ffmpegTarget -Force | Out-Null

foreach ($name in $programFiles) {
    $file = Join-Path $source $name
    if (Test-Path -LiteralPath $file) {
        Copy-Item -LiteralPath $file -Destination $target -Force
    }
}

foreach ($file in Get-ChildItem -LiteralPath $source -File) {
    $isProgram = $programFiles -contains $file.Name
    $isInstaller = $installerFiles -contains $file.Name
    if (-not $isProgram -and -not $isInstaller -and $file.Name -ne $shortcutName) {
        Copy-Item -LiteralPath $file.FullName -Destination $ffmpegTarget -Force
    }
}

# 2) Desinstalador, que já vem pronto no pacote e roda de dentro da pasta instalada.
Copy-Item -LiteralPath (Join-Path $source 'uninstall.ps1') -Destination $target -Force

# 3) Atalhos. O do Menu Iniciar sempre; o da Área de Trabalho só na instalação normal.
$programsMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Path $programsMenu -Force | Out-Null

$shell = New-Object -ComObject WScript.Shell
$targetExe = Join-Path $target 'TudoMKV.exe'

function New-Shortcut([string]$Path) {
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $targetExe
    $link.WorkingDirectory = $target
    $link.IconLocation = $targetExe
    $link.Description = 'TudoMKV - remux de DVD, Blu-ray e vídeos para MKV'
    $link.Save()
}

New-Shortcut (Join-Path $programsMenu $shortcutName)
if (-not $Silent) {
    New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) $shortcutName)
}

# 4) Entrada em "Aplicativos e recursos" do Windows (só do usuário atual, sem UAC).
$sizeKb = [int](((Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1KB)
$uninstallScript = Join-Path $target 'uninstall.ps1'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TudoMKV'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'TudoMKV'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Clediomar do Carmo Santos'
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $target
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $targetExe
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $uninstallScript)
Set-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -Silent' -f $uninstallScript)
Set-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $sizeKb -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

# 5) Fim: avisa e pergunta se pode abrir o programa.
if ($Silent) {
    exit 0
}

Add-Type -AssemblyName System.Windows.Forms
$answer = [System.Windows.Forms.MessageBox]::Show(
    "TudoMKV $version instalado em:`n$target`n`nAbrir o TudoMKV agora?",
    'TudoMKV',
    [System.Windows.Forms.MessageBoxButtons]::YesNo,
    [System.Windows.Forms.MessageBoxIcon]::Information)

if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) {
    Start-Process -FilePath $targetExe -WorkingDirectory $target
}
