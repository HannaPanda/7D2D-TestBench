# CLAUDE.md

Projektkontext und Arbeitsabläufe stehen in **[AGENTS.md](AGENTS.md)** - zuerst lesen.

@AGENTS.md

## Kurze Erinnerungen

- Vor jeder Änderung an Deploy, Prefs, Launcher oder Analyzer den passenden
  Absatz in [`docs/conventions/traps.md`](docs/conventions/traps.md) lesen. Dort
  steht, was schon einmal falsch grün gemeldet hat.
- `tb doctor` ist die Antwort auf "warum funktioniert das nicht", nicht das
  Durchsuchen von Logs.
- Vor einem Lauf sicherstellen, dass 7DTD **nicht** läuft: das Spiel sperrt die
  Mod-DLLs, und zwei Instanzen teilen den GamePrefs-Registry-Key.
- `--visual ok` ist dem Menschen vorbehalten. Als Agent immer `--visual defer`.
- Die Erwartungswerte in `LogAnalyzerParityTests` sind gemessen, nicht geschätzt.
  Nicht auf runde Zahlen "korrigieren".
- Für Fragen zur 7DTD-Engine die `7d2d-modding`-Skill benutzen, nie aus dem
  Gedächtnis antworten.
