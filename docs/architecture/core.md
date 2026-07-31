# Architecture

## One core, two front ends

```
Testbench.Core                 all of the logic, knows no user interface
   ├── tb.exe                  CLI, --json, defined exit codes
   └── Testbench.Gui           WPF window
```

Both front ends reference `Testbench.Core` directly and write into the same run
store on disk. There is no daemon and no server. That works because only one run may
happen at a time anyway (see `RunLock`), and mutual exclusion through a named mutex
is cheaper and more robust than a process that has to be running.

The reason for the split: in the PowerShell scripts the order of the steps existed
twice, in `Invoke-SmokeTest.ps1` and in `Start-Gui.ps1`, and the two had already
drifted apart. Now only `TestRunner` knows that order.

## The building blocks

| Type | Responsible for |
|---|---|
| `Config/MachineConfig` | this machine: paths, version register, dependency library, patterns |
| `Config/ModConfig` | one mod: variants, dependency references, stage 1/stage 2, profiles |
| `Config/ConfigStore` | loading, saving, validating, finding `testbench.json` |
| `Config/Psd1Importer` | one-off migration of the old `.psd1` |
| `Config/VersionScanner` | finding installations and reading their real version |
| `Deploy/ModInfoReader` | `<Name>`, `<DisplayName>`, `<Version>` from `ModInfo.xml` |
| `Deploy/DirectoryMirror` | replacing a target folder with an exact copy |
| `Deploy/ModDeployer` | bringing the Mods folder to a defined state, resolving dependencies |
| `Prefs/PrefsGuard` | backing up GamePrefs, restoring them, verifying the restore |
| `Run/GameLauncher` | starting the game, waiting for a marker or for the window to close |
| `Run/LogAnalyzer` | **pure**: log in, counters and verdict out |
| `Run/TestRunner` | the order of the steps, once |
| `Run/RunLock` | one run at a time, machine-wide |
| `Store/RunStore` | every run as one JSON file, plus import of `gui-verified.json` |
| `Report/ReportBuilder` | matrix, markdown, `TESTED_VERSIONS` line |
| `Diagnostics/Doctor` | why it does not run, before the run |
| `Diagnostics/SteamLocator` | finding the played installation in order to refuse it |
| `I18n/Loc` | the language catalogs, see [i18n.md](../i18n.md) |

## The order of a run

`TestRunner.Run` does exactly this, in this sequence:

1. Check the installation and `7DaysToDie.exe`, otherwise `NOT INSTALLED`.
2. Refuse an installation that lies in the Steam library: that is the copy somebody
   plays, and a run would take its modlist apart.
3. Is a `7DaysToDie.exe` already running? Then `SETUP ERROR`, because two instances
   share Steam, ports and the prefs key.
4. On stage 2, warn when Steam is not running.
5. Check the user data folder against the live data (`%APPDATA%\7DaysToDie`).
6. Deploy: foreign mods into `_Mods-disabled`, mirror the mod, mirror the
   dependencies. Always mirror, not only when the folder is missing, otherwise every
   installation would have a different Gears version depending on its history.
7. Back up the GamePrefs. If that fails, the run aborts.
8. Start and wait. Headless for `readyPattern`, `fatalPattern` or timeout; GUI until
   the human closes the window.
9. Restore the GamePrefs in a `finally`, exactly once, then verify the restore by
   exporting the key again and comparing it against the backup.
10. Analyze the log, prove the dependencies in the log, form the verdict.
11. On stage 2: check the evidence patterns and handle the visual check.
12. Save the record.

The return value is always a `RunRecord`, even when something goes wrong. No path
through this method returns "nothing".

## Why the analyzer is pure

`LogAnalyzer` has no access to the file system, to processes or to writing config.
That makes every number a run reports reproducible from a stored log, and that is
what makes the port checkable in the first place:
`tests/Testbench.Core.Tests/LogAnalyzerParityTests.cs` runs four real logs against
the counters the PowerShell logic produced for the same files.

A deliberate difference: the game version is kept up to the first comma
(`V 3.1.0 (b14) Compatibility Version: V 3.1.0`), because the build number
distinguishes two releases of the same version number. `Invoke-SmokeTest.ps1` cut
after `V 3.1.0`; the short form is still available as `GameVersionShort`.

## The order of the verdict

`LogAnalyzer.Verdict` checks in this order, and the order is load-bearing:

```
FATAL > MOD NOT LOADED > DEPENDENCY MISSING > HARMONY MISSING
      > EXCEPTIONS > ERRORS > XML WARNINGS > TIMEOUT > OK
```

A run whose mod never loaded says nothing about error counts. A missing dependency
makes every test of the integration with it worthless. Both therefore rank above the
counters.

## Human and agent

The one piece of information no script can produce is `VisualState`. An agent starts
a GUI run with `--visual defer`, the run stays there as `Pending`, and only
`tb verify --run <id> --visual ok` (or the GUI) closes it. `RunRecord.FullyVerified`
requires stage 2, `Status == Ok`, a confirmed visual check and log evidence that did
not fail.

Only such runs may reach a `TESTED_VERSIONS` line through `ReportBuilder`, and the
confirmation is bound to the mod version: a release that touches DLL or atlas code
invalidates every earlier visual check.

## What is deliberately not done here

- **Parallel runs.** Not possible, see `RunLock`.
- **Downloading installations.** `tb versions add` creates the folder and prints the
  `DepotDownloader` command. Password and Steam Guard code are entered by the human;
  the tool never sees them.
- **Touching the live modlist.** The bench reads from a modlist folder such as
  `C:\Modlists\<your list>\...` only in order to mirror Gears and Quartz. Writing
  happens exclusively into the test installations under `gameRoot`.
