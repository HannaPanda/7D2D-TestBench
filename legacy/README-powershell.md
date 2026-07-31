# 7DTD Multiversion-Testbench

Einen Mod gegen mehrere Spielversionen testen, ohne die Live-Modlist
(Smorgasbord) oder den Spielstand anzufassen.

## Warum drei Dinge isoliert werden muessen

| # | Was | Wie isoliert |
|---|---|---|
| 1 | Spielinstallation | eigene Kopie pro Version unter `E:\Games\7DTD-<version>` |
| 2 | Saves / Config / Gears | `-UserDataFolder=E:\Games\_TestUserData\<version>` |
| 3 | **GamePrefs** | `reg export` vor dem Lauf, `reg import` danach |
| 4 | Fremdmods | `Dependencies` spiegelt Gears/Quartz vor jedem Lauf hinein, alles uebrige wandert nach `_Mods-deaktiviert` |

Punkt 3 ist der unauffaellige. 7DTD legt die Optionen als Unity PlayerPrefs
unter `HKCU\Software\The Fun Pimps\7 Days To Die` ab - **ausserhalb** des
UserDataFolders, von allen Installationen geteilt, **durch keinen
Startparameter umbiegbar**. Ein frisch entpackter Build schreibt beim ersten
Start seine Defaults hinein und ueberschreibt damit lautlos die getunten
Live-Settings (`DynamicMeshDistance`, `DynamicMeshUseImposters`,
`OptionsGfxTexQuality`, `OptionsGfxViewDistance`).

Beweis, falls jemand zweifelt: ein Start mit brandneuem, leerem
UserDataFolder loggt trotzdem `Last played version: V 3.0.1`.

Referenzstand der getunten Werte: `E:\Backup\7DTD-Prefs\prefs_golden.reg`
(`DynamicMeshDistance 1000`, `UseImposters 1`, `TexQuality 1`,
`ViewDistance 5`). Jedes Skript hier sichert und stellt wieder her - **wer
etwas Neues baut, das das Spiel startet, muss das genauso machen.**

## Zwei Teststufen

**Stufe 1 - Headless, automatisiert.** `7DaysToDie.exe -dedicated -batchmode
-nographics` (der Fallback aus TFPs eigener `startdedicated.bat`; ein
`7DaysToDieServer.exe` liegt der Client-Installation nicht bei). Laeuft ohne
GUI und ohne Klicken, damit schleifenfaehig ueber beliebig viele Versionen.
Mod und Harmony sind nach ~1,5 s geladen, der komplette Start dauert ~35 s.

Deckt ab: Mod-Laden, Harmony-Patches, XML-Parsing, Localization, alle
`ERR`/`EXC` beim Start.

**Stufe 2 - GUI, manuell.** `.\Start-Gui.ps1 -Version <version> [-ConfigPath ...]`.

Deployt den Mod **und die Dependencies**, sichert die GamePrefs, startet mit
GUI und haelt danach den Nachweis in `gui-verified.json` fest - getrennt nach
`EvidenceOk` (im Log belegbar, Muster aus `Stage2.EvidencePatterns`) und
`VisualOk` (kann nur ein Mensch beantworten, Frage aus
`Stage2.VisualQuestion`). Beides ist mod-spezifisch und stand frueher fest im
Skript, weshalb ein GUI-Lauf jedes anderen Mods nach einem lila Kristallblock
fragte.

Noetig, weil `-nographics` **nichts Grafisches** ausfuehrt:
`TextureAtlasBlocks.LoadTextureAtlas` laeuft nicht, die Atlas-/Textur-
Injektion ist headless also **nicht** getestet. Pro Version einmal einen Block
setzen und anschauen.

## Welcher Mod getestet wird

`Testbench.psd1` (`ModRepo` + `Editions`) bestimmt die Quelle. Fuer einen
anderen Mod **nicht diese Datei umschreiben**, sondern eine eigene
Konfiguration mitgeben:

    .\Invoke-SmokeTest.ps1 -Version 3.0.1 `
        -ConfigPath C:\Users\sourc\7D2D-7DashesToDie\test\Testbench.SevenDashes.psd1

Der Ordnername im Mods-Verzeichnis ist der Quellordner; der Name, auf den die
Zeile `[MODS] Loaded Mod: <name>` geprueft wird, kommt aus `<Name>` der
`ModInfo.xml` des Mods. Beides war frueher fest auf `AdamantBlock` verdrahtet -
jeder andere Mod wurde dadurch faelschlich als "MOD NICHT GELADEN" gemeldet.

## Abhaengigkeiten (Gears/Quartz) laufen immer mit

`Dependencies` in der Konfiguration listet Fremdmods, die vor **jedem** Lauf
nach `Mods\<Name>` gespiegelt werden und vom Aufraeumschritt nie angefasst
werden:

    Dependencies = @(
        @{ Name = '0-Quartz'    ; Source = 'C:\Modlists\Smorgasbord\mods\Quartz\0-Quartz' }
        @{ Name = '00000-Gears' ; Source = 'C:\Modlists\...\00000-Gears' }
    )

Warum das noetig ist: der Aufraeumschritt kennt nur `KeepMods` und schiebt
alles andere nach `_Mods-deaktiviert`. Ein einziger Smoketest hat so schon
Gears und Quartz aus allen drei Installationen verbannt - der naechste
GUI-Test stand dann ohne Settingsmenue da, ohne dass irgendwo etwas
fehlgeschlagen waere. Mit dem Eintrag holt sich jeder Lauf beides selbst
zurueck.

**Bereitgestellt ist nicht geladen.** Nach dem Lauf wird fuer jede
Abhaengigkeit geprueft, ob `[MODS] Loaded Mod: <name>` wirklich im Log steht -
`<name>` kommt dabei aus der `ModInfo.xml` der *installierten* Kopie, denn der
Ordner heisst `00000-Gears` und der Mod meldet sich als `Gears`. Fehlt die
Zeile, ist der Status `ABHAENGIGKEIT FEHLT` und nicht `OK`. Per
Negativkontrolle verifiziert (Quelle verbogen + Ordner geloescht -> rot;
richtige Konfiguration -> gruen, Ordner wieder da).

## Benutzung

Eine Version:

    .\Invoke-SmokeTest.ps1 -Version 3.0.0 -Edition Creative

Alle konfigurierten Versionen plus Markdown-Report:

    .\Invoke-TestMatrix.ps1

Der Report unter `results\matrix_*.md` enthaelt am Ende die fertige
`TESTED_VERSIONS`-Zeile fuer `.github/workflows/release.yml`.

## Eine neue Spielversion aufnehmen

1. Installation nach `E:\Games\7DTD-<version>` besorgen (siehe unten).
2. Version in `Testbench.psd1` unter `Versions` eintragen.
3. `Invoke-SmokeTest.ps1 -Version <version>` laufen lassen.
4. Marker gegenpruefen: kommt `INF StartGame done` im Log vor? Falls TFP den
   Wortlaut geaendert hat, `ReadyPattern` in `Testbench.psd1` anpassen.

### Installation besorgen

**Variante A - Steam-Branch (ohne Zusatzwerkzeug, aber umstaendlich):**
Steam-Branch auf die Zielversion stellen, den Ordner
`C:\Steam\steamapps\common\7 Days To Die` nach `E:\Games\7DTD-<version>`
kopieren, Branch zurueckstellen, danach "Dateien ueberpruefen". Nachteil: die
Live-Installation wird zwischendurch angefasst, und MO2 darf in der Zeit nicht
starten.

**Variante B - DepotDownloader (empfohlen, sobald mehr als zwei Versionen):**
laedt einen Branch direkt in einen Zielordner, ohne die Steam-Installation
anzufassen.

    dotnet tool install -g DepotDownloader
    DepotDownloader -app 251570 -depot 251576 -branch v3.1.0 -dir E:\Games\7DTD-3.1.0 -username <dein-steam-name>

Steam fragt dann nach Passwort und Steam-Guard-Code - das gibst du selbst ein.

Danach die Fremdmods aus `Mods\` raeumen (`0_TFP_Harmony` bleibt); der
Smoketest macht das ab dem zweiten Lauf selbst.

## Marker- und Skript-Fallen (teuer gelernt)

- **Nicht auf `Started Telnet` warten.** Kommt nach ~2,7 s, lange bevor die
  XMLs geladen sind - der Lauf meldet gruen und hat nichts getestet.
- **`grep -a` auf `Assembly-CSharp.dll` findet keine String-Literale.** .NET
  legt Metadaten-Namen als UTF-8 ab, Literale als UTF-16 im `#US`-Heap. Ein
  negativer Treffer beweist nur "kein Member-Name", nicht "kommt nicht vor".
- **PowerShell 5.1, native Exes:** `& reg.exe ... *> $null` verpackt die
  Erfolgsmeldung als `NativeCommandError` und reisst bei
  `$ErrorActionPreference='Stop'` das Skript um - trotz Exit-Code 0.
- **`-match` auf einem Array** filtert nur und fuellt `$Matches` nicht.

## Dateien

| Datei | Zweck |
|---|---|
| `Testbench.psd1` | Pfade, Versionsliste, Ready-Marker, Timeout, Rauschfilter |
| `Invoke-SmokeTest.ps1` | Headless-Test einer Version (Stufe 1) |
| `Invoke-TestMatrix.ps1` | Schleife ueber alle Versionen + Report |
| `Start-Gui.ps1` | GUI-Lauf einer Version mit Prefs-Schutz (Stufe 2) |
| `gui-verified.json` | Stufe-2-Nachweise, an die Mod-Version gebunden |
| `results\` | Logs und Markdown-Reports |

### gui-verified.json

`Start-Gui.ps1` schreibt nach jedem Lauf einen Eintrag mit zwei getrennten
Feldern: `AtlasOk` (aus dem Log belegbar - Slice angehaengt, Texture-Id
gesetzt) und `VisualOk` (nur ein Mensch kann sagen, ob der Block richtig
aussieht; das Skript fragt am Ende nach). `Invoke-TestMatrix.ps1` schlaegt eine
Version nur dann fuer `TESTED_VERSIONS` vor, wenn Stufe 1 `OK` ist **und** ein
Eintrag mit beiden Feldern **fuer die aktuelle Mod-Version** vorliegt.

Die Bindung an die Mod-Version ist Absicht: ein Release, das am DLL- oder
Atlas-Code dreht, entwertet alle alten Sichtpruefungen. Der Report weist solche
Faelle als "nur Stufe 1 bestanden" aus, statt sie stillschweigend
mitzuschleppen.

`Start-Gui.ps1` loest die versionsspezifische
`E:\Games\7DTD-3.0.0\Start-Test.bat` ab - eine Datei fuer alle Versionen. Die
alte `.bat` funktioniert weiter, muesste aber pro Installation kopiert und
angepasst werden.
