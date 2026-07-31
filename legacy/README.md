# legacy - the PowerShell bench

These files were the test bench until this program replaced them on 2026-07-31. They
are here because they are the **reference for the parity proof**: the expected values
in `tests/Testbench.Core.Tests/LogAnalyzerParityTests.cs` come from these counting
lines, measured on the same logs.

| File | What it was |
|---|---|
| `Invoke-SmokeTest.ps1` | stage 1, one headless run. The template for `TestRunner` and `LogAnalyzer` |
| `Invoke-TestMatrix.ps1` | the loop over all versions plus the markdown report |
| `Start-Gui.ps1` | stage 2, a run with a window and a visual check |
| `Testbench.psd1` | machine and mod configuration in one file |
| `README-powershell.md` | their documentation, the source of many paragraphs in `docs/conventions/traps.md` |

They are **not** maintained and should not be used any more. Two silent bugs are still
in there and are deliberately not fixed, so that the parity proof runs against the
real old state:

- `Invoke-TestMatrix.ps1` reads `$g.AtlasOk` while `Start-Gui.ps1` writes
  `EvidenceOk`. That is why a `TESTED_VERSIONS` line was never proposed although
  confirmed GUI runs existed.
- The comparison only went by the game version, not by mod, variant and mod version.
  A confirmation for one mod would have turned another one green.

Both are trap 15 in [`../docs/conventions/traps.md`](../docs/conventions/traps.md).

`README-powershell.md` is kept in German, verbatim, because it is an archive of what
the retired scripts documented at the time. Everything it is still worth reading for
has been carried over into `docs/conventions/traps.md` in English.
