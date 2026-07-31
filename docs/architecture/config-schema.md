# Configuration

Three levels, so nothing has to be maintained twice.

| Level | File | Belongs to |
|---|---|---|
| Machine | `D:\7DTD-Bench\testbench.json` | this computer |
| Mod | `<repo>\test\testbench.mod.json` | the mod, lives in its repo |
| Profile | in the mod JSON under `profiles` | a named test combination |

In the old bench both lived in the same `.psd1`, which is why every mod dragged its
own copy of `GameRoot`, `UserDataRoot`, `PrefsKey` and `Dependencies` along, and
every one of those copies could go stale.

## Where the machine configuration is looked for

In this order:

1. `--config <path>`
2. environment variable `TESTBENCH_CONFIG`
3. `<exe folder>\testbench.json`
4. `<exe folder>\..\testbench.json`

Number 4 is the normal case: `tb.exe` lies in `D:\7DTD-Bench\bin`, the config one
level above.

## testbench.json

```jsonc
{
  "language": "auto",                        // "german", "english", ... or the system language
  "gameRoot": "D:\\7DTD-Bench\\Games",       // installations: <gameRoot>\7DTD-<version>
  "userDataRoot": "D:\\7DTD-Bench\\UserData", // <root>\<version> or <version>-gui
  "resultRoot": "D:\\7DTD-Bench\\results",
  "stateRoot": "D:\\7DTD-Bench\\state",      // run store and lock file

  "prefs": {
    "key": "HKCU\\Software\\The Fun Pimps\\7 Days To Die",
    "backupDir": "D:\\7DTD-Bench\\prefs-backup",
    "goldenValues": {}                       // optional, see below
  },

  "versions": [                              // tb versions scan --add writes this
    { "id": "3.0.1", "branch": "v3.0.1", "build": "1.301.4.0", "notes": "live version" },
    { "id": "3.1.0", "path": "D:\\7DTD-Bench\\Games\\7DTD-3.1.0", "build": "1.310.14.0" }
  ],

  "modConfigs": [                            // where the mod configs live
    "D:\\Mods\\MyMod\\test\\testbench.mod.json"
  ],

  "dependencyLibrary": {
    "quartz": { "folder": "0-Quartz",    "source": "C:\\Modlists\\MyList\\mods\\Quartz\\0-Quartz" },
    "gears":  { "folder": "00000-Gears", "source": "C:\\Modlists\\MyList\\mods\\Gears\\00000-Gears",
                "requires": ["quartz"], "displayName": "Gears" }
  },

  "keepMods": ["0_TFP_Harmony"],
  "timeoutSeconds": 420,
  "readyPattern": "INF StartGame done",
  "fatalPattern": "(?i)(Fatal error|System\\.(NullReference|Type|Missing|Argument)\\w*Exception|HarmonyException)",
  "xmlProblemPattern": "XML loader:|XML patch for .+ did not apply|...",
  "ignorePatterns": ["\\[EOS\\]", "\\[Discord\\]", "Retrieving remote news file"]
}
```

### Fields that are traps

- **`versions[].path`** left empty means `<gameRoot>\7DTD-<id>`. Only set it when an
  installation lies somewhere else.
- **`versions[].readyPattern`** overrides the global marker for one version. Needed
  when TFP change the wording.
- **`versions[].build`** is the identity version from `MicrosoftGame.Config` as it
  stood there at registration time. Do not maintain it by hand: it is the reference
  value by which `tb doctor` notices that Steam updated the folder afterwards and
  that the folder name is therefore lying. See
  [traps.md](../conventions/traps.md#16-the-folder-name-is-not-a-version).
- **`readyPattern`** must not wait for Telnet, see
  [traps.md](../conventions/traps.md#2-do-not-wait-for-started-telnet). `tb doctor`
  checks that.
- **`dependencyLibrary[].folder`** carries the load order in its name (`0-`,
  `00000-`). 7DTD sorts by folder name, so the prefixes are load-bearing and do not
  get prettified. The key (`gears`) is what you type.
- **`keepMods`** is the only list the cleanup step knows about. The mod under test
  and everything from its dependencies are added automatically; everything else
  moves into `_Mods-disabled`.
- **`ignorePatterns`** stays deliberately narrow: only what demonstrably appears
  without a mod as well belongs in here. Hits are counted and reported, not
  swallowed.
- **`prefs.goldenValues`** is optional and normally empty. Every restore is verified
  anyway, by exporting the whole registry key again and comparing it against the
  backup (`PrefsGuard.RoundTrip`); that works for every setting and without any
  configuration. Entries here additionally mean "and this value has to come out
  exactly like this", for settings you absolutely do not want to lose.
- **`language`** is a catalog name from `lang\` (`english`, `german`, `schinese`,
  ...) or `"auto"` for the system language with English as the fallback. See
  [i18n.md](../i18n.md).

Comments and trailing commas are allowed in these files (the parser skips them), so
you can write into them by hand why something is the way it is. Complete, commented
examples are in [`examples/`](../../examples/).

## testbench.mod.json

```jsonc
{
  "modId": "mymod",     // from ModInfo.xml <Name>, lowercased
  "displayName": "My Mod",        // from ModInfo.xml <DisplayName>
  "repo": "D:\\Mods\\MyMod",

  "variants": [
    { "name": "Default", "folder": "MyMod" }
  ],

  "dependencies": ["quartz", "gears"],   // keys from dependencyLibrary

  "stage1": {
    "harmonyPattern": "Harmony patches applied",
    "requireHarmony": true,
    "extraFatalPatterns": []
  },

  "stage2": {
    "logFilter": "My Mod|HarmonyException| EXC | ERR ",
    "evidencePatterns": ["MyMod: registered"],
    "evidenceLabel": "registration proven in the log",
    "visualQuestion": "Did the block texture look right and did the key work?"
  },

  "profiles": [
    { "name": "matrix", "variant": "Default",
      "versions": ["3.0.0", "3.0.1", "3.1.0"], "stages": ["headless"] },
    { "name": "gui", "variant": "Default",
      "versions": ["3.1.0"], "stages": ["gui"] }
  ]
}
```

### Fields that are traps

- **`modId`** comes from `ModInfo.xml` `<Name>`, because that is exactly the name
  that appears in the log. On the command line a unique fragment is enough
  (`--mod mymod`).
- **`variants`** is a list, not a fixed Survival/Creative pair. A mod without
  editions has one variant; the importer folds two identical folders into a single
  one called `Default`.
- **`variants[].folder`** is the folder name in the Mods directory and **not**
  necessarily the reported mod name.
- **`stage1.harmonyPattern`** looks like a vanilla marker and is not one, the line
  belongs to the mod. A pure XML mod sets `requireHarmony: false`.
- **`stage2.evidencePatterns`**: **all** of them have to occur in the log. An empty
  list means "for this mod there is no evidence a log can carry" and is reported as
  such, instead of booking an empty pattern as passed.
- **`stage2.visualQuestion`** used to be hardcoded in the script, which is why a GUI
  run of any mod asked about one particular other mod's block.
- **`profiles`** are the reason nobody has to assemble a combination by hand.
  Explicit arguments still win, so a profile is also just a starting point.

## Run store

Every run is a file under `<stateRoot>\runs\<runId>.json`, where `runId` is
`<timestamp>_<modId>_<version>_<stage>`. It replaces `gui-verified.json`, which held
exactly one entry per mod/version/edition and therefore had no history.

Taking over old confirmations:

```bash
tb import --gui-verified D:\7DTD-Bench\gui-verified.json --mod mymod
```

Accepts `EvidenceOk` and `AtlasOk`, because both spellings could occur in the file.
That very disagreement is what made the old bench useless.
