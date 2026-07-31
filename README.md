# 7DTD Multiversion-Testbench

Einen 7-Days-to-Die-Mod gegen mehrere Spielversionen testen, ohne die eigene
Modlist oder den Spielstand anzufassen. Ein Programm mit Fenster für Menschen und
einer Kommandozeile für Automatisierung, beide auf demselben Kern, in dreizehn
Sprachen.

> **English:** a bench that tests a 7 Days to Die mod against several game
> versions: headless smoke test plus a real GUI run, and a compatibility list
> that only names versions which actually passed both. The tool itself speaks all
> thirteen languages 7DTD ships. Full description in
> [`docs/nexus-description.md`](docs/nexus-description.md), command reference in
> [`docs/cli.md`](docs/cli.md) (German, but every command and option is English).

## Warum

Vorher waren es drei PowerShell-Skripte mit Befehlszeilen wie dieser:

    .\Start-Gui.ps1 -Version 3.0.0 -Edition Survival -ConfigPath D:\Mods\MyMod\test\Testbench.psd1

Das muss man sich merken, richtig tippen und darauf hoffen, dass es stimmt. Und
selbst dann konnte es lautlos das Falsche tun: das Matrix-Skript las `AtlasOk`,
während das GUI-Skript `EvidenceOk` schrieb, weshalb trotz bestätigter GUI-Läufe
**nie** eine Kompatibilitätsliste vorgeschlagen wurde.

Jetzt:

```bash
tb run --mod mymod --profile matrix
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

## Voraussetzungen

- Windows (die Bench liest die GamePrefs aus der Registry und startet 7DTD)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Eine oder mehrere Spielinstallationen zum Testen, **nicht** die gespielte.
  Kopien oder `DepotDownloader`-Abzüge, ein Ordner je Version.

## Einrichten

ZIP aus den [Releases](https://github.com/HannaPanda/7D2D-TestBench/releases)
entpacken, dann im entpackten Ordner:

```bash
tb init
```

Das legt `testbench.json` und die Ordner daneben an, findet deine gespielte
Installation und sagt ausdrücklich, dass die nicht eingetragen werden darf.

Spielinstallationen nach `<bench>\Games\` legen (ein Ordner je Version), dann:

```bash
tb versions scan --add
```

Mod anmelden: eine `testbench.mod.json` nach dem Muster in
[`examples/`](examples/) in `<mod-repo>\test\` legen, dann `tb mods add <pfad>`.

```bash
tb doctor
```

Sagt vor dem ersten Lauf, was noch fehlt.

Aus dem Quellcode bauen geht auch:

```bash
dotnet publish src\Testbench.Cli\Testbench.Cli.csproj -c Release -o <ziel>
```

## Eine neue Spielversion aufnehmen

Wenn die Installation schon auf der Platte liegt, muss nichts getippt werden:

```bash
tb versions scan --add
```

Er sucht unter `gameRoot` nach Installationen und liest aus jeder heraus, welche
Version sie ist. Im Fenster macht das der Knopf *verwalten* neben VERSIONEN:
*hier suchen* durchsucht den Ordner im Feld, *Ordner wählen...* nimmt eine
einzelne Installation von überall her, und wählst du versehentlich den
übergeordneten Ordner, sucht er einfach darin.

Die Version kommt dabei **nicht** aus dem Ordnernamen, sondern aus der
`MicrosoftGame.Config` der Installation. Das ist Absicht: `7DTD-3.0.1` heißt
nicht, dass dort 3.0.1 liegt, sobald Steam den Ordner einmal im Bestand
aktualisiert hat. Wo Name und Build sich widersprechen, sagt die Zeile das, und
eingetragen wird der Ordner erst nach einem Blick von dir.

Fehlt die Installation noch, legt `tb versions add 3.2.0 --branch v3.2.0` den
Ordner an und druckt den passenden `DepotDownloader`-Befehl.

## Das Fenster

```bash
Testbench.Gui.exe
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

## Sprachen

Oben rechts im Fenster, oder:

```bash
tb lang german
```

Vorgabe ist die Systemsprache, sonst Englisch. Die Kataloge heißen wie die
Sprachspalten der `Localization.csv` des Spiels und liegen als bearbeitbare
JSON-Dateien in `lang\`: eine Übersetzung verbessern heißt, dort eine Zeile zu
ändern und neu zu starten. Details und die Regeln für neue Texte in
[`docs/i18n.md`](docs/i18n.md).

## Benutzung auf der Kommandozeile

| Was | Befehl |
|---|---|
| Was ist da | `tb versions`, `tb mods`, `tb profiles --mod mymod` |
| Alle Versionen headless | `tb run --mod mymod --profile matrix` |
| Eine Version mit Fenster | `tb run --mod mymod --version 3.1.0 --stage gui` |
| Letzte Läufe | `tb status` |
| Offene Sichtprüfungen | `tb status --pending` |
| Sichtprüfung beantworten | `tb verify --run <runId> --visual ok` |
| Kompatibilitätsliste | `tb report --mod mymod --write` |
| Auffällige Logzeilen | `tb log --run <runId>` |
| Installationen suchen | `tb versions scan`, eintragen mit `--add` |
| Neue Spielversion ohne Installation | `tb versions add 3.2.0 --branch v3.2.0` |
| Sprache | `tb lang`, `tb lang <sprache>`, `tb lang <sprache> --check` |

Vollständige Optionen in [`docs/cli.md`](docs/cli.md).

## Was das Tool nicht tut

Es lädt keine Spielinstallation selbst herunter. `tb versions add` legt den Ordner
an und druckt den passenden `DepotDownloader`-Befehl; Passwort und
Steam-Guard-Code gibst du selbst ein.

Es verändert deine gespielte Installation nicht. Es findet sie sogar
ausdrücklich, um sie auszuschließen: eine Version, die dort liegt, wird von
`tb doctor` und vom Lauf selbst verweigert, weil ein Lauf jeden fremden Mod aus
dem `Mods`-Ordner räumt.

Es beantwortet keine Sichtprüfung. Ein Agent kann einen GUI-Lauf vorbereiten und
starten, aber ob etwas richtig aussieht und sich richtig anfühlt, kann kein
Logmuster ersetzen.

## Lizenz

[MIT](LICENSE). Benutzen, ändern, weitergeben, auch in etwas Eigenem und auch
kommerziell; drinbleiben muss nur der Copyright-Hinweis. Ohne Gewährleistung, was
bei einem Werkzeug, das Spielinstallationen umbaut und in der Registry
herumschreibt, kein Kleingedrucktes ist, sondern der Grund für `tb doctor`.

Übersetzungen, Fehlerberichte und Fallen, die hier noch fehlen, sind willkommen:
[Issues](https://github.com/HannaPanda/7D2D-TestBench/issues).

## Weiterlesen

- [`docs/conventions/traps.md`](docs/conventions/traps.md) - die Fallen, die dieses
  Projekt kennt. Interessant auch ohne Absicht, am Code etwas zu ändern: dort steht,
  warum der Bench Dinge tut, die auf den ersten Blick übertrieben wirken.
- [`docs/architecture/core.md`](docs/architecture/core.md)
- [`docs/architecture/config-schema.md`](docs/architecture/config-schema.md)
- [`docs/i18n.md`](docs/i18n.md)
- [`AGENTS.md`](AGENTS.md)
