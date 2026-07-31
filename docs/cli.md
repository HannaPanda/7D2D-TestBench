# tb - Kommandozeile

`tb.exe` liegt nach `dotnet publish` unter `E:\7DTD-Testbench\bin\tb.exe` und
findet seine Konfiguration eine Ebene darüber.

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
tb init --bench-root E:\7DTD-Testbench
```

```bash
tb import --psd1 E:\7DTD-Testbench\Testbench.psd1 --mod-out C:\Users\sourc\7D2D-Adamant\test\testbench.mod.json
```

Trennt eine alte `.psd1` in Maschinen- und Mod-Teil, schreibt den Mod-Teil neben
den Mod und registriert ihn. Mehrere Importe verschmelzen die Maschinenteile;
Abweichungen bei einer schon bekannten Abhängigkeit werden gemeldet und **nicht**
übernommen.

```bash
tb import --gui-verified E:\7DTD-Testbench\gui-verified.json --mod adamant
```

```bash
tb doctor
```

Prüft Pfade, Versionen, Dependency-Quellen, Mod-Quellen, alle regulären Ausdrücke,
laufende Spiele, die Laufsperre, die GamePrefs gegen die Goldwerte und offene
Sichtprüfungen. Exit 2, wenn so kein Lauf stattfinden kann.

## Nachschauen

```bash
tb versions
```

```bash
tb versions add 3.2.0 --branch v3.2.0
```

Legt den Zielordner an und druckt den `DepotDownloader`-Befehl. Das Tool lädt
nichts selbst herunter: Passwort und Steam-Guard-Code gibst du selbst ein.

```bash
tb mods
```

```bash
tb profiles --mod seven
```

## Testen

```bash
tb run --mod seven --profile matrix
```

```bash
tb run --mod seven --version 3.0.1 --stage headless
```

Optionen von `run`:

| Option | Wirkung |
|---|---|
| `--mod <id>` | eindeutiges Fragment genügt (`seven`, `adamant`) |
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
tb verify --run 20260731-222549_sevendashestodie_3.0.1_gui --visual ok --note "Controller-Zeile da, Doppeltipp dasht"
```

Nur für GUI-Läufe. Ein Headless-Lauf führt nichts Grafisches aus, da gibt es
nichts, was ein Auge bestätigt haben könnte.

```bash
tb report --mod seven --write
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

## Ablauf für einen Agenten

```bash
tb doctor --json
```
```bash
tb run --mod seven --profile matrix --json
```
```bash
tb run --mod seven --version 3.1.0 --stage gui --visual defer --json
```
```bash
tb status --pending --json
```
```bash
tb report --mod seven --json
```

Der Agent kann alles außer der Sichtprüfung. Die bleibt offen, bis ein Mensch
`tb verify` sagt oder in der GUI antwortet.
