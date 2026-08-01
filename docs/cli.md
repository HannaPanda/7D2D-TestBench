# tb - the command line

`tb.exe` and `testbench.json` live in the same folder. The configuration is looked
for next to the exe and one level above it, so the layout
`<bench>\bin\tb.exe` next to `<bench>\testbench.json` works as well.

## Exit codes

Part of the contract. An agent decides what happens next from them.

| Code | Meaning |
|---|---|
| 0 | fine |
| 1 | test failed (the tool itself worked) |
| 2 | configuration or environment error, nothing was tested |
| 3 | refused on purpose: another run holds the lock |

A single failed run fails the whole call. Nobody should have to add up lines to
find out whether something is broken.

## --json

Exactly one JSON object on stdout, nothing else. Progress and warning messages end
up in the `messages` field, the result in `data`.

```json
{ "ok": true, "command": "run", "exitCode": 0, "messages": ["..."], "data": { "runs": [ ... ] } }
```

The game's console output is captured and discarded, otherwise Unity's allocator
configuration would sit in front of the envelope.

In JSON mode `--visual` defaults to `defer`: there is nobody at a console who could
answer the question, and an unanswered run must not count as confirmed.

## Setting up

```bash
tb init
```

Writes `testbench.json` and creates the folders below it, all derived from where
the exe lies. `--bench-root <path>` picks a different place, `--game-root <path>`
points at installations that already exist, `--lang <language>` sets the language.

```bash
tb import --psd1 D:\7DTD-Bench\Testbench.psd1 --mod-out D:\Mods\MyMod\test\testbench.mod.json
```

Splits an old `.psd1` into a machine part and a mod part, writes the mod part next
to the mod and registers it. Several imports merge their machine parts; differences
in an already known dependency are reported and **not** taken over.

```bash
tb import --gui-verified D:\7DTD-Bench\gui-verified.json --mod mymod
```

```bash
tb doctor
```

Checks paths, versions, dependency sources, mod sources, every regular expression,
running games, the run lock, the GamePrefs against the golden values and open
visual checks. Exit 2 when no run can happen like this.

For every version it also compares three independent statements: the registered id,
the build in `MicrosoftGame.Config` and the `INF Version:` line of the last real
run. If they contradict each other, every report about that version is wrong, and
no log would ever say so.

## Looking around

```bash
tb versions
```

The *Build* column is the identity version from the installation's
`MicrosoftGame.Config`. Status `CHANGED` means a different build lies there than at
registration time, so the folder name no longer holds.

### Finding versions instead of typing them

```bash
tb versions scan
```

Searches under `gameRoot` (or `--root <folder>`, default depth 2, `--depth n`) for
installations and says for each one which version it is and how it knows. It does
not descend into an installation it found.

```bash
tb versions scan --add
```

Registers everything there is no doubt about, and fills in the build for versions
already registered. A folder whose name contradicts its build is **not**
registered: that is the case where a report afterwards claims a version that was
never tested. With `--force` anyway.

```bash
tb versions add --path "D:\7DTD-Bench\Games\7DTD-3.2.0"
```

Reads the version out of the installation. With
`tb versions add 3.2.0 --path <folder>` you state it yourself; with
`tb versions add 3.2.0 --branch v3.2.0` and no folder it creates one and prints the
`DepotDownloader` command. The tool downloads nothing itself: password and Steam
Guard code are yours to enter.

**Where the version comes from.** Inside an installation the version number is
nowhere to be found as text; `Assembly-CSharp.dll` assembles the string at runtime.
What is usable is the identity version in `MicrosoftGame.Config`: 3.0.1 ships
`1.301.4.0`, 3.1.0 ships `1.310.14.0`. Only that three-digit form is read,
everything else falls back to the folder name. The final authority remains the
`INF Version:` line of a real run, and that is exactly what `tb doctor` compares
against the entry.

```bash
tb mods
```

```bash
tb profiles --mod mymod
```

## Testing

```bash
tb run --mod mymod --profile matrix
```

```bash
tb run --mod mymod --version 3.0.1 --stage headless
```

Options of `run`:

| Option | Effect |
|---|---|
| `--mod <id>` | a unique fragment of the modId is enough |
| `--profile <name>` | named combination; explicit arguments win |
| `--version <v>` | repeatable or comma separated; all known ones if omitted |
| `--variant <name>` | the mod's first variant if omitted |
| `--stage headless\|gui` | repeatable, in the order given |
| `--visual ask\|defer\|ok` | default: `ask` at a terminal, `defer` with `--json` |
| `--skip-deploy` | takes whatever lies in the installation |
| `--timeout <s>`, `--ready-pattern <regex>` | override the configuration |
| `--note <text>` | stored in the run record |

A GUI run ends when the window is closed.

## Reading the results

```bash
tb status --pending
```

```bash
tb status --mod mymod --limit 5
```

`--mod` takes a unique fragment here too, the same rule `run` and `report` use, and an
unknown one is an error rather than an empty table: "no run stored yet" has to keep
meaning that the store is empty. An empty result says which of the three reasons it
was - no runs at all, none for this mod, or no visual check waiting.

```bash
tb verify --run 20260731-222549_mymod_3.0.1_gui --visual ok --note "controller line is there, double tap dashes"
```

For GUI runs only. A headless run executes nothing graphical, so there is nothing an
eye could have confirmed.

```bash
tb report --mod mymod --write
```

Shows the matrix and the `TESTED_VERSIONS` line, with `--write` also as markdown
under `resultRoot`. A version only makes the list when stage 1 is `OK` **and** a GUI
run **for the current mod version** had its visual check confirmed.

```bash
tb log --run <runId>
```

Without further arguments the interesting lines, with `--lines <n>` the last n lines
of the log.

## Language

```bash
tb lang
```

Shows every available language, which one is active, which one the system language
would be and how many keys a translation is missing.

```bash
tb lang german
```

Sets it and writes it into `testbench.json`. `tb lang <language> --check` names
every missing key. `--lang <language>` applies to one call only. Background in
[i18n.md](i18n.md).

## The sequence for an agent

```bash
tb doctor --json
```
```bash
tb run --mod mymod --profile matrix --json
```
```bash
tb run --mod mymod --version 3.1.0 --stage gui --visual defer --json
```
```bash
tb status --pending --json
```
```bash
tb report --mod mymod --json
```

An agent can do everything except the visual check. That one stays open until a
human says `tb verify` or answers in the GUI.
