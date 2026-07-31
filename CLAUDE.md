# CLAUDE.md

Project context and workflows live in **[AGENTS.md](AGENTS.md)** - read that first.

@AGENTS.md

## Short reminders

- Before any change to deploy, prefs, launcher or analyzer, read the matching
  paragraph in [`docs/conventions/traps.md`](docs/conventions/traps.md). It records
  what has already reported green while being wrong.
- `tb doctor` is the answer to "why does this not work", not searching through logs.
- Before a run, make sure 7DTD is **not** running: the game locks the mod DLLs, and
  two instances share the GamePrefs registry key.
- `--visual ok` is reserved for the human. As an agent always `--visual defer`.
- The expected values in `LogAnalyzerParityTests` are measured, not estimated. Do not
  "correct" them to round numbers.
- Everything in this repository is written in English, including commit messages.
  German exists in exactly one place: `lang\german.json`.
- For questions about the 7DTD engine use the `7d2d-modding` skill, never answer from
  memory.
