# WinMorph

<img src="website/public/favicon.svg" alt="WinMorph" width="80" height="80">

**Windows-Looks per Klick.** · Download: **[winmorph.benjaminberger.at](https://winmorph.benjaminberger.at)**

WinMorph legt ein komplett eigenes Design über Windows 10/11: Taskleiste, Startmenü, Alt+Tab, WLAN-, Ton-, Akku- und Kalender-Panels, Mitteilungen, Lautstärkeanzeige, Fensterrahmen, Mauszeiger, Desktop, Sperrbildschirm und eigene Apps für Explorer, Task-Manager und Ausführen. **Windows-Einstellungen bleiben unverändert** (kein Dunkelmodus, keine Akzentfarbe, keine Schriften, Klänge oder Taskleisten-Optionen; Rahmen und Mauszeiger gelten nur, solange der Look läuft, und landen nie in der Registry). Ein Look wird immer komplett angewendet, und alles lässt sich vollständig und geprüft zurücksetzen.

## Vier Looks

| Look | Taskleiste & Start | Alt+Tab | Sperrbildschirm |
|---|---|---|---|
| **Windows 7** | Glasleiste mit Startkugel, zweispaltiges Startmenü mit Kontobild und Suche | Aero-Glas mit Live-Vorschau | Windows-7-Anmeldung mit Kontobild, „Benutzer wechseln“, Ein/Aus-Knopf, echter Fehlerseite |
| **Zen** („Morgenteich“) | Milchglas-Leiste über einem Koi-Teich, Lotus als Start; Startmenü aus Glas mit angehefteten Apps als Kieseln und allen Apps A–Z | Teichspiegel: das gewählte Fenster live im Glasrahmen, jedes Fenster ein Kiesel mit Wasserring | Uhr und Glasfeld über dem Teich, jeder Tastendruck ein Wasserring |
| **macOS** (Tahoe, hell) | Menüleiste oben mit Apple-Menü, Programmname und Programm-Menüs (Ablage, Bearbeiten, Darstellung, Fenster, Hilfe), Batterie, WLAN, Spotlight, Kontrollzentrum, Datum und Uhrzeit; Dock aus Glas mit Finder, Launchpad, deinen Apps, Systemeinstellungen, Downloads und Papierkorb, Lauf-Punkten, Namensschildern und Hüpfen beim Start; Windows-Taste = Launchpad | App-Umschalter wie ⌘-Tab: ein Symbol pro App auf hellem Glas | Datum und große Uhrzeit, Kontobild, Name und Passwortfeld, Schütteln bei falschem Passwort, „Benutzer wechseln“ |
| **Hacker** | Statuszeile mit Netz, CPU, RAM, Lautstärke, Akku, Uhr; Windows-Taste = echtes Terminal | Prozess-Tabelle mit PID, RAM und Live-Vorschau | Terminal-Sperre mit Code-Regen |

Eigene Apps: Jeder Look bringt Explorer, Task-Manager und Ausführen in seinem Stil mit: **Windows 7** mit dem originalen Aero-Steuerelement-Design von WPF, **Zen** als „Dateien“, „Aktivität“ und „Ausführen“ aus Milchglas, **macOS** als Finder (Seitenleiste, Symbol- und Listenansicht, Papierkorb mit Zurücklegen und Entleeren), Aktivitätsanzeige (CPU und Speicher mit Lastgrafik) und Spotlight (Win+S, Win+Leertaste, Win+R), dazu „Über diesen PC“ und „Sofort beenden“, **Hacker** im Terminal-Stil. Der Explorer des Looks erscheint nicht nur bei Win+E, sondern auch, wenn ein Ordner ganz normal geöffnet wird (Startmenü, Desktop-Symbol, andere Programme): das Windows-Fenster wird beim Erscheinen versteckt und die App des Looks öffnet am selben Ordner. Systemsteuerung, Papierkorb und andere Windows-Orte bleiben Windows'. Der Task-Manager des Looks öffnet über Strg+Umschalt+Esc, das Startmenü, Win+X und die Leiste; ein Windows-Task-Manager (etwa über Strg+Alt+Entf) wird nie geschlossen, denn das tun sonst nur Schadprogramme. Dazu eigene Mauszeiger (Aero, Zen, Mac mit drehendem Wasserball, Hacker-Farbe).

Panels und Mitteilungen: WLAN (mit Verbinden), Ton, Akku mit Energiesparplänen und Helligkeit, Uhr mit Kalender, bei Zen dazu Mitteilungen und ein Ruhe-Panel (Fokus-Timer, Atemübung, Hintergrundklang, Nicht stören, Bluetooth), bei macOS das Kontrollzentrum (WLAN, Bluetooth, Energiemodus, Fokus, Bildschirm sperren, Bildschirmfoto, Helligkeit, Ton), die Menüs von WLAN und Batterie und die Mitteilungszentrale mit Kalender- und Batterie-Widget. WinMorphs eigene Hinweise (Fokus-Timer, WLAN, Terminal-Timer) erscheinen im Design des Looks; Mitteilungen anderer Apps bleiben Windows', WinMorph liest sie nicht und ändert ihre Einstellungen nicht. Die Lautstärketasten zeigen die Anzeige des Looks. Hacker gibt es in jeder Farbe (Grün, Cyan, Bernstein, Rot, Violett, Weiß oder eigene).

Sperrbildschirm: ohne Tastenhinweise; Windows Hello erscheint als Schaltfläche, wenn es eingerichtet ist. Bei Windows 7 und Zen ist das Kennwortfeld sofort da, bei Hacker weckt der erste Tastendruck.

Dazu in jedem Look: Fensterrahmen in den Farben des Looks, Desktop des Looks, Win+X-Schnellmenü, Leisten auf allen Bildschirmen, Live-Vorschau beim Darüberfahren. Ein kurzes Alt+Tab wechselt sofort (wie bei Windows), der Umschalter erscheint erst, wenn Alt gehalten wird.

## Was beim Anwenden passiert

1. Ein vorheriger Look wird abgebaut (ohne dass die Windows-Taskleiste zwischendurch aufblitzt).
2. Desktop-Bild und Live-Desktop des Looks.
3. Sperrbildschirm: eigenes Sperrbild (ohne Windows-Blickpunkt-Werbung) und der WinMorph-Sperrbildschirm als Bildschirmschoner-Bild. Wann und ob der Bildschirmschoner startet, bleibt deine Einstellung.
4. Design-Shell: blendet die Windows-Taskleiste aus und zeichnet Leiste, Startmenü, Alt+Tab und Rahmen.

Explorer wird dabei nicht neu gestartet.

**Nach einem Neustart** startet der Look sofort bei der Anmeldung: über eine geplante Aufgabe „WinMorph Shell“ (normale Priorität, vor den verzögerten Autostart-Programmen, von Windows bei einem Fehler automatisch neu gestartet, unabhängig vom Autostart-Schalter im Task-Manager). Der normale Autostart-Eintrag bleibt als zweiter Weg; ein zweiter Start beendet sich sofort. Beim Abmelden bleibt die Windows-Taskleiste versteckt, damit sie bei der nächsten Anmeldung nicht aufblitzt; der Shell-Prozess versteckt sie außerdem als Allererstes, noch bevor er zeichnet, und merkt sich seine Startarbeit (Multicore-JIT), sodass der Look rund eine Sekunde nach dem Start steht (`%LOCALAPPDATA%\WinMorph\shell-start.log`). Öffnest du die App, prüft sie Aufgabe und Shell und repariert beides bei Bedarf. Beim ersten Mal fragt WinMorph einmal nach Admin-Rechten, damit Win+L den Sperrbildschirm des Looks zeigen kann (ablehnen geht, dann sperrt Win+L direkt mit Windows).

## Änderungen und Zurücksetzen

Die Seite **„Änderungen“** listet jede einzelne Änderung mit Originalwert und aktuellem Wert. **„Alles zurücksetzen“** stellt alles her und vergleicht danach jeden Wert einzeln mit dem Original; erst wenn alles stimmt, wird das Backup abgelegt. **„Letzten Schritt rückgängig“** bringt den vorherigen Look zurück.

Die Windows-Taskleiste kommt immer zurück, auch wenn der Design-Shell abgestürzt ist, beendet wurde oder der PC neu startete: Der ursprüngliche Auto-Ausblenden-Zustand liegt bis zur Rückgabe in der Registry und wird von jedem WinMorph-Prozess wiederhergestellt. Beim Öffnen der App repariert WinMorph Reste eines Absturzes oder einer älteren Version selbst. Backups älterer Versionen (mit Windows-Einstellungen) werden weiterhin vollständig zurückgesetzt.

Notfall: Strg+Alt+Umschalt+F12 beendet den Design-Shell sofort. Ohne Oberfläche: `WinMorph.exe --restore`.

- Das Backup (`%LOCALAPPDATA%\WinMorph\backup.txt`) speichert jeden Wert **vor** der ersten Änderung, auch wenn du danach mehrere Looks durchprobierst.
- Alles betrifft nur dein Benutzerkonto.

## App

Links die Vorschau des gewählten Looks (beim Wechsel zieht eine Rakel das neue Bild auf), darunter die vier Looks, rechts was der Look ändert und „Anwenden“; beim Anwenden füllt sich jeder Bereich, bekommt ein Häkchen und seine Zeit, und der Knopf selbst wird zum Fortschrittsbalken. Die Seite „Änderungen“ zeigt jede Änderung mit Originalwert. Tasten: `1`–`4` wählen den Look, `Strg+Enter` wendet an. Schriften: Bricolage Grotesque und Instrument Sans, im macOS-Look Inter (alle SIL Open Font License, siehe `Fonts/`); ist Apples SF Pro installiert, nimmt der macOS-Look diese. Apple-Logo, SF-Schriften und Apple-Bilder werden nicht mitgeliefert: Das Apple-Menü zeigt das Apple-Zeichen nur, wenn SF Pro installiert ist.

Wer einen Look baut oder umgestaltet: `LOOKS.md` (jede Oberfläche, Sperrbildschirm ohne Hinweise, kein Fülltext, Prüfbefehle).

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
makensis.exe /DAppVersion=6.2.0 Setup/WinMorph.nsi
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
| `--lock-test <id> <präfix>` | Sperrbildschirm im Ruhezustand, beim Tippen und nach falschem Passwort als Bilder speichern (sperrt nichts) |
| `--own-app <win7\|zen\|macos\|terminal> <tasks\|run\|files> [ordner]` | eigene App des Looks für ein paar Sekunden öffnen (Test) |
| `--ui-test <id> <net\|vol\|bat\|clock\|zen\|cc\|nc> <png>` | Panel des Looks auf dem Bildschirm öffnen und fotografieren (ändert nichts) |
| `--screenshot <png> <id\|changes> <de\|en>` | Bild der App speichern |
| `--selftest <datei>` | Backup/Restore an einem Test-Schlüssel prüfen |
| `--uninstall` | WinMorph deinstallieren (mit Rückfrage) |
| `--wifi-list <datei>` | sichtbare WLAN-Netze in eine Datei schreiben (Test) |

Look-IDs: `win7`, `zen`, `macos`, `terminal` (= Hacker)

Alle Hintergrundbilder werden im Programm selbst gezeichnet. „Windows“ ist eine Marke von Microsoft; WinMorph ist ein unabhängiges Projekt und ahmt nur den Stil nach.
