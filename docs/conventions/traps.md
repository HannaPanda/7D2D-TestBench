# Fallen, die dieses Projekt kennt

Alles hier ist bezahlt worden: entweder mit einem Testlauf, der grün meldete und
nichts geprüft hatte, oder mit verlorenen Einstellungen. Wer eine dieser Stellen
anfasst, liest vorher den Absatz dazu.

Der Code verweist an den betroffenen Stellen hierher zurück; die Kommentare dort
sind Teil der Absicherung und werden nicht "aufgeräumt".

## 1. GamePrefs liegen in der Registry und werden geteilt

7DTD legt seine Optionen als Unity PlayerPrefs unter
`HKCU\Software\The Fun Pimps\7 Days To Die` ab. Das ist **außerhalb** des
UserDataFolders, wird von **allen** Installationen geteilt und ist durch
**keinen** Startparameter umbiegbar. Ein frisch entpackter Build schreibt beim
ersten Start seine Defaults hinein und überschreibt lautlos die getunten
Live-Settings (`DynamicMeshDistance`, `DynamicMeshUseImposters`,
`OptionsGfxTexQuality`, `OptionsGfxViewDistance`).

Beweis, falls jemand zweifelt: ein Start mit brandneuem, leerem UserDataFolder
loggt trotzdem `Last played version`.

Konsequenz im Code: `PrefsGuard` sichert vor jedem Lauf per `reg export` und
spielt hinterher zurück, im `finally` und genau einmal. Nach dem Restore werden
die vier Werte gegen `prefs.goldenValues` geprüft und Abweichungen gemeldet. Die
alten Skripte haben restauriert und dem Ergebnis vertraut.

Unity verunstaltet PlayerPrefs-Namen zu `<name>_h<hash>`, deshalb wird per Präfix
gesucht und nicht literal verglichen.

**Wer etwas Neues baut, das das Spiel startet, muss durch `PrefsGuard` gehen.**

## 2. Nicht auf `Started Telnet` warten

Diese Zeile kommt nach etwa 2,7 Sekunden, lange bevor die XMLs geladen sind. Ein
Lauf, der darauf wartet, meldet grün und hat praktisch nichts getestet. Der
Marker ist `INF StartGame done` (bei 3.0.0 empirisch nach 31,7 s).

`Doctor` schlägt Alarm, wenn `readyPattern` das Wort Telnet enthält.

## 3. `Move-Item -Force` überschreibt kein Verzeichnis

Es scheitert daran, und mit `-ErrorAction SilentlyContinue` lautlos. Ein Mod, der
einmal nach `_Mods-deaktiviert` verschoben wurde, blieb dadurch bei jedem
weiteren Lauf im Mods-Ordner liegen und lud mit, ohne dass irgendwo etwas rot
wurde.

Im Port: `ModDeployer.DisableForeignMods` räumt den Zielordner vorher (die Ablage
ist per Definition Abfall), verschiebt dann und **prüft danach nach**, dass der
Quellordner weg ist. Schlägt das fehl, ist es ein Fehler und keine Warnung, denn
der Lauf wäre nicht aussagekräftig.

Der Fixture-Log `smoke_3.0.0_Survival_2026-07-31_21-40-35.log` ist ein Beleg
dafür, wie das aussieht: dort sind `AdamantBlock` **und** `SevenDashesToDie`
gleichzeitig geladen. Der alte Bench konnte das nicht sehen, weil er nur fragte,
ob *sein* Mod geladen ist.

## 4. Ordnername ist nicht der gemeldete Modname

Im Mods-Verzeichnis heißt der Ordner `00000-Gears`, im Log meldet sich der Mod
als `Gears`. Maßgeblich ist `<Name>` aus der `ModInfo.xml`, und zwar aus der
**installierten** Kopie, denn nur die sagt, was gleich im Log stehen wird.

Genauso bei Varianten: `AdamantBlock-Creative` meldet sich als `AdamantBlock`.
Eine Prüfung auf den Ordnernamen ergibt "MOD NICHT GELADEN" für einen Mod, der
einwandfrei geladen ist. Der Test `Folder_name_is_not_the_reported_name` hält das
fest.

`<DisplayName>` ist ein drittes, wieder anderes Ding (`7 Dashes to Die`) und
taucht nur in der eigenen Logzeile des Mods auf.

## 5. Bereitgestellt ist nicht geladen

Eine Abhängigkeit zu kopieren beweist nichts. Ein einziger Smoketest hat Gears
und Quartz aus allen drei Installationen nach `_Mods-deaktiviert` verbannt (der
Aufräumschritt kennt nur `keepMods`), und der nächste GUI-Test stand ohne
Settingsmenü da, ohne dass irgendwo etwas fehlgeschlagen wäre.

Deshalb wird für jede Abhängigkeit `[MODS] Loaded Mod: <gemeldeter Name>` im Log
nachgewiesen. Fehlt die Zeile, ist der Status `ABHAENGIGKEIT FEHLT` und nicht
`OK`. Gears braucht Quartz; das steht einmal in `dependencyLibrary.gears.requires`
und wird aufgelöst, statt in jeder Mod-Konfiguration in der richtigen Reihenfolge
wiederholt zu werden.

## 6. `Harmony patches applied` ist keine Zeile des Spiels

Es sieht wie ein Vanilla-Marker aus und ist keiner. Die echte Zeile gehört dem
Mod:

    INF [7 Dashes to Die] loaded from ...\Mods\SevenDashesToDie, Harmony patches applied

Der Default funktioniert für beide bestehenden Mods, weil beide ihre
Startmeldung zufällig so beenden. Ein Mod, der es anders formuliert, muss
`stage1.harmonyPattern` setzen, sonst gilt jeder Lauf als `HARMONY FEHLT`. Ein
Mod ohne DLL setzt `stage1.requireHarmony` auf `false`; im alten Bench konnte ein
reiner XML-Mod nie `OK` erreichen.

## 7. Headless deckt nichts Grafisches und nichts Eingabebezogenes ab

Unter `-batchmode -nographics` läuft `TextureAtlasBlocks.LoadTextureAtlas` nicht,
die Atlas-/Textur-Injektion ist also **nicht** getestet. Menüs, Tasten und
Controller werden überhaupt nicht angefasst.

Deshalb gibt es Stufe 2, deshalb ist `VisualState` ein eigenes Feld, und deshalb
darf `--visual ok` nur ein Mensch setzen, der hingesehen hat. Ein Agent
verwendet `--visual defer`; der Lauf bleibt dann als `Pending` liegen, bis
jemand `tb verify` sagt.

## 8. Erfolgreiche XML-Patches loggt 7DTD nicht

"0 Treffer" heißt "nichts kaputt", nicht "geprüft". Die Muster in
`xmlProblemPattern` stammen aus den echten Meldungstexten in `Assembly-CSharp`
(UTF-16-Bytesuche), nicht geraten:

    XML loader: Loading XML patch file '{0}' from mod '{1}' failed:
    XML patch for "{0}" from mod "{1}" did not apply: {2} (line {3} ...)
    XML.Patch ({0}, line {1} at pos {2}): Patch type ({3}) unknown
    XML loader: XML is missing: ...

Negativkontrolle vorhanden: `smoke_3.0.0_Creative_2026-07-29_18-33-00.log`
enthält einen absichtlich kaputten xpath und liefert genau einen XML-Treffer,
aber keinen ERR (die Meldung ist eine `WRN`-Zeile). Der Test
`Broken_xpath_is_found_as_an_xml_problem_only` hält beides fest.

## 9. `grep -a` auf `Assembly-CSharp.dll` findet keine String-Literale

.NET legt Metadaten-Namen als UTF-8 ab, Literale als UTF-16 im `#US`-Heap. Ein
negativer Treffer beweist nur "kein Member-Name", nicht "kommt nicht vor". Gilt
für jede Suche nach einem Meldungstext im Spiel.

## 10. Rauschen wird gezählt, nicht verschluckt

`ignorePatterns` entfernt bekanntes Startrauschen (`[EOS]`, `[Discord]`,
Newsfile) **vor** dem Zählen, damit eine ignorierte Zeile nicht als Fehler
mitzählt. Die Zahl der entfernten Zeilen wird aber ausgewiesen. Stilles
Wegfiltern ist genau die Sorte grüner Haken, die nichts bedeutet.

Die Liste bleibt bewusst eng: hier gehört nur hinein, was nachweislich auch ohne
Mod erscheint.

## 11. PowerShell-Fallen (gelten für alle PS-Reste)

Der Port ist C#, aber es gibt weiterhin PowerShell im Spiel (`Psd1Importer` ruft
`Import-PowerShellDataFile`, und die alten Skripte liegen als Referenz noch
unter `E:\7DTD-Testbench`).

- **Native Exes und `$ErrorActionPreference='Stop'`:** unter PowerShell 5.1 wird
  jede stderr-Zeile eines nativen Programms zu einem `NativeCommandError` und
  reißt das Skript um, **auch bei Exit-Code 0**. Nie `*> $null` auf `reg.exe`;
  nach Exit-Code urteilen. Der Grundsatz gilt im Port weiter: `PrefsGuard.Reg`
  bewertet den Exit-Code, nicht die Anwesenheit von stderr-Ausgabe.
- **`-match` auf einem Array** filtert nur und füllt `$Matches` nicht. Erst die
  Zeile ziehen, dann einzeln matchen.
- **`ConvertFrom-Json` gibt ein JSON-Array als EIN Objekt** in die Pipeline.
  `@(...)` entpackt das nicht, sondern verpackt es zusätzlich. `[object[]]`
  castet korrekt.
- **`ConvertTo-Json` macht aus einem einelementigen Array einen Skalar.**
  `Psd1Importer.StrList` akzeptiert deshalb String **und** Array.
- **`Get-Content` erzeugt keine leere letzte Zeile** für eine Datei, die mit
  Newline endet. Alle Zähler des alten Benchs sind so gemessen, deshalb macht
  `GameLauncher.SplitLines` es genauso. Ohne das wären Zeilenzahl und
  "ignoriert" gegenüber jedem alten Report um eins verschoben.
- **`cd X && befehl` gibt es in PowerShell 5.1 nicht.** `&&` ist ein
  Parser-Fehler. Das war einer der Gründe, aus denen dieses Tool existiert.

## 12. Zwei Läufe gleichzeitig gehen nicht

Sie teilen Steam, die Server-Ports und vor allem den Prefs-Key: die Sicherung des
zweiten Laufs würde die Defaults des ersten einfangen und die anschließend als
"die getunten Werte" zurückspielen. `RunLock` (Named Mutex plus Lockfile)
verhindert das maschinenweit, weil GUI und Agent beide Läufe starten können.

## 13. Das Spiel hält Dateien offen

Die Mod-DLL ist gesperrt, solange `7DaysToDie.exe` läuft, und der Prozess ist
weg, bevor seine Handles es sind. Deshalb wartet `GameLauncher.WaitUntilGone`
auf jeden `7DaysToDie*`-Prozess und danach noch zwei Sekunden. Das Logfile wird
mit `FileShare.ReadWrite | FileShare.Delete` gelesen, weil das Spiel es während
des Laufs offen hält.

## 14. Unity redet auf stdout, auch mit `-logfile`

Die Allocator-Konfiguration und ein paar Startzeilen landen trotzdem auf stdout.
Vererbt an unsere Konsole würde das im `--json`-Modus vor dem Envelope stehen und
das Ergebnis unparsbar machen. `GameLauncher.Start` leitet daher beide Ströme um
und leert sie (ungeleerte Pipes blockieren das Spiel, sobald der Puffer voll ist).

## 15. `EvidenceOk` gegen `AtlasOk`

Der Fehler, der den alten Bench nutzlos machte: `Start-Gui.ps1` schrieb
`EvidenceOk`, `Invoke-TestMatrix.ps1` las `AtlasOk`. `GuiOk` war damit immer
`false`, und es wurde **nie** eine `TESTED_VERSIONS`-Zeile vorgeschlagen, obwohl
drei bestätigte GUI-Läufe im Store lagen. Dazu verglich die Matrix nur die
Spielversion, nicht Mod und Variante, sodass eine Adamant-Bestätigung einen
7-Dashes-Lauf grün gemacht hätte.

Lehre für den Port: ein Store, ein typisiertes Modell, geschrieben und gelesen
von derselben Stelle. `RunStore.ImportGuiVerified` akzeptiert beide
Schreibweisen, damit alte Einträge nicht verloren gehen.

## 16. Der Ordnername ist keine Versionsangabe

`E:\Games\7DTD-3.0.1` heißt nicht, dass dort 3.0.1 liegt. Ein Ordner wird
umbenannt, kopiert, oder Steam aktualisiert ihn im Bestand, und der Name bleibt
stehen. Ein Bench, der Versionen nach Ordnernamen führt, meldet dann ruhig weiter
Ergebnisse für eine Version, die nie gestartet wurde.

In einer Installation steht die Nummer nirgends als Text: `Assembly-CSharp.dll`
baut den String erst zur Laufzeit zusammen, eine Suche nach `3.0.1` über den
ganzen Ordner findet nur EAC- und Steam-Logs. Was es gibt, ist die
Identity-Version in `MicrosoftGame.Config`, die als
`1.<major><minor><patch>.<build>` mitgeliefert wird:

| Version | Identity |
|---|---|
| 3.0.0 | `1.300.259.0` |
| 3.0.1 | `1.301.4.0` |
| 3.1.0 | `1.310.14.0` |

`VersionScanner.IdFromBuild` liest ausschließlich die dreistellige Mitte. Eine
andere Form gibt `null` zurück, nicht eine plausibel aussehende falsche Antwort;
dann übernimmt der Ordnername, und die Zeile sagt, dass geraten wurde.

Deshalb gibt es drei Aussagen und einen Vergleich: die eingetragene Id, den Build
beim Eintragen (`versions[].build`), und die Zeile `INF Version:` des letzten
echten Laufs, die als einzige keine Namensverwechslung sein kann. `tb doctor`
stellt sie gegenüber. `tb versions scan` trägt einen Ordner, dessen Name seinem
Build widerspricht, nicht ohne `--force` ein.
