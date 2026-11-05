$ErrorActionPreference = 'Stop'
function Run([string]$File, [string]$Arguments) {
    Write-Host "Running $File $Arguments"
    $p = Start-Process $File -ArgumentList $Arguments -PassThru
    if (-not $p.WaitForExit(90000)) { $p.Kill(); throw "Timed out: $File $Arguments" }
    if ($p.ExitCode -ne 0) { throw "Exit $($p.ExitCode): $File $Arguments" }
}
$dir = Join-Path $env:LOCALAPPDATA 'Programs\WinMorph'
$setup = Join-Path $PSScriptRoot 'out\WinMorph-Setup.exe'
Run $setup '/S'
if (-not (Test-Path "$dir\WinMorph.exe") -or -not (Test-Path "$dir\Uninstall.exe")) { throw 'Missing installation' }
if (Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'WinMorph Shell' -ErrorAction SilentlyContinue) { throw 'Installer activated a look' }
# Previously released apps use precisely this invocation; it must stay silent and finish.
Set-ItemProperty 'HKCU:\Software\WinMorph' 'Language' 'de'
Run $setup '/S /update'
if ((Get-ItemPropertyValue 'HKCU:\Software\WinMorph' 'Language') -ne 'de') { throw 'Update lost the language preference' }
if ((Get-FileHash "$dir\WinMorph.exe").Hash -ne (Get-FileHash 'build\WinMorph.exe').Hash) { throw 'Installed executable differs from build' }
Set-Content "$dir\keep.txt" 'User file that must survive uninstall'
Run "$dir\Uninstall.exe" '/S'
# NSIS hands uninstall over to a temporary copy so it can remove its own file.
$deadline = (Get-Date).AddSeconds(60)
while (Test-Path "$dir\WinMorph.exe") {
    if ((Get-Date) -gt $deadline) { throw 'Uninstall did not remove WinMorph' }
    Start-Sleep -Milliseconds 250
}
if ((Get-Content "$dir\keep.txt") -ne 'User file that must survive uninstall') { throw 'Uninstall deleted user data' }
Remove-Item "$dir\keep.txt"

