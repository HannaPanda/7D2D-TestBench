# 7DTD Multiversion Testbench

Test a 7 Days to Die mod against several game versions without touching your own
modlist or your save games. One program with a window for people and a command line
for automation, both on the same core, in thirteen languages.

> The **tool** speaks the thirteen languages 7DTD itself ships and follows your system
> language. Its **documentation** is English only, on purpose: one source, no
> translation that quietly goes stale. See [`docs/i18n.md`](docs/i18n.md).

## Why

Before this, it was three PowerShell scripts with command lines like this one:

    .\Start-Gui.ps1 -Version 3.0.0 -Edition Survival -ConfigPath D:\Mods\MyMod\test\Testbench.psd1

You had to remember that, type it correctly, and hope it was right. And even then it
could silently do the wrong thing: the matrix script read `AtlasOk` while the GUI
script wrote `EvidenceOk`, which is why a compatibility list was **never** proposed
despite confirmed GUI runs.

Now:

```bash
tb run --mod mymod --profile matrix
```

## Two test stages

**Stage 1, headless.** A dedicated server with `-batchmode -nographics`, no clicking
involved, therefore loopable over as many versions as you like. One start takes about
35 seconds. Covers: mod loading, Harmony patches, XML parsing, localization, every
`ERR`/`EXC` during startup.

**Stage 2, with a window.** The only thing that can check the texture atlas, menus,
keys and how something feels, because `-nographics` executes none of that. It ends
when you close the window, and then it asks the mod-specific question.

A version reaches a compatibility list only once **both** stages passed and a human
confirmed the visual check for the **current** mod version.

## Requirements

- Windows (the bench reads the GamePrefs from the registry and starts 7DTD)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- One or more game installations to test against, **not** the one you play. Copies or
  `DepotDownloader` pulls, one folder per version.

## Setting up

Unpack the ZIP from the
[releases](https://github.com/HannaPanda/7D2D-TestBench/releases), then in the
unpacked folder:

```bash
tb init
```

That writes `testbench.json` and creates the folders next to it, finds the
installation you play and says explicitly that this one must not be registered.

Put game installations into `<bench>\Games\` (one folder per version), then:

```bash
tb versions scan --add
```

Register a mod: put a `testbench.mod.json` following the pattern in
[`examples/`](examples/) into `<mod-repo>\test\`, then run `tb mods add <path>`.

```bash
tb doctor
```

Says what is still missing before the first run.

Building from source works too:

```bash
dotnet publish src\Testbench.Cli\Testbench.Cli.csproj -c Release -o <target>
```

## Registering a new game version

If the installation is already on disk, nothing has to be typed:

```bash
tb versions scan --add
```

It searches under `gameRoot` for installations and reads out of each one which
version it is. In the window this is the *manage* button next to VERSIONS: *scan
here* searches the folder in the field, *choose folder...* takes a single
installation from anywhere, and if you accidentally pick the parent folder it simply
searches inside it.

The version does **not** come from the folder name but from the installation's
`MicrosoftGame.Config`. That is deliberate: `7DTD-3.0.1` does not mean 3.0.1 is what
lies there, as soon as Steam has updated the folder in place once. Where name and
build contradict each other, the line says so, and the folder is only registered
after you have looked.

If the installation is not there yet, `tb versions add 3.2.0 --branch v3.2.0` creates
the folder and prints the matching `DepotDownloader` command.

## The window

```bash
Testbench.Gui.exe
```

On the left you pick mod, variant, profile, versions and stages and press *start
run*. In the middle the log runs along (`ERR`/`EXC` red, `WRN` yellow, mod lines
green, noise grey, "follow along" can be switched off). On the right there is a result
tile per version, and below it the compatibility line with *copy*.

At the bottom a panel appears with open visual checks: every GUI run lands there with
the mod's own question and two buttons. That is deliberate instead of opening a modal
dialog while the game is still shutting down, and it is the place where you close out
the runs an agent started for you.

The last selection is remembered. Closing during a run asks first and then aborts
cleanly, so that the GamePrefs get restored.

## Languages

Top right in the window, or:

```bash
tb lang german
```

The default is the system language, otherwise English. The catalogs are named after
the language columns of the game's `Localization.csv` and sit in `lang\` as editable
JSON files: improving a translation means changing a line in there and restarting.
Details and the rules for new texts in [`docs/i18n.md`](docs/i18n.md).

## Using the command line

| What | Command |
|---|---|
| What is there | `tb versions`, `tb mods`, `tb profiles --mod mymod` |
| Every version headless | `tb run --mod mymod --profile matrix` |
| One version with a window | `tb run --mod mymod --version 3.1.0 --stage gui` |
| Recent runs | `tb status` |
| Open visual checks | `tb status --pending` |
| Answer a visual check | `tb verify --run <runId> --visual ok` |
| Compatibility list | `tb report --mod mymod --write` |
| The interesting log lines | `tb log --run <runId>` |
| Find installations | `tb versions scan`, register with `--add` |
| A new version without an installation | `tb versions add 3.2.0 --branch v3.2.0` |
| Language | `tb lang`, `tb lang <language>`, `tb lang <language> --check` |

Full options in [`docs/cli.md`](docs/cli.md).

## What the tool does not do

It does not download a game installation itself. `tb versions add` creates the folder
and prints the matching `DepotDownloader` command; the password and Steam Guard code
are yours to enter.

It does not modify the installation you play. It even locates it specifically in order
to rule it out: a version that lies there is refused by `tb doctor` and by the run
itself, because a run clears every foreign mod out of the `Mods` folder.

It does not answer a visual check. An agent can prepare and start a GUI run, but
whether something looks right and feels right is not something a log pattern can
replace.

## License

[MIT](LICENSE). Use it, change it, pass it on, inside something of your own and
commercially too; all that has to stay is the copyright notice. Without warranty,
which for a tool that rearranges game installations and writes to the registry is not
fine print but the reason `tb doctor` exists.

Translations, bug reports and traps still missing here are welcome:
[issues](https://github.com/HannaPanda/7D2D-TestBench/issues).

## Further reading

- [`docs/conventions/traps.md`](docs/conventions/traps.md) - the traps this project
  knows about. Worth reading even without any intention of changing the code: it says
  why the bench does things that look excessive at first glance.
- [`docs/architecture/core.md`](docs/architecture/core.md)
- [`docs/architecture/config-schema.md`](docs/architecture/config-schema.md)
- [`docs/i18n.md`](docs/i18n.md)
- [`AGENTS.md`](AGENTS.md)
