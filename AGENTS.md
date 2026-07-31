# AGENTS.md - 7DTD Multiversion-Testbench

Einstieg für KI-Agenten in dieses Repo. Zuerst lesen.

## Was das ist

Ein Werkzeug, das einen 7-Days-to-Die-Mod gegen mehrere Spielversionen testet,
ohne die gespielte Installation oder den Spielstand anzufassen. Ein Kern
(`Testbench.Core`), zwei Oberflächen: `tb.exe` für die Kommandozeile und Agenten,
ein WPF-Fenster für den Menschen, dreizehn Sprachen.

Es ersetzt drei PowerShell-Skripte, die unter [`legacy/`](legacy/) liegen. Sie sind
die Referenz für den Paritätsnachweis und werden nicht mehr gepflegt.

## Docs-Map

- [`docs/architecture/core.md`](docs/architecture/core.md) - Bausteine, Reihenfolge
  eines Laufs, Urteilsreihenfolge, was absichtlich nicht gemacht wird.
- [`docs/architecture/config-schema.md`](docs/architecture/config-schema.md) - die
  drei Konfigurationsebenen, jedes Feld, und welche davon Fallen sind.
- [`docs/conventions/traps.md`](docs/conventions/traps.md) - **wichtigste Datei.**
  Sechzehn Fallen, die alle bezahlt worden sind. Vor jeder Änderung an Deploy,
  Prefs, Launcher oder Analyzer den passenden Absatz lesen.
- [`docs/cli.md`](docs/cli.md) - Verben, Optionen, Exit-Codes, `--json`.
- [`docs/i18n.md`](docs/i18n.md) - wie Texte übersetzt werden, und die Regel, dass
  jeder neue Text in **allen** ausgelieferten Katalogen landet. Die Tests
  erzwingen das.
- [`examples/`](examples/) - kommentierte `testbench.json` und
  `testbench.mod.json` mit generischen Pfaden. Beim Schema-Ändern mitpflegen.

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

## Umgebung

Wo auf einem Rechner was liegt, steht in dessen `testbench.json` und wird von
`tb doctor --json` vollständig ausgegeben. Nichts im Code, in den Tests oder in
dieser Doku darf einen Laufwerksbuchstaben, einen Benutzernamen oder einen
bestimmten Mod voraussetzen: das Werkzeug ist zur Veröffentlichung gedacht.

Die Struktur, die `tb init` anlegt:

```
<bench>\               tb.exe, Testbench.Gui.exe, lang\, testbench.json
<bench>\Games\         eine Testinstallation je Version: 7DTD-<version>
<bench>\UserData\      Spielstände und Einstellungen der Läufe
<bench>\results\       Logs und Markdown-Reports
<bench>\state\         Run-Store, Laufsperre, Fensterzustand
```

- Die **gespielte** Installation gehört nicht dazu. `SteamLocator` findet sie, und
  `TestRunner` wie `Doctor` **verweigern** eine Version, die dort liegt: ein Lauf
  würde die Modlist auseinandernehmen.
- Für Fragen zur 7DTD-Engine oder -API ist die **`7d2d-modding`-Skill** das
  richtige Werkzeug: sie befragt die echte `Assembly-CSharp.dll`. Nie aus dem
  Gedächtnis antworten.

## Häufige Aufgaben

- **Mod gegen alle Versionen testen** →
  `tb run --mod <fragment> --profile matrix --json`
- **Neue Spielversion aufnehmen** → wenn die Installation schon da ist:
  `tb versions scan --add` (liest die Version aus `MicrosoftGame.Config`, nicht aus
  dem Ordnernamen, siehe [Falle 16](docs/conventions/traps.md#16-der-ordnername-ist-keine-versionsangabe)).
  Wenn sie noch fehlt: `tb versions add <version> --branch <branch>`, dann die
  Installation mit dem gedruckten `DepotDownloader`-Befehl holen (das macht der
  Mensch, wegen Passwort und Steam-Guard). Im Fenster macht das der Knopf
  *verwalten* neben VERSIONEN.
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

Drei Entscheidungen, die man kennen muss:

- **GUI-Läufe sind immer `VisualMode.Defer`.** Die Frage landet im Kasten unten,
  nicht in einem Modal-Dialog, der aufgeht, während das Spiel noch zugeht. Das ist
  dieselbe Warteschlange, in der die Läufe eines Agenten liegen.
- **Das Log wird live aus der Datei nachgezogen** (`RunOptions.LogPathReady` plus
  ein Tail mit `FileShare.ReadWrite`). Ohne das sieht ein 35-Sekunden-Start aus wie
  ein hängendes Programm.
- **`VersionsWindow` arbeitet auf derselben `MachineConfig`-Instanz** wie das
  Hauptfenster und schreibt sie sofort. Danach ruft das Hauptfenster
  `ReloadVersions()`, was die Häkchen behält und eine gerade neu eingetragene
  Version schon angehakt anbietet.

## Stand

Belegt, nicht behauptet: Paritätslauf gegen `legacy/Invoke-SmokeTest.ps1` auf
derselben Installation mit identischem Status und identischen Zählern, und vier
echte Logs als Fixtures, deren Erwartungswerte aus der alten PowerShell-Logik
gemessen sind. Die Testsuite ist grün und deckt Analyzer, Versionserkennung und
die Vollständigkeit aller Sprachkataloge ab.

Offen und bewusst offen: ob das Fenster gut *aussieht*, kann hier so wenig ein
Test beantworten wie eine Sichtprüfung im Spiel.
