# tb - Kommandozeile

`tb.exe` und `testbench.json` liegen im selben Ordner. Gesucht wird die
Konfiguration neben der Exe und eine Ebene darüber, damit auch die Aufteilung
`<bench>\bin\tb.exe` neben `<bench>\testbench.json` funktioniert.

## Exit-Codes

Teil des Kontrakts. Ein Agent entscheidet daran, was als nächstes passiert.

| Code | Bedeutung |
|---|---|
| 0 | in Ordnung |
| 1 | Test durchgefallen (das Werkzeug hat funktioniert) |
| 2 | Konfigurations- oder Umgebungsfehler, es wurde nichts getestet |
| 3 | absichtlich verweigert: ein anderer Lauf hält die Sperre |

Ein einziger durchgefallener Lauf lässt den ganzen Aufruf durchfallen. Niemand
soll Zeilen zusammenzählen müssen, um zu erfahren, ob etwas kaputt ist.

## --json

Genau ein JSON-Objekt auf stdout, sonst nichts. Fortschritts- und Warnmeldungen
landen im Feld `messages`, das Ergebnis in `data`.

```json
{ "ok": true, "command": "run", "exitCode": 0, "messages": ["..."], "data": { "runs": [ ... ] } }
```

Die Konsolenausgabe des Spiels wird abgefangen und verworfen, sonst stünde Unitys
Allocator-Konfiguration vor dem Envelope.

Im JSON-Modus ist `--visual` standardmäßig `defer`: es sitzt niemand an einer
Konsole, der die Frage beantworten könnte, und ein unbeantworteter Lauf darf nicht
als bestätigt gelten.

## Einrichten

```bash
tb init
```

Legt `testbench.json` und die Ordner darunter an, alles abgeleitet vom Ort der
Exe. `--bench-root <pfad>` setzt einen anderen Ort, `--game-root <pfad>` weist auf
schon vorhandene Installationen, `--lang <sprache>` legt die Sprache fest.

```bash
tb import --psd1 D:\7DTD-Bench\Testbench.psd1 --mod-out D:\Mods\MyMod\test\testbench.mod.json
```

Trennt eine alte `.psd1` in Maschinen- und Mod-Teil, schreibt den Mod-Teil neben
den Mod und registriert ihn. Mehrere Importe verschmelzen die Maschinenteile;
Abweichungen bei einer schon bekannten Abhängigkeit werden gemeldet und **nicht**
übernommen.

```bash
tb import --gui-verified D:\7DTD-Bench\gui-verified.json --mod mymod
```

```bash
tb doctor
```

Prüft Pfade, Versionen, Dependency-Quellen, Mod-Quellen, alle regulären Ausdrücke,
laufende Spiele, die Laufsperre, die GamePrefs gegen die Goldwerte und offene
Sichtprüfungen. Exit 2, wenn so kein Lauf stattfinden kann.

Für jede Version vergleicht er außerdem drei voneinander unabhängige Aussagen:
die eingetragene Id, den Build in `MicrosoftGame.Config` und die Zeile
`INF Version:` des letzten echten Laufs. Widersprechen sie sich, ist jeder Report
über diese Version falsch, und kein Log würde das je sagen.

## Nachschauen

```bash
tb versions
```

Spalte *Build* ist die Identity-Version aus `MicrosoftGame.Config` der
Installation. Status `GEAENDERT` heißt: dort liegt ein anderer Build als beim
Eintragen, der Ordnername stimmt also nicht mehr.

### Versionen finden statt eintippen

```bash
tb versions scan
```

Sucht unter `gameRoot` (oder `--root <ordner>`, Standardtiefe 2, `--depth n`)
nach Installationen und sagt für jede, welche Version sie ist und woher er das
weiß. Er steigt nicht in eine gefundene Installation hinein.

```bash
tb versions scan --add
```

Trägt alles ein, worüber es keinen Zweifel gibt, und notiert für schon
eingetragene Versionen den Build nach. Ein Ordner, dessen Name seinem Build
widerspricht, wird **nicht** eingetragen: das ist der Fall, in dem ein Report
hinterher eine Version behauptet, die nie getestet wurde. Mit `--force` trotzdem.

```bash
tb versions add --path "D:\7DTD-Bench\Games\7DTD-3.2.0"
```

Liest die Version aus der Installation. Mit `tb versions add 3.2.0 --path <ordner>`
gibst du sie selbst vor, mit `tb versions add 3.2.0 --branch v3.2.0` ohne Ordner
legt er ihn an und druckt den `DepotDownloader`-Befehl. Das Tool lädt nichts
selbst herunter: Passwort und Steam-Guard-Code gibst du selbst ein.

**Woher die Version kommt.** In einer Installation steht die Versionsnummer
nirgends als Text; `Assembly-CSharp.dll` baut den String erst zur Laufzeit
zusammen. Verwertbar ist die Identity-Version in `MicrosoftGame.Config`: 3.0.1
liefert `1.301.4.0`, 3.1.0 liefert `1.310.14.0`. Nur diese dreistellige Form wird
gelesen, alles andere fällt auf den Ordnernamen zurück. Die letzte Instanz bleibt
die Zeile `INF Version:` eines echten Laufs, und genau die vergleicht
`tb doctor` gegen den Eintrag.

```bash
tb mods
```

```bash
tb profiles --mod mymod
```

## Testen

```bash
tb run --mod mymod --profile matrix
```

```bash
tb run --mod mymod --version 3.0.1 --stage headless
```

Optionen von `run`:

| Option | Wirkung |
|---|---|
| `--mod <id>` | eindeutiges Fragment der modId genügt |
| `--profile <name>` | benannte Kombination; explizite Argumente gewinnen |
| `--version <v>` | mehrfach oder kommagetrennt; ohne Angabe alle bekannten |
| `--variant <name>` | ohne Angabe die erste Variante des Mods |
| `--stage headless\|gui` | mehrfach möglich, Reihenfolge wie angegeben |
| `--visual ask\|defer\|ok` | Standard: `ask` am Terminal, `defer` mit `--json` |
| `--skip-deploy` | nimmt, was in der Installation liegt |
| `--timeout <s>`, `--ready-pattern <regex>` | überschreiben die Konfiguration |
| `--note <text>` | wird im Run-Record gespeichert |

Ein GUI-Lauf endet, wenn das Fenster geschlossen wird.

## Auswerten

```bash
tb status --pending
```

```bash
tb verify --run 20260731-222549_mymod_3.0.1_gui --visual ok --note "Controller-Zeile da, Doppeltipp dasht"
```

Nur für GUI-Läufe. Ein Headless-Lauf führt nichts Grafisches aus, da gibt es
nichts, was ein Auge bestätigt haben könnte.

```bash
tb report --mod mymod --write
```

Zeigt die Matrix und die `TESTED_VERSIONS`-Zeile, mit `--write` zusätzlich als
Markdown unter `resultRoot`. Eine Version kommt nur auf die Liste, wenn Stufe 1
`OK` ist **und** ein GUI-Lauf **für die aktuelle Mod-Version** die Sichtprüfung
bestätigt hat.

```bash
tb log --run <runId>
```

Ohne weitere Angabe die auffälligen Zeilen, mit `--lines <n>` die letzten n
Zeilen des Logs.

## Sprache

```bash
tb lang
```

Zeigt jede vorhandene Sprache, welche aktiv ist, welche die Systemsprache wäre und
wie viele Schlüssel einer Übersetzung fehlen.

```bash
tb lang german
```

Setzt sie und schreibt sie in die `testbench.json`. `tb lang <sprache> --check`
nennt jeden fehlenden Schlüssel. `--lang <sprache>` gilt nur für einen Aufruf.
Hintergrund in [i18n.md](i18n.md).

## Ablauf für einen Agenten

```bash
tb doctor --json
```
```bash
tb run --mod mymod --profile matrix --json
```
```bash
tb run --mod mymod --version 3.1.0 --stage gui --visual defer --json
```
```bash
tb status --pending --json
```
```bash
tb report --mod mymod --json
```

Der Agent kann alles außer der Sichtprüfung. Die bleibt offen, bis ein Mensch
`tb verify` sagt oder in der GUI antwortet.
