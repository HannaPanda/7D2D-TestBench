# Traps this project knows about

Everything here has been paid for: either with a test run that reported green and
had checked nothing, or with lost settings. Anybody who touches one of these
places reads the matching paragraph first.

The code points back here at the affected spots. Those comments are part of the
safeguard and do not get "tidied up".

## 1. GamePrefs live in the registry and are shared

7DTD stores its options as Unity PlayerPrefs under
`HKCU\Software\The Fun Pimps\7 Days To Die`. That is **outside** the user data
folder, shared by **every** installation, and redirectable by **no** launch
parameter. A freshly unpacked build writes its defaults in there on first start
and silently overwrites tuned live settings (`DynamicMeshDistance`,
`DynamicMeshUseImposters`, `OptionsGfxTexQuality`, `OptionsGfxViewDistance`).

Proof, in case anybody doubts it: a start with a brand new, empty user data
folder still logs `Last played version`.

Consequence in the code: `PrefsGuard` backs the key up with `reg export` before
every run and imports it back afterwards, in a `finally` and exactly once. After
the restore it exports the key a second time and compares it value by value
against the backup, so "restored" is a verified statement rather than a hope. The
old scripts restored and trusted the outcome.

Unity mangles PlayerPrefs names into `<name>_h<hash>`, which is why lookups go by
prefix instead of comparing literally.

**Anything new that starts the game has to go through `PrefsGuard`.**

## 2. Do not wait for `Started Telnet`

That line arrives after about 2.7 seconds, long before the XMLs are loaded. A run
that waits for it reports green and has tested next to nothing. The marker is
`INF StartGame done` (measured at 31.7 s on 3.0.0).

`Doctor` raises an alarm when `readyPattern` contains the word Telnet.

## 3. `Move-Item -Force` does not overwrite a directory

It fails on one, and with `-ErrorAction SilentlyContinue` it fails silently. A mod
that had been moved into the trash folder once therefore stayed in the Mods folder
on every later run and loaded along, without anything anywhere turning red.

In the port: `ModDeployer.DisableForeignMods` clears the target first (the trash is
scrap by definition), then moves, and **verifies afterwards** that the source
folder is gone. If that fails it is an error and not a warning, because the run
would prove nothing.

The fixture log `smoke_3.0.0_Survival_2026-07-31_21-40-35.log` is the evidence of
what that looks like: `AdamantBlock` **and** `MyMod` are loaded at the same time.
The old bench could not see it, because it only ever asked whether *its own* mod
had loaded.

The folder is called `_Mods-disabled`. It used to be `_Mods-deaktiviert`, from the
days this tool was German only; `ModDeployer.MigrateLegacyTrash` carries an
existing one over and deliberately overwrites nothing while doing it.

## 4. The folder name is not the reported mod name

In the Mods directory the folder is called `00000-Gears`, in the log the mod
reports itself as `Gears`. What counts is `<Name>` from `ModInfo.xml`, and from the
**installed** copy, because only that one says what is about to appear in the log.

Same with variants: `AdamantBlock-Creative` reports itself as `AdamantBlock`. A
check against the folder name yields "MOD NOT LOADED" for a mod that loaded
perfectly. The test `Folder_name_is_not_the_reported_name` holds that down.

`<DisplayName>` is a third, again different thing (`7 Dashes to Die`) and only
shows up in the mod's own log line.

## 5. Provided is not loaded

Copying a dependency proves nothing. A single smoke test banished Gears and Quartz
from all three installations into the trash folder (the cleanup step only knows
`keepMods`), and the next GUI test stood there without a settings menu, without
anything having failed anywhere.

That is why `[MODS] Loaded Mod: <reported name>` is proven in the log for every
dependency. If the line is missing the status is `DEPENDENCY MISSING` and not
`OK`. Gears needs Quartz; that fact lives once in
`dependencyLibrary.gears.requires` and is resolved, instead of being repeated in
the right order in every mod configuration.

## 6. `Harmony patches applied` is not a line of the game

It looks like a vanilla marker and is not one. The real line belongs to the mod:

    INF [7 Dashes to Die] loaded from ...\Mods\MyMod, Harmony patches applied

The default works for both existing mods because both happen to end their startup
message that way. A mod that words it differently has to set
`stage1.harmonyPattern`, otherwise every run counts as `HARMONY MISSING`. A mod
without a DLL sets `stage1.requireHarmony` to `false`; in the old bench a pure XML
mod could never reach `OK`.

## 7. Headless covers nothing graphical and nothing input related

Under `-batchmode -nographics`, `TextureAtlasBlocks.LoadTextureAtlas` does not run,
so atlas and texture injection are **not** tested. Menus, keys and controllers are
not touched at all.

That is why stage 2 exists, why `VisualState` is a field of its own, and why
`--visual ok` may only be set by a human who looked. An agent uses
`--visual defer`; the run then sits there as `Pending` until somebody says
`tb verify`.

## 8. 7DTD does not log successful XML patches

"0 hits" means "nothing broken", not "checked". The patterns in `xmlProblemPattern`
come from the real message texts in `Assembly-CSharp` (UTF-16 byte search), not
from guesswork:

    XML loader: Loading XML patch file '{0}' from mod '{1}' failed:
    XML patch for "{0}" from mod "{1}" did not apply: {2} (line {3} ...)
    XML.Patch ({0}, line {1} at pos {2}): Patch type ({3}) unknown
    XML loader: XML is missing: ...

A negative control exists: `smoke_3.0.0_Creative_2026-07-29_18-33-00.log` contains
a deliberately broken xpath and yields exactly one XML hit but no ERR (the message
is a `WRN` line). The test `Broken_xpath_is_found_as_an_xml_problem_only` holds
both down.

## 9. `grep -a` on `Assembly-CSharp.dll` does not find string literals

.NET stores metadata names as UTF-8 and literals as UTF-16 in the `#US` heap. A
negative hit only proves "not a member name", not "does not occur". This holds for
any search for a message text inside the game.

## 10. Noise is counted, not swallowed

`ignorePatterns` removes known startup noise (`[EOS]`, `[Discord]`, news file)
**before** counting, so an ignored line does not count as an error. The number of
removed lines is reported, though. Filtering things away silently is exactly the
kind of green checkmark that means nothing.

The list stays deliberately narrow: only what demonstrably appears without a mod
as well belongs in it.

## 11. PowerShell traps (they hold for every PS remnant)

The port is C#, but PowerShell is still involved (`Psd1Importer` calls
`Import-PowerShellDataFile`, and the old scripts are still around under
[`legacy/`](../../legacy/) as reference).

- **Native exes and `$ErrorActionPreference='Stop'`:** under PowerShell 5.1 every
  stderr line of a native program becomes a `NativeCommandError` and tears the
  script down, **even on exit code 0**. Never `*> $null` on `reg.exe`; judge by
  exit code. The principle still holds in the port: `PrefsGuard.Reg` evaluates the
  exit code, not the presence of stderr output.
- **`-match` on an array** only filters and does not fill `$Matches`. Pull the line
  first, then match it on its own.
- **`ConvertFrom-Json` puts a JSON array into the pipeline as ONE object.**
  `@(...)` does not unpack that, it wraps it once more. `[object[]]` casts
  correctly.
- **`ConvertTo-Json` turns a single-element array into a scalar.**
  `Psd1Importer.StrList` therefore accepts a string **and** an array.
- **`Get-Content` produces no empty last line** for a file that ends with a
  newline. Every counter of the old bench was measured that way, which is why
  `GameLauncher.SplitLines` does the same. Without it, line count and "ignored"
  would be off by one against every old report.
- **`cd X && command` does not exist in PowerShell 5.1.** `&&` is a parser error.
  That was one of the reasons this tool exists.

## 12. Two runs at the same time do not work

They share Steam, the server ports and above all the prefs key: the backup of the
second run would capture the defaults of the first and then restore those as "the
tuned values". `RunLock` (named mutex plus lock file) prevents that machine-wide,
because the GUI and an agent can both start runs.

## 13. The game holds files open

The mod DLL is locked as long as `7DaysToDie.exe` runs, and the process is gone
before its handles are. That is why `GameLauncher.WaitUntilGone` waits for every
`7DaysToDie*` process and then two seconds more. The log file is read with
`FileShare.ReadWrite | FileShare.Delete`, because the game keeps it open during the
run.

## 14. Unity talks on stdout, even with `-logfile`

The allocator configuration and a few startup lines end up on stdout regardless.
Inherited by our console, that would sit in front of the envelope in `--json` mode
and make the result unparsable. `GameLauncher.Start` therefore redirects both
streams and drains them (unread pipes block the game as soon as the buffer is
full).

## 15. `EvidenceOk` versus `AtlasOk`

The bug that made the old bench useless: `Start-Gui.ps1` wrote `EvidenceOk`,
`Invoke-TestMatrix.ps1` read `AtlasOk`. `GuiOk` was therefore always `false`, and a
`TESTED_VERSIONS` line was **never** proposed although three confirmed GUI runs
sat in the store. On top of that the matrix only compared the game version, not
mod and variant, so a confirmation for one mod would have turned another mod's run
green.

Lesson for the port: one store, one typed model, written and read by the same
place. `RunStore.ImportGuiVerified` accepts both spellings so old entries are not
lost.

## 16. The folder name is not a version

`D:\7DTD-Bench\Games\7DTD-3.0.1` does not mean 3.0.1 is what lies there. A folder
gets renamed, copied, or Steam updates it in place, and the name stays as it was. A
bench that tracks versions by folder name then calmly keeps reporting results for a
version that was never started.

Inside an installation the number is nowhere to be found as text:
`Assembly-CSharp.dll` assembles the string at runtime, and a search for `3.0.1`
across the whole folder finds nothing but EAC and Steam logs. What does exist is
the identity version in `MicrosoftGame.Config`, shipped as
`1.<major><minor><patch>.<build>`:

| Version | Identity |
|---|---|
| 3.0.0 | `1.300.259.0` |
| 3.0.1 | `1.301.4.0` |
| 3.1.0 | `1.310.14.0` |

`VersionScanner.IdFromBuild` reads the three-digit middle and nothing else. Any
other shape returns `null` rather than a plausible-looking wrong answer; the folder
name then takes over, and the line says that this was a guess.

That is why there are three statements and one comparison: the registered id, the
build at registration time (`versions[].build`), and the `INF Version:` line of the
last real run, the only one of the three that cannot be a mix-up of names.
`tb doctor` puts them side by side. `tb versions scan` does not register a folder
whose name contradicts its build without `--force`.
