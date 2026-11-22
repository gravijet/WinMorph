# WinMorph – Hinweise für Agenten

WPF-App (.NET Framework 4.8, C#, kein XAML außer `App.xaml`/`MainWindow.xaml`; die Shell-Oberflächen werden im Code gebaut). Vier Looks: `win7`, `zen`, `macos`, `terminal` (Hacker).

## Bauen und prüfen

```
dotnet build WinMorph.csproj -c Release -o build      # lokal ggf. %USERPROFILE%\.dotnet\dotnet.exe
build\WinMorph.exe --selftest %TEMP%\selftest.txt
```
Weitere Prüfbefehle (Vorschaubilder, Panels, eigene Apps, Sperrbildschirm): `LOOKS.md`, Abschnitt 6, und `README.md`, „Befehlszeile“. Die Versionsnummer steht nur in `Directory.Build.props`; eine neue Nummer auf `main` veröffentlicht ein Release.

## Regeln

- **`LOOKS.md` ist verbindlich**: Ein neuer oder umgestalteter Look ersetzt jede Oberfläche der Checkliste. Teilweise Umgestaltung ist kein fertiger Stand.
- Sperrbildschirme ohne Tastenhinweise; Nachbauten (Windows 7) zeigen nur, was das Original zeigte.
- Kein Fülltext in App und Shell (keine Sprüche, Taglines, Metaphern-Begriffe, Tastenkürzel im Fließtext).
- Jede Oberfläche ist einzigartig für ihren Look, farbig mit einer benannten Palette, und bewegt sich in der Persönlichkeit des Looks.
- Windows-Einstellungen werden nicht dauerhaft geändert; Laufzeit-Änderungen (Taskleiste, Mauszeiger, Win+L) werden vorher gemerkt und über `ShellControl.GiveBackTaskbar` aus jedem Prozess zurückgegeben.
- Kein Verhalten, das Virenscanner als Schadsoftware werten: keine fremden Prozesse beenden, keine Mitteilungs-/Sicherheitseinstellungen anderer Apps ändern, keine fremden Mitteilungen lesen, den Bildschirm nicht abfotografieren (Glas zeigt das Hintergrundbild, nie den Bildschirm). Nach Änderungen an der Shell einmal echt anwenden und `Get-MpThreatDetection` prüfen.
- Leisten nur bei echter Änderung neu aufbauen; Vollbild selbst prüfen (`BarWindow.CheckFullscreen`).

## Wo was liegt

- `Shell/ShellHost.cs` Leisten, Tray, Tastatur (Win, Alt+Tab, Lautstärke), `Shell/ShellControl.cs` Prozess und Taskleiste
- `Shell/Flyouts.cs` Panels (WLAN, Ton, Akku, Uhr, Zen-Ruhe) und `VolumeOsd`; `Shell/Notify.cs` eigene Hinweise
- `Shell/ZenStyle.cs` Zen-Palette, Milchglas (`Pane`), Wasser-Animationen, Bildlaufleisten; `Shell/ZenApps.cs` Dateien/Aktivität/Ausführen
- `Shell/OwnAppRedirect.cs` ersetzt Windows-Explorer-Fenster durch den Dateimanager des Looks
- `Core/PondArt.cs` Zen-Teich (Standbild und Live), `Live/LookScenes.cs` Live-Hintergründe
- `Shell/LockScreen.cs` Designs der Sperrbildschirme (macOS: `LockScreen.Mac.cs`), `Shell/CustomLock.cs` Entsperren
- macOS: `Shell/MacStyle.cs` (Farben, Schrift, Vibrancy-Glas, Symbole, Icon-Raster, `MacMenu`, `MacAlert`), `ShellHost.Mac.cs` (Menüleiste, Dock), `MacPanels.cs` (Kontrollzentrum, WLAN, Batterie, Mitteilungszentrale, Lautstärke, Banner, Downloads-Stapel), `MacApps.cs` (Finder, Aktivitätsanzeige, Über diesen PC, Sofort beenden; Menüleisten-Menüs der eigenen Apps), `MacSpotlight.cs` (Spotlight mit Rechner, Windows-Suchindex, Aktionen, Systemeinstellungen), `StartMenu.Mac.cs` (Launchpad), `Core/MacArt.cs` (Hintergrund, auch live)
- Keine Apple-Grafiken, -Logos oder -Schriften einbauen: alles wird im Code gezeichnet; SF Pro nur, wenn der Nutzer sie installiert hat
