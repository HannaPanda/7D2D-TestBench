# legacy - der PowerShell-Bench

Diese vier Dateien waren der Testbench, bis am 2026-07-31 dieses Programm sie
ersetzt hat. Sie liegen hier, weil sie die **Referenz für den Paritätsnachweis**
sind: die Erwartungswerte in `tests/Testbench.Core.Tests/LogAnalyzerParityTests.cs`
stammen aus diesen Zählzeilen, gemessen auf denselben Logs.

| Datei | Was es war |
|---|---|
| `Invoke-SmokeTest.ps1` | Stufe 1, ein Lauf headless. Vorlage für `TestRunner` und `LogAnalyzer` |
| `Invoke-TestMatrix.ps1` | Schleife über alle Versionen plus Markdown-Report |
| `Start-Gui.ps1` | Stufe 2, Lauf mit Fenster und Sichtprüfung |
| `Testbench.psd1` | Maschinen- und Mod-Konfiguration in einer Datei |
| `README-powershell.md` | die Doku dazu, Quelle vieler Absätze in `docs/conventions/traps.md` |

Sie werden **nicht** gepflegt und sollen nicht mehr benutzt werden. Zwei stille
Fehler stecken noch drin und sind absichtlich nicht korrigiert, damit der
Paritätsnachweis gegen den echten alten Stand läuft:

- `Invoke-TestMatrix.ps1` liest `$g.AtlasOk`, während `Start-Gui.ps1` `EvidenceOk`
  schreibt. Deshalb wurde nie eine `TESTED_VERSIONS`-Zeile vorgeschlagen, obwohl
  bestätigte GUI-Läufe vorlagen.
- Der Abgleich lief nur über die Spielversion, nicht über Mod, Variante und
  Mod-Version. Eine Bestätigung für einen Mod hätte einen anderen grün gemacht.

Beides steht als Falle 15 in [`../docs/conventions/traps.md`](../docs/conventions/traps.md).
