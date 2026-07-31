# Nexus description

Source text for the Nexus mod page. Nexus wants BBCode, so `##` becomes
`[size=4][b]...[/b][/size]`, `**x**` becomes `[b]x[/b]`, code blocks become
`[code]...[/code]` and links become `[url=...]...[/url]`. Keep this file as the
single source and convert on paste, rather than editing the page and losing track
of what it says.

---

## 7 Days to Die Multiversion Testbench

A tool for mod authors. It runs your mod against several game versions, checks
the log for you, and produces a compatibility list you can actually stand behind.

"Should work on 3.x" is not a claim anyone can make. A mod is compatible with the
builds it was launched on, and the two things that break silently on a new build
are exactly the ones a quick look never catches: a Harmony patch whose target
signature moved, and an XML attribute the engine stopped honouring.

## What it does

**Stage 1, headless.** Starts a dedicated server with `-batchmode -nographics`,
about 35 seconds per version, no clicking involved, so it loops over as many
versions as you registered. It checks that your mod actually loaded (the
`[MODS] Loaded Mod:` line, not the folder you copied), that Harmony applied, that
your XML parsed, and it counts every `ERR` and `EXC` during startup.

**Stage 2, with a window.** The only stage that can judge a texture atlas, a
menu, a key binding or how something feels, because `-nographics` executes none of
that. The run ends when you close the game, and then the tool asks the question
you wrote for your mod, in a panel rather than a popup.

**A compatibility list that means something.** A version is only listed once
stage 1 passed, stage 2 ran, the log evidence you configured was found, and a
human confirmed the visual check for the *current* mod version. Bump your mod
version and the confirmations expire, because a release that touches DLL code
invalidates every earlier look.

**Two front ends, one core.** A window for people. A command line for scripts,
CI and AI assistants, with `--json` and exit codes that mean something: 0 fine,
1 test failed, 2 configuration or environment, 3 refused because another run
holds the lock.

**Your settings survive.** 7 Days to Die keeps its options as Unity PlayerPrefs
in the registry, outside the user data folder, shared by every installation and
not redirectable by any launch parameter. A freshly unpacked build overwrites them
with its defaults, silently. Every run here exports that key first, imports it
back afterwards, and then compares the two to prove it worked.

**Thirteen languages.** The same ones 7DTD ships: English, Deutsch, Espanol,
Francais, Italiano, Japanese, Korean, Polski, Portugues do Brasil, Russian,
Turkce, and both Chinese scripts. It follows your system language by default, and
the texts are plain JSON files you can fix yourself.

## Requirements

- Windows
- .NET 10 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/10.0
- One game installation per version you want to test. Copies, or pulls with
  DepotDownloader. The tool does not download anything itself: your Steam
  password and Steam Guard code are yours.

## Getting started

Unpack, then in the unpacked folder:

    tb.exe init

That writes the configuration, creates the folders, finds the installation you
play and tells you not to register it. Put a game installation per version into
`Games\`, then:

    tb.exe versions scan --add

It reads each installation's real version out of its `MicrosoftGame.Config`
instead of trusting the folder name, because a folder name lies as soon as Steam
updates the folder in place. Where the name and the build disagree, it says so and
refuses to register it.

Describe your mod once in a small JSON file next to it (there is a commented
example in the download), register it with `tb.exe mods add`, and from then on:

    tb.exe run --mod mymod --profile matrix
    tb.exe report --mod mymod

Or open `Testbench.Gui.exe` and click.

## A warning worth reading

A run moves every mod it did not install itself into `_Mods-disabled`, so that a
failure provably belongs to the mod under test. Pointed at the installation you
play, it would take your modlist apart. The tool locates your live installation
specifically in order to refuse it, in `tb doctor` and in the run itself, but keep
your test installations separate anyway.

## Licence

MIT. Use it, change it, ship it inside your own thing, commercially too; all you
have to keep is the copyright notice. No warranty, which for a tool that rearranges
game installations and writes to the registry is the honest position rather than
boilerplate.

Translations and bug reports are welcome. The language files are plain JSON, one
per language, and a pull request that adds one is the whole contribution.

## Source and issues

https://github.com/HannaPanda/7D2D-TestBench

The repository documents the traps this tool exists because of: why waiting for
`Started Telnet` reports green having tested nothing, why "provided" is not
"loaded", why a successful XML patch logs nothing at all, and why a folder name is
not a version.
