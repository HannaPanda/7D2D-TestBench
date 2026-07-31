# Konfiguration

Drei Ebenen, damit nichts doppelt gepflegt werden muss.

| Ebene | Datei | Gehört wem |
|---|---|---|
| Maschine | `D:\7DTD-Bench\testbench.json` | diesem Rechner |
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

Punkt 4 ist der Normalfall: `tb.exe` liegt in `D:\7DTD-Bench\bin`, die Config
eine Ebene darüber.

## testbench.json

```jsonc
{
  "language": "auto",                        // "german", "english", ... oder Systemsprache
  "gameRoot": "D:\\7DTD-Bench\\Games",                  // Installationen: <gameRoot>\7DTD-<version>
  "userDataRoot": "D:\\7DTD-Bench\\UserData", // <root>\<version> bzw. <version>-gui
  "resultRoot": "D:\\7DTD-Bench\\results",
  "stateRoot": "D:\\7DTD-Bench\\state",  // Run-Store und Lockfile

  "prefs": {
    "key": "HKCU\\Software\\The Fun Pimps\\7 Days To Die",
    "backupDir": "D:\\7DTD-Bench\\prefs-backup",
    "goldenValues": {}                       // optional, siehe unten
  },

  "versions": [                              // tb versions scan --add schreibt das
    { "id": "3.0.1", "branch": "v3.0.1", "build": "1.301.4.0", "notes": "Live-Version" },
    { "id": "3.1.0", "path": "D:\\7DTD-Bench\\Games\\7DTD-3.1.0", "build": "1.310.14.0" }
  ],

  "modConfigs": [                            // wo die Mod-Configs liegen
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

### Felder mit Fallen

- **`versions[].path`** leer lassen heißt `<gameRoot>\7DTD-<id>`. Nur setzen, wenn
  eine Installation woanders liegt.
- **`versions[].readyPattern`** überschreibt den globalen Marker für eine Version.
  Nötig, wenn TFP den Wortlaut ändert.
- **`versions[].build`** ist die Identity-Version aus `MicrosoftGame.Config`, wie
  sie beim Eintragen dort stand. Nicht von Hand pflegen: sie ist der
  Vergleichswert, an dem `tb doctor` merkt, dass Steam den Ordner im Nachhinein
  aktualisiert hat und der Ordnername damit lügt. Siehe
  [traps.md](../conventions/traps.md#16-der-ordnername-ist-keine-versionsangabe).
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
- **`prefs.goldenValues`** ist optional und normalerweise leer. Jeder Restore wird
  ohnehin geprüft, indem der ganze Registry-Key erneut exportiert und gegen die
  Sicherung verglichen wird (`PrefsGuard.RoundTrip`); das funktioniert für jede
  Einstellung und ohne Konfiguration. Einträge hier bedeuten zusätzlich "und dieser
  Wert muss danach genau das ergeben", für Einstellungen, die man auf keinen Fall
  verlieren will.
- **`language`** ist ein Katalogname aus `lang\` (`english`, `german`,
  `schinese`, ...) oder `"auto"` für die Systemsprache mit Englisch als Rückfall.
  Siehe [i18n.md](../i18n.md).

Kommentare und nachgestellte Kommas sind in diesen Dateien erlaubt (der Parser
überliest sie), damit man von Hand hineinschreiben kann, warum etwas so steht.
Vollständige, kommentierte Beispiele liegen in [`examples/`](../../examples/).

## testbench.mod.json

```jsonc
{
  "modId": "mymod",     // aus ModInfo.xml <Name>, kleingeschrieben
  "displayName": "My Mod",        // aus ModInfo.xml <DisplayName>
  "repo": "D:\\Mods\\MyMod",

  "variants": [
    { "name": "Default", "folder": "MyMod" }
  ],

  "dependencies": ["quartz", "gears"],   // Schlüssel aus dependencyLibrary

  "stage1": {
    "harmonyPattern": "Harmony patches applied",
    "requireHarmony": true,
    "extraFatalPatterns": []
  },

  "stage2": {
    "logFilter": "My Mod|HarmonyException| EXC | ERR ",
    "evidencePatterns": ["MyMod: registered"],
    "evidenceLabel": "Registrierung im Log belegt",
    "visualQuestion": "Sah die Blocktextur richtig aus und ging die Taste?"
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
  steht. Auf der Kommandozeile genügt ein eindeutiges Fragment (`--mod mymod`).
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
  jedes Mods nach dem Block eines bestimmten anderen Mods fragte.
- **`profiles`** sind der Grund, dass niemand eine Kombination von Hand
  zusammenbauen muss. Explizite Argumente gewinnen weiterhin, ein Profil ist also
  auch ein Startpunkt.

## Run-Store

Jeder Lauf ist eine Datei unter `<stateRoot>\runs\<runId>.json`, `runId` ist
`<zeitstempel>_<modId>_<version>_<stufe>`. Ersetzt `gui-verified.json`, das genau
einen Eintrag pro Mod/Version/Edition hielt und damit keine Historie hatte.

Übernahme alter Bestätigungen:

```bash
tb import --gui-verified D:\7DTD-Bench\gui-verified.json --mod mymod
```

Akzeptiert `EvidenceOk` und `AtlasOk`, weil beide Schreibweisen in der Datei
vorkommen konnten. Genau diese Uneinigkeit hat den alten Bench nutzlos gemacht.
