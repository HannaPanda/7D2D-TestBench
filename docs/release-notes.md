# 7D2D-TestBench

Test a 7 Days to Die mod against several game versions, without touching the
installation you play.

**Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).**
Unpack, then run `tb.exe init` in the unpacked folder, or read
`LIESMICH-ZUERST.txt`.

## What it does

- **Stage 1, headless.** A dedicated server with `-batchmode -nographics`, about
  35 seconds per version, no clicking, so it loops over as many versions as you
  registered. Checks mod loading, Harmony patches, XML parsing, localization and
  every `ERR`/`EXC` during startup.
- **Stage 2, with a window.** The only thing that can judge the texture atlas,
  menus, keys and feel, because `-nographics` executes none of it. Ends when you
  close the game, then asks your mod's own question.
- **A compatibility list you can trust.** A version is only listed once both
  stages passed and a human confirmed the visual check for the *current* mod
  version. Nothing else produces a `TESTED_VERSIONS` line.
- **A window for people, a CLI for scripts and agents**, on the same core, with
  `--json` output and meaningful exit codes.
- **Thirteen languages**, the ones 7DTD itself ships, switchable at runtime and
  editable as plain JSON files in `lang\`.

## Careful with

Your played installation. A run moves every mod it did not install into
`_Mods-deaktiviert` so that a failure provably belongs to the mod under test.
Point it at your own copy and it would take your modlist apart. The tool locates
that folder specifically in order to refuse it, both in `tb doctor` and in the run
itself, but keep test installations separate anyway.

Your graphics settings are safe: 7DTD keeps them in the registry, shared by every
installation and not redirectable, so every run exports that key first, imports it
back afterwards and compares the two.
