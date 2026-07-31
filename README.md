# 7DTD Multiversion-Testbench

Einen 7-Days-to-Die-Mod gegen mehrere Spielversionen testen, ohne die
Live-Modlist oder den Spielstand anzufassen. Ein Programm mit Fenster für
Menschen und einer Kommandozeile für Automatisierung, beide auf demselben Kern.

## Warum

Vorher waren es drei PowerShell-Skripte mit Befehlszeilen wie dieser:

    cd E:\7DTD-Testbench
    .\Start-Gui.ps1 -Version 3.0.0 -Edition Survival -ConfigPath C:\Users\sourc\7D2D-7DashesToDie\test\Testbench.SevenDashes.psd1

Das muss man sich merken, richtig tippen und darauf hoffen, dass es stimmt. Und
selbst dann konnte es lautlos das Falsche tun: `Invoke-TestMatrix.ps1` las
`AtlasOk`, während `Start-Gui.ps1` `EvidenceOk` schrieb, weshalb trotz drei
bestätigter GUI-Läufe **nie** eine Kompatibilitätsliste vorgeschlagen wurde.

Jetzt:

```bash
tb run --mod seven --profile matrix
```

## Zwei Teststufen

**Stufe 1, headless.** Dedizierter Server mit `-batchmode -nographics`, ohne
Klicken, deshalb schleifenfähig über beliebig viele Versionen. Ein Start dauert
etwa 35 Sekunden. Deckt ab: Mod-Laden, Harmony-Patches, XML-Parsing,
Localization, alle `ERR`/`EXC` beim Start.

**Stufe 2, mit Fenster.** Das Einzige, was Textur-Atlas, Menüs, Tasten und
Spielgefühl prüfen kann, weil `-nographics` nichts Grafisches ausführt. Endet,
wenn du das Fenster schließt, und fragt danach die mod-spezifische Frage.

Auf eine Kompatibilitätsliste kommt eine Version erst, wenn **beide** Stufen
bestanden sind und ein Mensch die Sichtprüfung für die **aktuelle** Mod-Version
bestätigt hat.

## Einrichten

```bash
dotnet publish src\Testbench.Cli\Testbench.Cli.csproj -c Release -o E:\7DTD-Testbench\bin
```

```bash
E:\7DTD-Testbench\bin\tb.exe init --bench-root E:\7DTD-Testbench
```

Alte Konfiguration übernehmen:

```bash
tb import --psd1 C:\Users\sourc\7D2D-7DashesToDie\test\Testbench.SevenDashes.psd1
```

```bash
tb import --gui-verified E:\7DTD-Testbench\gui-verified.json --mod adamant
```

Prüfen, ob alles steht:

```bash
tb doctor
```

## Das Fenster

```bash
E:\7DTD-Testbench\bin\Testbench.Gui.exe
```

Links wählst du Mod, Variante, Profil, Versionen und Stufen und drückst *Lauf
starten*. In der Mitte läuft das Log mit (`ERR`/`EXC` rot, `WRN` gelb,
Mod-Zeilen grün, Rauschen grau, "mitlaufen" abschaltbar). Rechts steht pro
Version eine Ergebniskachel, darunter die Kompatibilitätszeile mit *kopieren*.

Unten erscheint ein Kasten mit offenen Sichtprüfungen: jeder GUI-Lauf landet dort
mit der mod-eigenen Frage und zwei Knöpfen. Das ist bewusst so, statt ein
Modal-Fenster aufzuklappen, während das Spiel noch zugeht, und es ist die
Stelle, an der du die Läufe abschließt, die ein Agent für dich gestartet hat.

Die letzte Auswahl wird gemerkt. Schließen während eines Laufs fragt nach und
bricht dann sauber ab, damit die GamePrefs zurückgespielt werden.

## Benutzung auf der Kommandozeile

| Was | Befehl |
|---|---|
| Was ist da | `tb versions`, `tb mods`, `tb profiles --mod seven` |
| Alle Versionen headless | `tb run --mod seven --profile matrix` |
| Eine Version mit Fenster | `tb run --mod seven --version 3.1.0 --stage gui` |
| Letzte Läufe | `tb status` |
| Offene Sichtprüfungen | `tb status --pending` |
| Sichtprüfung beantworten | `tb verify --run <runId> --visual ok` |
| Kompatibilitätsliste | `tb report --mod seven --write` |
| Auffällige Logzeilen | `tb log --run <runId>` |
| Installationen suchen | `tb versions scan`, eintragen mit `--add` |
| Neue Spielversion ohne Installation | `tb versions add 3.2.0 --branch v3.2.0` |

Vollständige Optionen in [`docs/cli.md`](docs/cli.md).

## Eine neue Spielversion aufnehmen

Wenn die Installation schon auf der Platte liegt, muss nichts getippt werden:

```bash
tb versions scan --add
```

Er sucht unter `gameRoot` nach Installationen und liest aus jeder heraus, welche
Version sie ist. Im Fenster macht das der Knopf *verwalten* neben VERSIONEN:
*hier suchen* durchsucht den Ordner im Feld, *Ordner waehlen...* nimmt eine
einzelne Installation von überall her, und wählst du versehentlich den
übergeordneten Ordner, sucht er einfach darin.

Die Version kommt dabei **nicht** aus dem Ordnernamen, sondern aus der
`MicrosoftGame.Config` der Installation. Das ist Absicht: `E:\Games\7DTD-3.0.1`
heißt nicht, dass dort 3.0.1 liegt, sobald Steam den Ordner einmal im Bestand
aktualisiert hat. Wo Name und Build sich widersprechen, sagt die Zeile das, und
eingetragen wird der Ordner erst nach einem Blick von dir.

Fehlt die Installation noch, legt `tb versions add 3.2.0 --branch v3.2.0` den
Ordner an und druckt den passenden `DepotDownloader`-Befehl.

## Was das Tool nicht tut

Es lädt keine Spielinstallation selbst herunter. Passwort und Steam-Guard-Code
gibst du selbst ein.

Es verändert die Live-Installation und die MO2-Modlist nicht. Aus der Modlist wird
nur gelesen, um Gears und Quartz in die Testinstallationen zu spiegeln.

Es beantwortet keine Sichtprüfung. Ein Agent kann einen GUI-Lauf vorbereiten und
starten, aber ob etwas richtig aussieht und sich richtig anfühlt, kann kein
Logmuster ersetzen.

## Weiterlesen

- [`docs/conventions/traps.md`](docs/conventions/traps.md) - die Fallen, die dieses
  Projekt kennt. Interessant auch ohne Absicht, am Code etwas zu ändern: dort steht,
  warum der Bench Dinge tut, die auf den ersten Blick übertrieben wirken.
- [`docs/architecture/core.md`](docs/architecture/core.md)
- [`docs/architecture/config-schema.md`](docs/architecture/config-schema.md)
- [`AGENTS.md`](AGENTS.md)
