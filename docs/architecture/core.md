# Architektur

## Ein Kern, zwei Oberflächen

```
Testbench.Core                 die ganze Logik, kennt keine Oberfläche
   ├── tb.exe                  CLI, --json, definierte Exit-Codes
   └── Testbench.Gui           WPF-Fenster
```

Beide Oberflächen referenzieren `Testbench.Core` direkt und schreiben in denselben
Run-Store auf Platte. Es gibt keinen Daemon und keinen Server. Das ist möglich,
weil ohnehin nur ein Lauf gleichzeitig stattfinden darf (siehe `RunLock`), und
Ausschluss über einen Named Mutex billiger und robuster ist als ein Prozess, der
laufen muss.

Der Grund für die Trennung: in den PowerShell-Skripten stand die Reihenfolge der
Schritte zweimal, in `Invoke-SmokeTest.ps1` und in `Start-Gui.ps1`, und die beiden
waren schon auseinandergelaufen. Jetzt kennt nur `TestRunner` diese Reihenfolge.

## Die Bausteine

| Typ | Zuständig für |
|---|---|
| `Config/MachineConfig` | diese Maschine: Pfade, Versionsregister, Dependency-Bibliothek, Muster |
| `Config/ModConfig` | einen Mod: Varianten, Dependency-Referenzen, Stage1/Stage2, Profile |
| `Config/ConfigStore` | Laden, Speichern, Validieren, Fundort der `testbench.json` |
| `Config/Psd1Importer` | Einmalmigration der alten `.psd1` |
| `Deploy/ModInfoReader` | `<Name>`, `<DisplayName>`, `<Version>` aus `ModInfo.xml` |
| `Deploy/DirectoryMirror` | Zielordner durch exakte Kopie ersetzen |
| `Deploy/ModDeployer` | Mods-Ordner auf definierten Stand bringen, Dependencies auflösen |
| `Prefs/PrefsGuard` | GamePrefs sichern, zurückspielen, gegen Goldwerte prüfen |
| `Run/GameLauncher` | Spiel starten, auf Marker oder Fensterschluss warten |
| `Run/LogAnalyzer` | **rein**: Log rein, Zähler und Urteil raus |
| `Run/TestRunner` | die Reihenfolge der Schritte, einmal |
| `Run/RunLock` | ein Lauf gleichzeitig, maschinenweit |
| `Store/RunStore` | jeder Lauf als eine JSON-Datei, plus Import von `gui-verified.json` |
| `Report/ReportBuilder` | Matrix, Markdown, `TESTED_VERSIONS`-Zeile |
| `Diagnostics/Doctor` | warum es nicht läuft, vor dem Lauf |

## Reihenfolge eines Laufs

`TestRunner.Run` macht genau das, in dieser Folge:

1. Installation und `7DaysToDie.exe` prüfen, sonst `FEHLT`.
2. Läuft schon eine `7DaysToDie.exe`? Dann `FEHLER`, denn zwei Instanzen teilen
   Steam, Ports und Prefs-Key.
3. Bei Stufe 2 warnen, wenn Steam nicht läuft.
4. UserDataFolder gegen die Live-Daten prüfen (`%APPDATA%\7DaysToDie`).
5. Deployen: Fremdmods nach `_Mods-deaktiviert`, Mod spiegeln, Dependencies
   spiegeln. Immer spiegeln, nicht nur bei fehlendem Ordner, sonst hätte jede
   Installation je nach Vorgeschichte eine andere Gears-Version.
6. GamePrefs sichern. Scheitert das, bricht der Lauf ab.
7. Starten und warten. Headless auf `readyPattern`, `fatalPattern` oder Timeout;
   GUI, bis der Mensch das Fenster schließt.
8. GamePrefs im `finally` zurückspielen, genau einmal, und die Goldwerte prüfen.
9. Log auswerten, Dependencies im Log nachweisen, Urteil bilden.
10. Bei Stufe 2: Nachweismuster prüfen und die Sichtprüfung behandeln.
11. Record speichern.

Der Rückgabewert ist immer ein `RunRecord`, auch wenn etwas schiefgeht. Kein Pfad
durch diese Methode gibt "nichts" zurück.

## Warum der Analyzer rein ist

`LogAnalyzer` hat keinen Zugriff auf Dateisystem, Prozesse oder Config-Schreiben.
Dadurch ist jede Zahl, die ein Lauf berichtet, aus einem gespeicherten Log
reproduzierbar, und genau das macht die Portierung überhaupt überprüfbar:
`tests/Testbench.Core.Tests/LogAnalyzerParityTests.cs` fährt vier echte Logs
gegen die Zähler, die die PowerShell-Logik für dieselben Dateien geliefert hat.

Bewusster Unterschied: die Spielversion wird bis zum ersten Komma behalten
(`V 3.1.0 (b14) Compatibility Version: V 3.1.0`), weil die Buildnummer zwei
Veröffentlichungen derselben Versionsnummer unterscheidet.
`Invoke-SmokeTest.ps1` schnitt nach `V 3.1.0` ab; die Kurzform gibt es weiter als
`GameVersionShort`.

## Urteilsreihenfolge

`LogAnalyzer.Verdict` prüft in dieser Ordnung, und die ist tragend:

```
FATAL > MOD NICHT GELADEN > ABHAENGIGKEIT FEHLT > HARMONY FEHLT
      > EXCEPTIONS > ERRORS > XML-WARNUNGEN > TIMEOUT > OK
```

Ein Lauf, dessen Mod nie geladen wurde, sagt nichts über Fehlerzahlen aus. Eine
fehlende Abhängigkeit macht jeden Test der Integration mit ihr wertlos. Beides
steht deshalb über den Zählern.

## Mensch und Agent

Die einzige Information, die kein Skript erzeugen kann, ist `VisualState`. Ein
Agent startet einen GUI-Lauf mit `--visual defer`, der Lauf bleibt als `Pending`
liegen, und erst `tb verify --run <id> --visual ok` (oder die GUI) schließt ihn
ab. `RunRecord.FullyVerified` verlangt Stufe 2, `Status == Ok`, bestätigte
Sichtprüfung und einen nicht fehlgeschlagenen Lognachweis.

Nur solche Läufe dürfen über `ReportBuilder` in eine `TESTED_VERSIONS`-Zeile
geraten, und die Bestätigung ist an die Mod-Version gebunden: ein Release, das am
DLL- oder Atlas-Code dreht, entwertet jede ältere Sichtprüfung.

## Was hier absichtlich nicht gemacht wird

- **Parallele Läufe.** Nicht möglich, siehe `RunLock`.
- **Installationen selbst herunterladen.** `tb versions add` legt den Ordner an
  und druckt den `DepotDownloader`-Befehl. Passwort und Steam-Guard-Code gibt der
  Mensch selbst ein; das Tool sieht sie nie.
- **Die Live-Modlist anfassen.** Der Bench liest aus
  `C:\Modlists\Smorgasbord\...` nur, um Gears und Quartz zu spiegeln. Geschrieben
  wird ausschließlich in die Testinstallationen unter `gameRoot`.
