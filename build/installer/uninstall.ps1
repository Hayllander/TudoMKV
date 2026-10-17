# Desinstala o TudoMKV. Roda de dentro da própria pasta instalada, então o caminho é
# sempre o lugar onde este arquivo está.
#
# Parâmetros:
#   -Silent  remove sem perguntar nada (usado pelo QuietUninstallString do Windows).
[CmdletBinding()]
param(
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'

$target = $PSScriptRoot
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TudoMKV'

function Confirm([string]$Text) {
    if ($Silent) {
        return $true
    }

    Add-Type -AssemblyName System.Windows.Forms
    $answer = [System.Windows.Forms.MessageBox]::Show(
        $Text, 'TudoMKV',
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question)
    return $answer -eq [System.Windows.Forms.DialogResult]::Yes
}

if (-not (Confirm "Remover o TudoMKV de:`n$target ?")) {
    exit 0
}

if (Get-Process -Name 'TudoMKV' -ErrorAction SilentlyContinue) {
    if (-not $Silent) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            'Feche o TudoMKV antes de desinstalar.', 'TudoMKV',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }

    exit 1
}

# Atalhos: Menu Iniciar e Área de Trabalho.
foreach ($folder in @(
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
        [Environment]::GetFolderPath('Desktop'))) {
    $link = Join-Path $folder 'TudoMKV.lnk'
    if (Test-Path -LiteralPath $link) {
        Remove-Item -LiteralPath $link -Force
    }
}

# Entrada em "Aplicativos e recursos".
if (Test-Path $key) {
    Remove-Item -Path $key -Recurse -Force
}

# A pasta só pode ser apagada depois que este script terminar, então o cmd espera 2 s e
# apaga em seguida.
Start-Process -FilePath 'cmd.exe' -WindowStyle Hidden `
    -ArgumentList "/c timeout /t 2 /nobreak >nul & rd /s /q `"$target`""

if (-not $Silent) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        'TudoMKV removido.', 'TudoMKV',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
