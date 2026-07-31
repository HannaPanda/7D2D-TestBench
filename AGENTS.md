# AGENTS.md - 7DTD Multiversion Testbench

The entry point for AI agents into this repository. Read this first.

## What this is

A tool that tests a 7 Days to Die mod against several game versions without
touching the installation somebody plays or their save games. One core
(`Testbench.Core`), two front ends: `tb.exe` for the command line and for agents, a
WPF window for the human, thirteen languages.

It replaces three PowerShell scripts, which live under [`legacy/`](legacy/). They are
the reference for the parity proof and are not maintained any more.

## Docs map

- [`docs/architecture/core.md`](docs/architecture/core.md) - building blocks, the
  order of a run, the order of the verdict, what is deliberately not done.
- [`docs/architecture/config-schema.md`](docs/architecture/config-schema.md) - the
  three configuration levels, every field, and which of them are traps.
- [`docs/conventions/traps.md`](docs/conventions/traps.md) - **the most important
  file.** Sixteen traps, all of them paid for. Before any change to deploy, prefs,
  launcher or analyzer, read the matching paragraph.
- [`docs/cli.md`](docs/cli.md) - verbs, options, exit codes, `--json`.
- [`docs/i18n.md`](docs/i18n.md) - how texts are translated, and the rule that every
  new text lands in **all** shipped catalogs. The tests enforce it.
- [`examples/`](examples/) - commented `testbench.json` and `testbench.mod.json` with
  generic paths. Keep them current when the schema changes.

Docs are maintained along with the code: any change to behaviour, architecture or
configuration updates the matching file in the same commit.

**The language of this repository is English** - code, comments, docs, commit
messages, issues. The tool itself speaks thirteen languages, but its documentation
has exactly one, so that a mod author anywhere can read it and contribute. German
belongs in `lang\german.json` and nowhere else.

## Layout

| Path | Contents |
|---|---|
| `src/Testbench.Core/` | all of the logic, knows no user interface |
| `src/Testbench.Cli/` | `tb.exe` (the AssemblyName is `tb`) |
| `src/Testbench.Gui/` | WPF window |
| `tests/Testbench.Core.Tests/` | xunit, including `fixtures/` with four real logs |

`net10.0-windows` everywhere: the registry, 7DTD and WPF are all Windows-bound.

## Environment

Where things lie on a machine is written in that machine's `testbench.json` and is
printed in full by `tb doctor --json`. Nothing in the code, in the tests or in these
docs may assume a drive letter, a user name or a particular mod: this tool is meant
to be published.

The structure `tb init` creates:

```
<bench>\               tb.exe, Testbench.Gui.exe, lang\, testbench.json
<bench>\Games\         one test installation per version: 7DTD-<version>
<bench>\UserData\      save games and settings of the runs
<bench>\results\       logs and markdown reports
<bench>\state\         run store, run lock, window state
```

- The **played** installation is not part of this. `SteamLocator` finds it, and both
  `TestRunner` and `Doctor` **refuse** a version that lies there: a run would take
  the modlist apart.
- For questions about the 7DTD engine or API, the **`7d2d-modding` skill** is the
  right tool: it queries the real `Assembly-CSharp.dll`. Never answer from memory.

## Common tasks

- **Test a mod against every version** →
  `tb run --mod <fragment> --profile matrix --json`
- **Register a new game version** → if the installation is already there:
  `tb versions scan --add` (reads the version from `MicrosoftGame.Config`, not from
  the folder name, see
  [trap 16](docs/conventions/traps.md#16-the-folder-name-is-not-a-version)).
  If it is not there yet: `tb versions add <version> --branch <branch>`, then fetch
  the installation with the printed `DepotDownloader` command (the human does that,
  because of the password and Steam Guard). In the window this is the *manage* button
  next to VERSIONS.
- **Register a new mod** → create a `testbench.mod.json` in `<repo>\test\`, then
  `tb mods add <path>`. Schema in
  [config-schema.md](docs/architecture/config-schema.md).
- **Why does it not run** → `tb doctor --json`, not searching through logs.
- **Compatibility list** → `tb report --mod <fragment> --write`.

## Rules that are not negotiable

1. **Everything that starts the game goes through `PrefsGuard`.** The GamePrefs live
   in `HKCU`, are shared by every installation and are redirectable by no launch
   parameter. Without a backup a run overwrites tuned live settings, silently.
2. **`--visual ok` is set only by a human who looked.** An agent uses
   `--visual defer`. Headless executes nothing graphical and nothing input related; log
   evidence cannot replace a visual check.
3. **One run at a time.** `RunLock` is machine-wide. Do not work around it.
4. **The live installation and the modlist are not modified.** Writing happens only
   into `gameRoot` installations and `userDataRoot`.
5. **No counter is "cleaned up" without looking at the parity tests.** The expected
   values in `LogAnalyzerParityTests` come from the old PowerShell logic on the same
   files. Changing them changes what gets reported as tested.
6. **Trap comments in the code stay where they are.** They are the safeguard, not
   decoration.

## The GUI

`Testbench.Gui.exe`, one window, without an MVVM package: `MainViewModel` is a class
with hand-written `INotifyPropertyChanged`. Runs happen on a thread pool thread,
messages come back through `Dispatcher.BeginInvoke`.

Three decisions worth knowing:

- **GUI runs are always `VisualMode.Defer`.** The question lands in the panel at the
  bottom, not in a modal dialog that opens while the game is still shutting down.
  That is the same queue an agent's runs sit in.
- **The log is tailed live from the file** (`RunOptions.LogPathReady` plus a tail with
  `FileShare.ReadWrite`). Without it a 35-second start looks like a hung program.
- **`VersionsWindow` works on the same `MachineConfig` instance** as the main window
  and saves it immediately. Afterwards the main window calls `ReloadVersions()`,
  which keeps the check marks and offers a freshly registered version already
  checked.

## State

Proven, not claimed: a parity run against `legacy/Invoke-SmokeTest.ps1` on the same
installation with identical status and identical counters, and four real logs as
fixtures whose expected values are measured from the old PowerShell logic. The test
suite is green and covers the analyzer, version detection, the trash folder migration
and the completeness of every language catalog.

Open, and deliberately open: whether the window *looks* good is as little a question
for a test here as a visual check inside the game is.
