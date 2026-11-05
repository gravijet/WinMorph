$ErrorActionPreference = 'Stop'
function Run([string]$File, [string]$Arguments) {
    $p = Start-Process $File -ArgumentList $Arguments -PassThru
    if (-not $p.WaitForExit(90000)) { $p.Kill(); throw "Timed out: $File $Arguments" }
    if ($p.ExitCode -ne 0) { throw "Exit $($p.ExitCode): $File $Arguments" }
}
$dir = Join-Path $env:LOCALAPPDATA 'Programs\WinMorph'
$setup = Join-Path $PSScriptRoot 'out\WinMorph-Setup.exe'
Run $setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=install.log'
if (-not (Test-Path "$dir\WinMorph.exe") -or -not (Test-Path "$dir\unins000.exe")) { throw 'Missing installation' }
if (Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'WinMorph Shell' -ErrorAction SilentlyContinue) { throw 'Installer activated a look' }
# A previously released app still invokes /S /update.
Run $setup '/S /update'
Run "$dir\unins000.exe" '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=uninstall.log'
if (Test-Path "$dir\WinMorph.exe") { throw 'Uninstall did not remove WinMorph' }
