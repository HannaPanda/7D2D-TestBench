# Konfiguration

Drei Ebenen, damit nichts doppelt gepflegt werden muss.

| Ebene | Datei | Gehört wem |
|---|---|---|
| Maschine | `E:\7DTD-Testbench\testbench.json` | diesem Rechner |
| Mod | `<repo>\test\testbench.mod.json` | dem Mod, liegt in seinem Repo |
| Profil | im Mod-JSON unter `profiles` | einer benannten Testkombination |

Im alten Bench lag beides in derselben `.psd1`, weshalb jeder Mod eine eigene
Kopie von `GameRoot`, `UserDataRoot`, `PrefsKey` und `Dependencies` mitschleppte
und jede dieser Kopien veralten konnte.

## Fundort der Maschinenkonfiguration

In dieser Reihenfolge:

1. `--config <pfad>`
2. Umgebungsvariable `TESTBENCH_CONFIG`
3. `<exe-ordner>\testbench.json`
4. `<exe-ordner>\..\testbench.json`

Punkt 4 ist der Normalfall: `tb.exe` liegt in `E:\7DTD-Testbench\bin`, die Config
eine Ebene darüber.

## testbench.json

```jsonc
{
  "gameRoot": "E:\\Games",                  // Installationen: <gameRoot>\7DTD-<version>
  "userDataRoot": "E:\\Games\\_TestUserData", // <root>\<version> bzw. <version>-gui
  "resultRoot": "E:\\7DTD-Testbench\\results",
  "stateRoot": "E:\\7DTD-Testbench\\state",  // Run-Store und Lockfile

  "prefs": {
    "key": "HKCU\\Software\\The Fun Pimps\\7 Days To Die",
    "backupDir": "E:\\Backup\\7DTD-Prefs",
    "goldenReg": "E:\\Backup\\7DTD-Prefs\\prefs_golden.reg",
    "goldenValues": {                        // nach dem Restore geprüft
      "DynamicMeshDistance": 1000,
      "DynamicMeshUseImposters": 1,
      "OptionsGfxTexQuality": 1,
      "OptionsGfxViewDistance": 5
    }
  },

  "versions": [
    { "id": "3.0.1", "branch": "public", "notes": "Live-Version" },
    { "id": "3.1.0", "path": "E:\\Games\\7DTD-3.1.0", "readyPattern": null }
  ],

  "modConfigs": [                            // wo die Mod-Configs liegen
    "C:\\Users\\sourc\\7D2D-7DashesToDie\\test\\testbench.mod.json"
  ],

  "dependencyLibrary": {
    "quartz": { "folder": "0-Quartz",    "source": "C:\\Modlists\\..." },
    "gears":  { "folder": "00000-Gears", "source": "C:\\Modlists\\...",
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

### Felder mit Fallen

- **`versions[].path`** leer lassen heißt `<gameRoot>\7DTD-<id>`. Nur setzen, wenn
  eine Installation woanders liegt.
- **`versions[].readyPattern`** überschreibt den globalen Marker für eine Version.
  Nötig, wenn TFP den Wortlaut ändert.
- **`readyPattern`** darf nicht auf Telnet warten, siehe
  [traps.md](../conventions/traps.md#2-nicht-auf-started-telnet-warten). `tb doctor`
  prüft das.
- **`dependencyLibrary[].folder`** trägt die Ladereihenfolge im Namen (`0-`,
  `00000-`). 7DTD sortiert nach Ordnernamen, die Präfixe sind also tragend und
  werden nicht verschönert. Der Schlüssel (`gears`) ist das, was man tippt.
- **`keepMods`** ist die einzige Liste, die der Aufräumschritt kennt. Der Mod unter
  Test und alles aus seinen Dependencies kommen automatisch dazu; alles andere
  wandert nach `_Mods-deaktiviert`.
- **`ignorePatterns`** bewusst eng halten: hier gehört nur hinein, was nachweislich
  auch ohne Mod erscheint. Treffer werden gezählt und ausgewiesen, nicht
  verschluckt.
- **`prefs.goldenValues`** sind die vier Werte, die das RAM-Thrashing behoben
  haben. Sie werden nach jedem Restore geprüft.

Kommentare und nachgestellte Kommas sind in diesen Dateien erlaubt (der Parser
überliest sie), damit man von Hand hineinschreiben kann, warum etwas so steht.

## testbench.mod.json

```jsonc
{
  "modId": "sevendashestodie",     // aus ModInfo.xml <Name>, kleingeschrieben
  "displayName": "7 Dashes to Die",// aus ModInfo.xml <DisplayName>
  "repo": "C:\\Users\\sourc\\7D2D-7DashesToDie",

  "variants": [
    { "name": "Default", "folder": "SevenDashesToDie" }
  ],

  "dependencies": ["quartz", "gears"],   // Schlüssel aus dependencyLibrary

  "stage1": {
    "harmonyPattern": "Harmony patches applied",
    "requireHarmony": true,
    "extraFatalPatterns": []
  },

  "stage2": {
    "logFilter": "7 Dashes to Die|HarmonyException| EXC | ERR ",
    "evidencePatterns": ["dash key registered", "dash added to the controller bindings list"],
    "evidenceLabel": "Dash-Taste und Controller-Zeile im Log belegt",
    "visualQuestion": "Controller-Zeile unter Options > Controller > On Foot da ...?"
  },

  "profiles": [
    { "name": "matrix", "variant": "Default",
      "versions": ["3.0.0", "3.0.1", "3.1.0"], "stages": ["headless"] },
    { "name": "gui", "variant": "Default",
      "versions": ["3.1.0"], "stages": ["gui"] }
  ]
}
```

### Felder mit Fallen

- **`modId`** kommt aus `ModInfo.xml` `<Name>`, weil genau dieser Name im Log
  steht. Auf der Kommandozeile genügt ein eindeutiges Fragment (`--mod seven`).
- **`variants`** ist eine Liste, kein festes Survival/Creative-Paar. Ein Mod ohne
  Editionen hat eine Variante; der Importer klappt zwei identische Ordner zu einer
  namens `Default` zusammen.
- **`variants[].folder`** ist der Ordnername im Mods-Verzeichnis und **nicht**
  zwangsläufig der gemeldete Modname.
- **`stage1.harmonyPattern`** sieht wie ein Vanilla-Marker aus und ist keiner, die
  Zeile gehört dem Mod. Ein reiner XML-Mod setzt `requireHarmony: false`.
- **`stage2.evidencePatterns`**: **alle** müssen im Log vorkommen. Leere Liste
  bedeutet "für diesen Mod gibt es keinen im Log belegbaren Nachweis" und wird als
  solches ausgewiesen, statt ein leeres Muster als bestanden zu verbuchen.
- **`stage2.visualQuestion`** stand früher fest im Skript, weshalb ein GUI-Lauf
  jedes anderen Mods nach einem lila Kristallblock fragte.
- **`profiles`** sind der Grund, dass niemand eine Kombination von Hand
  zusammenbauen muss. Explizite Argumente gewinnen weiterhin, ein Profil ist also
  auch ein Startpunkt.

## Run-Store

Jeder Lauf ist eine Datei unter `<stateRoot>\runs\<runId>.json`, `runId` ist
`<zeitstempel>_<modId>_<version>_<stufe>`. Ersetzt `gui-verified.json`, das genau
einen Eintrag pro Mod/Version/Edition hielt und damit keine Historie hatte.

Übernahme alter Bestätigungen:

```bash
tb import --gui-verified E:\7DTD-Testbench\gui-verified.json --mod adamant
```

Akzeptiert `EvidenceOk` und `AtlasOk`, weil beide Schreibweisen in der Datei
vorkommen konnten. Genau diese Uneinigkeit hat den alten Bench nutzlos gemacht.
