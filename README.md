# WinMorph

<img src="website/public/favicon.svg" alt="WinMorph" width="80" height="80">

**Windows-Looks per Klick.** · Download: **[winmorph.benjaminberger.at](https://winmorph.benjaminberger.at)**

WinMorph legt ein komplett eigenes Design über Windows 10/11: Taskleiste, Startmenü, Alt+Tab, Fensterrahmen, Desktop und Sperrbildschirm. **Windows-Einstellungen bleiben unverändert** (kein Dunkelmodus, keine Akzentfarbe, keine Schriften, Mauszeiger, Klänge oder Taskleisten-Optionen). Ein Look wird immer komplett angewendet, und alles lässt sich vollständig und geprüft zurücksetzen.

## Drei Looks

| Look | Taskleiste & Start | Alt+Tab | Sperrbildschirm |
|---|---|---|---|
| **Windows 7** | Glasleiste mit Startkugel, zweispaltiges Startmenü mit Suche | Aero-Glas mit Live-Vorschau, die Glas-Markierung gleitet von Fenster zu Fenster | Aero-Anmeldung mit Lichtbändern |
| **Zen** | schwebende helle Leiste mit Fokus-Timer (25/5), Atemübung, Regen, Suchpalette | Shōji: Papier-Schiebetüren gleiten zu und auf dem gewählten Fenster wieder auf; die Fenster hängen als Ema-Täfelchen an einer Kordel und schwingen | stille Berglandschaft mit Uhr |
| **Hacker** | Statuszeile mit Netz, CPU, RAM, Lautstärke, Akku, Uhr; Windows-Taste = echtes Terminal | Prozess-Tabelle mit PID, RAM und Live-Vorschau | Terminal-Sperre mit Code-Regen |

Hacker gibt es in jeder Farbe (Grün, Cyan, Bernstein, Rot, Violett, Weiß oder eigene). Hacker ersetzt bewusst **nur** Taskleiste, Alt+Tab und Sperrbildschirm: Task-Manager, Explorer, Win+R und die Windows-Benachrichtigungen bleiben original.

Dazu in jedem Look: Fensterrahmen in den Farben des Looks, Desktop des Looks, Win+X-Schnellmenü, Leisten auf allen Bildschirmen, Live-Vorschau beim Darüberfahren. Ein kurzes Alt+Tab wechselt sofort (wie bei Windows), der Umschalter erscheint erst, wenn Alt gehalten wird.

## Was beim Anwenden passiert

1. Ein vorheriger Look wird abgebaut (ohne dass die Windows-Taskleiste zwischendurch aufblitzt).
2. Desktop-Bild und Live-Desktop des Looks.
3. Sperrbildschirm: eigenes Sperrbild (ohne Windows-Blickpunkt-Werbung) und der WinMorph-Sperrbildschirm als Bildschirmschoner-Bild. Wann und ob der Bildschirmschoner startet, bleibt deine Einstellung.
4. Design-Shell: blendet die Windows-Taskleiste aus und zeichnet Leiste, Startmenü, Alt+Tab und Rahmen.

Explorer wird dabei nicht neu gestartet. Beim ersten Mal fragt WinMorph einmal nach Admin-Rechten, damit Win+L den Sperrbildschirm des Looks zeigen kann (ablehnen geht, dann sperrt Win+L direkt mit Windows).

## Änderungen und Zurücksetzen

Die Seite **„Änderungen“** listet jede einzelne Änderung mit Originalwert und aktuellem Wert. **„Alles zurücksetzen“** stellt alles her und vergleicht danach jeden Wert einzeln mit dem Original; erst wenn alles stimmt, wird das Backup abgelegt. **„Letzten Schritt rückgängig“** bringt den vorherigen Look zurück.

Die Windows-Taskleiste kommt immer zurück, auch wenn der Design-Shell abgestürzt ist, beendet wurde oder der PC neu startete: Der ursprüngliche Auto-Ausblenden-Zustand liegt bis zur Rückgabe in der Registry und wird von jedem WinMorph-Prozess wiederhergestellt. Beim Öffnen der App repariert WinMorph Reste eines Absturzes oder einer älteren Version selbst. Backups älterer Versionen (mit Windows-Einstellungen) werden weiterhin vollständig zurückgesetzt.

Notfall: Strg+Alt+Umschalt+F12 beendet den Design-Shell sofort. Ohne Oberfläche: `WinMorph.exe --restore`.

- Das Backup (`%LOCALAPPDATA%\WinMorph\backup.txt`) speichert jeden Wert **vor** der ersten Änderung, auch wenn du danach mehrere Looks durchprobierst.
- Alles betrifft nur dein Benutzerkonto.

## App

Die App ist eine Bühne: Der gewählte Look steht hinter einem Samtvorhang, der sich beim Wechsel schließt und wieder öffnet. Beim Anwenden leuchten die Schritte nacheinander auf; die Änderungen-Seite ist das Cue-Sheet mit jeder Änderung. Tasten: `1`–`3` wählen den Look, `Strg+Enter` wendet an. Schriften: Big Shoulders Display und Hanken Grotesk (SIL Open Font License, siehe `Fonts/`).

## Systemvoraussetzungen

Windows 10 oder 11 (nutzt das in Windows eingebaute .NET Framework 4.8).

**Installieren:** `WinMorph-Setup.exe` starten. Der reguläre NSIS-Installer installiert nach `%LOCALAPPDATA%\Programs\WinMorph` (ohne Administratorrechte), ins Startmenü, auf Wunsch auf den Desktop und unter Einstellungen → Apps. Deinen ersten Look wählst du anschließend selbst in der App. Deutsch und Englisch sind im Installer und in der App verfügbar; die App merkt sich deine Sprachwahl auch für Shell, Live-Hintergründe und Vorschauen.

Ohne Fenster: `WinMorph-Setup.exe /S`. Der alte Aufruf `/S /update` wird für bereits installierte Versionen weiterhin unterstützt.

**Deinstallieren:** Einstellungen → Apps → WinMorph. Vor dem Entfernen stellt WinMorph Windows wieder her. Wenn dabei etwas fehlschlägt, bleiben Anwendung und Backup für einen erneuten Versuch erhalten.

**Virenschutz meldet einen Fund ([Issue #1](https://github.com/gravijet/WinMorph/issues/1)):** Der bisherige selbstentpackende .NET-Installer wurde ersetzt. Ob eine konkrete Erkennung ein Fehlalarm ist, lässt sich daraus allein nicht ableiten. Schutz eingeschaltet lassen, Erkennungsname und Dateiprüfsumme festhalten und die Datei als Entwickler bei [Microsoft zur Prüfung einreichen](https://www.microsoft.com/wdsi/filesubmission). Ein neuer Installer garantiert keine Freigabe durch Defender. Die Dateien sind weiterhin nicht mit einem Herausgeberzertifikat signiert.

## Updates

WinMorph aktualisiert sich selbst. Die installierte Version fragt `https://winmorph.benjaminberger.at/api/release` nach der neuesten Version, lädt das Setup herunter und prüft Größe und SHA-256, bevor sie es installiert (`WinMorph-Setup.exe /S /update`). Deine Looks bleiben dabei, wie sie sind.

- **App offen:** Oben rechts erscheint „Update x installieren“ (WinMorph startet danach neu). Mit „Updates automatisch installieren“ (Seite „Änderungen“) wird ein geladenes Update auch beim Schließen installiert.
- **Nur der Design-Shell läuft:** Er sucht drei Minuten nach der Anmeldung und dann alle sechs Stunden nach Updates und installiert sie, sobald der PC zwei Minuten nicht benutzt wurde (die Leiste ist dann für ein paar Sekunden weg).
- Abschalten: Schalter „Updates automatisch installieren“ auf der Seite „Änderungen“. Eine nicht installierte (portable) Kopie verweist nur auf die Website.

## Release

Die Versionsnummer steht an einer Stelle: `Directory.Build.props`. Jeder Push baut WinMorph auf Windows (`.github/workflows/build.yml`), führt den Selbsttest aus und erzeugt Screenshots aller Looks (Artefakt „screenshots“). Steht auf `main` eine Version ohne Tag, wird daraus automatisch ein GitHub-Release mit `WinMorph-Setup-<version>.exe` und `SHA256SUMS.txt`. Die Website und das Update in der App holen es von dort.

## Website

`website/` ist die Seite für `winmorph.benjaminberger.at`. Sie läuft über nginx auf dem eigenen Server hinter Cloudflare. Ein Timer holt alle fünf Minuten den neuesten Stand von `main` und das neueste Release (`/api/release`, `/download/setup`), siehe `website/README.md`.

## Bauen

```
dotnet build WinMorph.csproj -c Release -o build
mkdir Setup/out
makensis.exe /DAppVersion=6.0.0 Setup/WinMorph.nsi
```
Zum Bauen des Installers wird [NSIS 3](https://nsis.sourceforge.io/) benötigt. Die Versionsnummer für `AppVersion` muss `Directory.Build.props` entsprechen. Der Installer (`Setup/out/WinMorph-Setup.exe`) enthält die zuvor gebaute App und deren Konfigurationsdatei aus `build/`. `Setup/test.ps1` prüft auf einem frischen Windows-Testsystem Neuinstallation, den alten Update-Aufruf und Deinstallation. Er aktiviert keinen Look.

Das skalierbare Original des Logos liegt in `website/public/favicon.svg`. `WinMorph.ico` enthält neun Größen von 16 bis 256 Pixeln und wird von App, Setup und Verknüpfungen verwendet. Die App lädt ihr Logo direkt aus derselben ICO-Datei.

## Befehlszeile

| Befehl | Wirkung |
|---|---|
| `--apply <id>` | Look ohne Oberfläche anwenden (z. B. `--apply zen`) |
| `--restore` | Alles wiederherstellen |
| `--undo` | Letzten Look rückgängig machen |
| `--shell-stop` | Design-Shell beenden (Windows-Taskleiste kommt zurück) |
| `--live-set <id\|off>` | Live-Hintergrund setzen (IDs siehe `wall list` im Terminal) |
| `--live-stop` | Live-Hintergrund beenden |
| `--lockscreen-preview <id> <png>` | Sperrbildschirm als Bild speichern (ändert nichts) |
| `--shell-preview <id> <png> [menu]` | Vorschau des Shells als Bild speichern (ändert nichts) |
| `--live-preview <scene> <png>` | Live-Hintergrund als Bild speichern (z. B. `zen-desk`) |
| `--switcher-demo <id>` | Alt+Tab des Looks mit den echten Fenstern zeigen, wechselt alle 1,5 s (ändert nichts) |
| `--screenshot <png> <id\|changes> <de\|en>` | Bild der App speichern |
| `--selftest <datei>` | Backup/Restore an einem Test-Schlüssel prüfen |
| `--uninstall` | WinMorph deinstallieren (mit Rückfrage) |
| `--wifi-list <datei>` | sichtbare WLAN-Netze in eine Datei schreiben (Test) |

Look-IDs: `win7`, `zen`, `terminal` (= Hacker)

Alle Hintergrundbilder werden im Programm selbst gezeichnet. „Windows“ ist eine Marke von Microsoft; WinMorph ist ein unabhängiges Projekt und ahmt nur den Stil nach.
