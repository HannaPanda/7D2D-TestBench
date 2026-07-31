# AGENTS.md - 7DTD Multiversion-Testbench

Einstieg für KI-Agenten in dieses Repo. Zuerst lesen.

## Was das ist

Ein Werkzeug, das einen 7-Days-to-Die-Mod gegen mehrere Spielversionen testet,
ohne die Live-Modlist (MO2 "Smorgasbord") oder den Spielstand anzufassen. Ein Kern
(`Testbench.Core`), zwei Oberflächen: `tb.exe` für die Kommandozeile und Agenten,
ein WPF-Fenster für den Menschen.

Es ersetzt drei PowerShell-Skripte, die weiterhin unverändert unter
`E:\7DTD-Testbench` liegen (`Invoke-SmokeTest.ps1`, `Invoke-TestMatrix.ps1`,
`Start-Gui.ps1`). Sie sind die Referenz für den Paritätsnachweis und werden erst
entfernt, wenn dieses Tool sie im Alltag ersetzt hat.

## Docs-Map

- [`docs/architecture/core.md`](docs/architecture/core.md) - Bausteine, Reihenfolge
  eines Laufs, Urteilsreihenfolge, was absichtlich nicht gemacht wird.
- [`docs/architecture/config-schema.md`](docs/architecture/config-schema.md) - die
  drei Konfigurationsebenen, jedes Feld, und welche davon Fallen sind.
- [`docs/conventions/traps.md`](docs/conventions/traps.md) - **wichtigste Datei.**
  Fünfzehn Fallen, die alle bezahlt worden sind. Vor jeder Änderung an Deploy,
  Prefs, Launcher oder Analyzer den passenden Absatz lesen.
- [`docs/cli.md`](docs/cli.md) - Verben, Optionen, Exit-Codes, `--json`.

Docs werden mitgepflegt: jede Änderung an Verhalten, Architektur oder
Konfiguration aktualisiert die passende Datei im selben Commit.

## Aufbau

| Pfad | Inhalt |
|---|---|
| `src/Testbench.Core/` | die ganze Logik, kennt keine Oberfläche |
| `src/Testbench.Cli/` | `tb.exe` (AssemblyName ist `tb`) |
| `src/Testbench.Gui/` | WPF-Fenster |
| `tests/Testbench.Core.Tests/` | xunit, inklusive `fixtures/` mit vier echten Logs |

`net10.0-windows` überall: Registry, 7DTD und WPF sind Windows-gebunden.

## Umgebung (dieser Rechner)

- Testinstallationen: `E:\Games\7DTD-<version>` (3.0.0, 3.0.1, 3.1.0)
- Bench-Daten: `E:\7DTD-Testbench` (`testbench.json`, `bin\tb.exe`, `results\`, `state\`)
- Getestete Mods: `C:\Users\sourc\7D2D-Adamant`, `C:\Users\sourc\7D2D-7DashesToDie`
- Fremdmods: `C:\Modlists\Smorgasbord\mods\...` (nur lesend, zum Spiegeln)
- GamePrefs-Sicherungen: `E:\Backup\7DTD-Prefs`
- Für Fragen zur 7DTD-Engine oder -API ist die **`7d2d-modding`-Skill** das
  richtige Werkzeug: sie befragt die echte `Assembly-CSharp.dll`. Nie aus dem
  Gedächtnis antworten.

## Häufige Aufgaben

- **Mod gegen alle Versionen testen** →
  `tb run --mod <fragment> --profile matrix --json`
- **Neue Spielversion aufnehmen** → `tb versions add <version> --branch <branch>`,
  dann die Installation mit dem gedruckten `DepotDownloader`-Befehl holen (das
  macht der Mensch, wegen Passwort und Steam-Guard).
- **Neuen Mod aufnehmen** → `testbench.mod.json` in `<repo>\test\` anlegen, dann
  `tb mods add <pfad>`. Schema in
  [config-schema.md](docs/architecture/config-schema.md).
- **Warum läuft es nicht** → `tb doctor --json`, nicht Logs durchsuchen.
- **Kompatibilitätsliste** → `tb report --mod <fragment> --write`.

## Regeln, die nicht verhandelbar sind

1. **Alles, was das Spiel startet, geht durch `PrefsGuard`.** Die GamePrefs liegen
   in `HKCU`, werden von allen Installationen geteilt und sind durch keinen
   Startparameter umbiegbar. Ohne Sicherung überschreibt ein Lauf die getunten
   Live-Settings, lautlos.
2. **`--visual ok` setzt nur ein Mensch, der hingesehen hat.** Ein Agent nimmt
   `--visual defer`. Headless führt nichts Grafisches und keine Eingabe aus, ein
   Lognachweis kann eine Sichtprüfung nicht ersetzen.
3. **Ein Lauf gleichzeitig.** `RunLock` ist maschinenweit. Nicht umgehen.
4. **Die Live-Installation und die Modlist werden nicht verändert.** Geschrieben
   wird nur in `gameRoot`-Installationen und `userDataRoot`.
5. **Kein Zähler wird "aufgeräumt", ohne die Paritätstests anzusehen.** Die
   Erwartungswerte in `LogAnalyzerParityTests` stammen aus der alten
   PowerShell-Logik auf denselben Dateien. Wer sie ändert, ändert, was als getestet
   gemeldet wird.
6. **Fallenkommentare im Code bleiben stehen.** Sie sind die Absicherung, nicht
   Dekoration.

## Die GUI

`Testbench.Gui.exe`, ein Fenster, ohne MVVM-Paket: `MainViewModel` ist eine
Klasse mit handgeschriebenem `INotifyPropertyChanged`. Läufe laufen auf einem
Threadpool-Thread, Meldungen gehen über `Dispatcher.BeginInvoke` zurück.

Zwei Entscheidungen, die man kennen muss:

- **GUI-Läufe sind immer `VisualMode.Defer`.** Die Frage landet im Kasten unten,
  nicht in einem Modal-Dialog, der aufgeht, während das Spiel noch zugeht. Das ist
  dieselbe Warteschlange, in der die Läufe eines Agenten liegen.
- **Das Log wird live aus der Datei nachgezogen** (`RunOptions.LogPathReady` plus
  ein Tail mit `FileShare.ReadWrite`). Ohne das sieht ein 35-Sekunden-Start aus wie
  ein hängendes Programm.

## Stand

Etappe 1 fertig und belegt: Core, `tb.exe`, 24 Tests grün, Paritätslauf gegen
`Invoke-SmokeTest.ps1` auf 3.0.1 mit identischem Status und identischen Zählern
(OK, ERR 0, EXC 0, XML 0, ignoriert 11, beide Abhängigkeiten im Log nachgewiesen).

Etappe 2 fertig: WPF-Fenster auf demselben Core, gestartet und gerendert. Die
Sichtprüfung des Fensters selbst steht noch aus, was passend ist: ob etwas richtig
aussieht, kann hier genauso wenig ein Skript beantworten.

`7 Dashes to Die` 1.1.0 ist headless-clean auf 3.0.0, 3.0.1 und 3.1.0, hat aber
**keinen GUI-Lauf**. Beide neuen Features sind Menü- und Eingabeverhalten, das
`-nographics` überhaupt nicht ausführt. Der Report weist das korrekt als "kein
GUI-Lauf" aus und schlägt keine `TESTED_VERSIONS`-Zeile vor.
