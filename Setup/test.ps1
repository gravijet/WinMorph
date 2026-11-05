$ErrorActionPreference = 'Stop'
function Run([string]$File, [string]$Arguments) {
    Write-Host "Running $File $Arguments"
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
$legacy = Start-Process $setup -ArgumentList '/S /update /LOG=legacy-update.log' -PassThru
if (-not $legacy.WaitForExit(30000)) { $legacy.Kill(); throw 'Legacy handover hung' }
# The bootstrap exits while the standard silent installer completes the update.
$deadline = (Get-Date).AddSeconds(90)
while (Get-Process -Name 'WinMorph-Setup*' -ErrorAction SilentlyContinue) {
    if ((Get-Date) -gt $deadline) { throw 'Legacy update hung' }
    Start-Sleep -Milliseconds 250
}
if (-not (Select-String -Path legacy-update.log -SimpleMatch 'Installation process succeeded.')) { throw 'Legacy update failed' }
Run "$dir\unins000.exe" '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=uninstall.log'
if (Test-Path "$dir\WinMorph.exe") { throw 'Uninstall did not remove WinMorph' }
