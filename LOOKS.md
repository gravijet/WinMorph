# Richtlinie: Ein Look ändert alles

Ein Look ist erst fertig, wenn **nichts mehr nach Windows 11 aussieht**. Wer einen Look neu baut oder umgestaltet, geht diese Liste vollständig durch. Jede Zeile ist entweder im Look-eigenen Design umgesetzt oder steht mit Begründung unter „Grenzen“ unten. „Fast alles“ zählt nicht.

## 1. Konzept zuerst

- Ein Konzept aus der Welt des Looks (Zen: *Morgenteich*, ein Koi-Teich von oben; Windows 7: das Original, so echt wie möglich; Hacker: Terminal). Daraus folgen Palette, Schrift, Formen, Bewegung und Symbole. Kein Element wird „neutral“ gelassen.
- **Benannte Palette** in `Shell/ShellThemes.cs` (Zen zusätzlich `Shell/ZenStyle.cs`), kräftig und mehrfarbig, Kontrast gemessen. Statusfarben bleiben bedeutungsgleich (Fehler rot, Erfolg grün).
- **Eigene Schrift** pro Look (Zen: Yu Gothic UI, Hacker: Consolas, Windows 7: Segoe UI). Segoe UI Variable und Windows-11-Symbolschrift nur dort, wo der Look sie wirklich so hatte.
- **Bewegung** mit einer Persönlichkeit pro Look (Zen: Wasser, Flächen tauchen mit einer Welle auf und setzen sich ohne Federn; Windows 7: kurze Überblendungen wie das Original; Hacker: hart, getippt). Jede Fläche geht animiert auf und zu.

## 2. Jede Oberfläche (Checkliste)

| Bereich | Datei | Muss |
|---|---|---|
| Hintergrund | `Core/WallpaperArt.cs`, `Core/PondArt.cs` | im Code gezeichnet, keine fremden Bilder |
| Live-Hintergrund | `Live/LookScenes.cs`, `Live/WallCatalog.cs` | zeigt dasselbe Motiv wie das Standbild, sparsam (nur geänderte Bereiche neu zeichnen) |
| Taskleiste | `Shell/ShellHost.cs` | eigene Form, eigene Startschaltfläche, eigene Laufend-Markierung |
| Tray-Symbole | `Shell/TrayGlyph.cs` | WLAN, Ton, Akku selbst gezeichnet, keine Windows-11-Glyphen |
| WLAN, Ton, Akku, Uhr/Kalender | `Shell/Flyouts.cs` (Hacker: `HackerPanels.cs`) | eigene Panels, keine Windows-Schnelleinstellungen |
| Mitteilungen | `Shell/Notify.cs` | eigene Banner und eigene Liste; Windows-Banner nur während der Look läuft aus |
| Lautstärketasten | `Shell/ShellHost.cs` (`VolumeKey`), `VolumeOsd` | eigene Anzeige statt Windows-Popup |
| Startmenü | `Shell/StartMenu*.cs` | eigenes Layout, nicht das Windows-11-Raster |
| Win+X / Rechtsklick / Tooltips | `QuickMenu.cs`, `MiniMenu.cs`, `ShellHost.ThemeToolTips` | im Look |
| Alt+Tab | `Shell/Switcher.cs` | eigene Darstellung mit Live-Vorschau |
| Vorschau beim Darüberfahren | `Shell/HoverPreview.cs` | im Look |
| Fensterrahmen | `ShellTheme.Frame` | Titelleisten in den Farben des Looks |
| Explorer, Task-Manager, Ausführen | `SevenApps.cs`, `ZenApps.cs`, `HackerApps.cs` | eigene Apps, auch beim normalen Öffnen (Startmenü, Desktop, Strg+Alt+Entf) über `OwnAppRedirect` |
| Mauszeiger | `Shell/CursorSkin.cs` | eigene Zeiger, nur für die Sitzung |
| Sperrbildschirm | `Shell/LockScreen.cs`, `CustomLock.cs` | siehe Abschnitt 3 |
| Bildlaufleisten, Titelzeile eigener Apps | `ZenStyle.ScrollBars`, `ZenWindow.Body` | keine Windows-Pfeilleisten, keine Windows-Knöpfe in eigenen Apps |
| App-Karte | `MainWindow.xaml.cs` (`Passes`, `Inks`, `Field`) | was der Look ändert, in einem Satz pro Bereich, Farben des Looks |

## 3. Sperrbildschirm: echt, ohne Hinweise

- **Keine Tastenhinweise** auf dem Bildschirm („Enter“, „F1: Windows Hello“, „F2“, „Taste drücken“). Was es gibt, ist ein sichtbares, klickbares Element: Pfeil zum Entsperren, Windows Hello nur wenn eingerichtet, „Benutzer wechseln“, Ein/Aus.
- Ein Nachbau (Windows 7) zeigt genau, was das Original zeigte: Kontobild, Name, „Gesperrt“, Kennwortfeld mit Pfeil, die echte Fehlerseite mit OK, die Edition unten. Nichts dazuerfinden.
- Meldungen sind kurze Sätze ohne Tasten („Das Kennwort ist falsch.“).

## 4. Nur was informiert

- Kein Text, der nichts sagt: keine Sprüche, keine Taglines, keine Metaphern-Begriffe („gedruckt“, „Auftrag“, „Druckgang“), keine Zähler ohne Zweck, keine Hinweise auf Tastenkürzel im Fließtext (dafür gibt es Tooltips).
- Jedes Element informiert, führt eine Aktion aus oder gibt Rückmeldung. Leere Zustände: ein Satz und die passende Aktion.

## 5. Stabil

- Leisten werden nur neu aufgebaut, wenn sich wirklich etwas geändert hat (Signatur-Vergleich), Tooltips lesen den aktuellen Titel.
- Vollbild wird selbst geprüft (`BarWindow.CheckFullscreen`), nie blind `ABN_FULLSCREENAPP` folgen.
- Alles, was zur Laufzeit an Windows geändert wird (Taskleiste, Banner, Zeiger, Win+L), wird vor der Änderung gemerkt und von jedem WinMorph-Prozess zurückgegeben (`ShellControl.GiveBackTaskbar`).

## 6. Prüfen, bevor es fertig ist

```
dotnet build WinMorph.csproj -c Release -o build
build\WinMorph.exe --selftest %TEMP%\st.txt
build\WinMorph.exe --shell-preview <look> bar.png [menu]
build\WinMorph.exe --lock-test <look> lock
build\WinMorph.exe --ui-test <look> <net|vol|bat|clock|zen> panel.png
build\WinMorph.exe --own-app <look> <files|tasks|run>
build\WinMorph.exe --switcher-demo <look>
build\WinMorph.exe --live-preview <scene> live.png
build\WinMorph.exe --screenshot app.png <look> de
```

Jedes Bild ansehen. Danach den Look einmal echt anwenden (`--apply <look>`), Startmenü, Panels, Alt+Tab, Explorer über das Startmenü und Win+L ausprobieren, und mit `--restore` prüfen, dass alles zurückkommt.

## Grenzen (bewusst Windows)

- Windows-Einstellungen-App (`ms-settings:`), Systemsteuerung und Verwaltungswerkzeuge: Windows' eigene Fenster. Die Panels der Looks decken WLAN, Ton, Akku/Energie, Helligkeit, Bluetooth und Mitteilungen ab; tiefe Einstellungen öffnen Windows.
- Tray-Symbole fremder Programme (OneDrive, Discord …): nur über Windows erreichbar.
- Mitteilungen von Anruf- und Wecker-Apps behalten Windows' Banner, weil nur dieser Annehmen/Schlummern kann.
- Ein Task-Manager, der mit Administratorrechten läuft, lässt sich von einem normalen Prozess nicht ersetzen.
- Titelleisten-Knöpfe fremder Programme zeichnet Windows; der Look färbt nur die Leiste.
