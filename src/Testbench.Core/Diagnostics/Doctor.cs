using Testbench.Core.Config;
using Testbench.Core.Prefs;
using Testbench.Core.Run;
using Testbench.Core.Store;

namespace Testbench.Core.Diagnostics;

public enum CheckLevel { Ok, Warn, Fail }

public sealed record Check(string Area, CheckLevel Level, string Message, string? Fix = null);

/// <summary>
/// Answers "why does it not work" before a run rather than after it.
///
/// This exists because the failure modes of the PowerShell bench were all
/// invisible from the outside: a source path that had moved, a game still
/// running, a dependency folder that a previous run had swept into
/// _Mods-deaktiviert, prefs left at a build's defaults.
/// </summary>
public static class Doctor
{
    public static List<Check> Run(MachineConfig machine, string machinePath)
    {
        var checks = new List<Check>();

        checks.Add(new Check("config", CheckLevel.Ok, $"Maschinenkonfiguration: {machinePath}"));

        // ---- paths --------------------------------------------------------
        CheckDir(checks, "gameRoot", machine.GameRoot, required: true);
        CheckDir(checks, "userDataRoot", machine.UserDataRoot, required: false);
        CheckDir(checks, "resultRoot", machine.ResultRoot, required: false);
        CheckDir(checks, "prefs.backupDir", machine.Prefs.BackupDir, required: false);

        var store = new RunStore(machine);

        // ---- versions -----------------------------------------------------
        if (machine.Versions.Count == 0)
        {
            checks.Add(new Check("versions", CheckLevel.Fail, "Keine Version eingetragen.",
                "tb versions scan --add"));
        }
        foreach (var v in machine.Versions)
        {
            var dir = machine.GameDir(v.Id);
            var exe = Path.Combine(dir, VersionScanner.ExeName);
            if (File.Exists(exe))
            {
                var harmony = Directory.Exists(Path.Combine(dir, "Mods", "0_TFP_Harmony"));
                checks.Add(harmony
                    ? new Check($"version {v.Id}", CheckLevel.Ok, dir)
                    : new Check($"version {v.Id}", CheckLevel.Warn,
                        $"{dir}: kein Mods\\0_TFP_Harmony - DLL-Mods laden dort nicht.",
                        "Installation pruefen oder 0_TFP_Harmony nachlegen."));

                CheckIdentity(checks, machine, store, v, dir);
            }
            else
            {
                checks.Add(new Check($"version {v.Id}", CheckLevel.Fail, $"Keine 7DaysToDie.exe unter {dir}",
                    v.Branch is null
                        ? "Installation besorgen (DepotDownloader) oder Version entfernen."
                        : $"DepotDownloader -app 251570 -depot 251576 -branch {v.Branch} -dir \"{dir}\""));
            }
        }

        // ---- dependency library -------------------------------------------
        foreach (var (key, dep) in machine.DependencyLibrary)
        {
            if (string.IsNullOrWhiteSpace(dep.Source))
                checks.Add(new Check($"dep {key}", CheckLevel.Warn, "Keine Quelle eingetragen."));
            else if (!Directory.Exists(dep.Source))
                checks.Add(new Check($"dep {key}", CheckLevel.Fail, $"Quelle fehlt: {dep.Source}",
                    "Pfad in dependencyLibrary korrigieren; sonst laeuft jeder Test ohne diese Abhaengigkeit."));
            else
                checks.Add(new Check($"dep {key}", CheckLevel.Ok, $"{dep.Folder} <- {dep.Source}"));
        }

        // ---- registered mods ----------------------------------------------
        var mods = ConfigStore.LoadRegisteredMods(machine, out var missingMods);
        foreach (var m in missingMods)
            checks.Add(new Check("mods", CheckLevel.Warn, $"Registrierte Mod-Konfiguration fehlt: {m}",
                "tb mods remove <pfad>"));

        foreach (var (mod, path) in mods)
        {
            foreach (var variant in mod.Variants)
            {
                var src = mod.VariantSource(variant);
                checks.Add(Directory.Exists(src)
                    ? new Check($"mod {mod.ModId}", CheckLevel.Ok, $"{variant.Name} <- {src}")
                    : new Check($"mod {mod.ModId}", CheckLevel.Fail, $"Variante '{variant.Name}': Quelle fehlt: {src}",
                        $"Pfad in {path} korrigieren."));
            }

            foreach (var key in mod.Dependencies.Where(k => !machine.DependencyLibrary.ContainsKey(k)))
                checks.Add(new Check($"mod {mod.ModId}", CheckLevel.Fail,
                    $"Abhaengigkeit '{key}' steht nicht in dependencyLibrary.", $"In {machinePath} ergaenzen."));

            foreach (var p in mod.Stage2?.EvidencePatterns ?? new List<string>())
                if (!LogAnalyzer.IsValidRegex(p))
                    checks.Add(new Check($"mod {mod.ModId}", CheckLevel.Fail,
                        $"evidencePattern ist kein gueltiger regulaerer Ausdruck: {p}"));
        }

        // ---- patterns in the machine config --------------------------------
        foreach (var (name, pattern) in new[]
                 {
                     ("readyPattern", machine.ReadyPattern),
                     ("fatalPattern", machine.FatalPattern),
                     ("xmlProblemPattern", machine.XmlProblemPattern),
                 })
        {
            if (!LogAnalyzer.IsValidRegex(pattern))
                checks.Add(new Check("patterns", CheckLevel.Fail, $"{name} ist kein gueltiger regulaerer Ausdruck."));
        }

        // The one that cost a whole test run: waiting for a marker that appears
        // seconds into startup reports green having tested nothing.
        if (machine.ReadyPattern.Contains("Telnet", StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check("patterns", CheckLevel.Fail,
                "readyPattern wartet auf Telnet. Das kommt nach ~3 s, lange vor dem XML-Laden - jeder Lauf waere falsch gruen.",
                "readyPattern auf 'INF StartGame done' zuruecksetzen."));

        // ---- environment ---------------------------------------------------
        var running = GameLauncher.RunningInstances();
        if (running.Length > 0)
        {
            checks.Add(new Check("environment", CheckLevel.Fail,
                $"7DaysToDie laeuft (PID {running[0].Id}) - kein Lauf moeglich, und die Mod-DLLs sind gesperrt.",
                "Spiel beenden."));
        }
        foreach (var p in running) p.Dispose();

        var holder = RunLock.CurrentHolder(machine.StateRoot);
        if (holder is not null)
            checks.Add(new Check("environment", CheckLevel.Warn,
                $"Ein Lauf ist aktiv: {holder.Owner} macht '{holder.What}' seit {holder.Since:HH:mm} (PID {holder.Pid})."));

        // ---- prefs ---------------------------------------------------------
        var prefsChecks = new PrefsGuard(machine.Prefs).Verify();
        foreach (var c in prefsChecks)
        {
            checks.Add(c.Ok
                ? new Check("prefs", CheckLevel.Ok, $"{c.Name} = {c.Actual}")
                : new Check("prefs", CheckLevel.Warn,
                    $"{c.Name} ist {c.Actual?.ToString() ?? "nicht lesbar"}, erwartet {c.Expected}. {c.Problem}",
                    machine.Prefs.GoldenReg is { } g ? $"reg import \"{g}\"" : null));
        }

        // ---- pending human work --------------------------------------------
        var pending = store.PendingVisual();
        if (pending.Count > 0)
            checks.Add(new Check("stage2", CheckLevel.Warn,
                $"{pending.Count} GUI-Lauf/-Laeufe warten auf die Sichtpruefung: " +
                string.Join(", ", pending.Select(p => $"{p.ModId} {p.VersionId}")),
                "tb verify --run <id> --visual ok|fail"));

        return checks;
    }

    /// <summary>
    /// Asks whether an installation is still the version it is registered as.
    /// Three independent statements exist: the id someone typed, the build in
    /// MicrosoftGame.Config, and the "INF Version:" line of the last actual run.
    /// When they disagree, every report about that version is wrong, and nothing
    /// in a log would ever say so.
    /// </summary>
    private static void CheckIdentity(List<Check> checks, MachineConfig machine, RunStore store,
        GameVersion v, string dir)
    {
        var area = $"version {v.Id}";
        var build = VersionScanner.ReadBuild(dir);

        if (v.Build is not null && build is not null && v.Build != build)
            checks.Add(new Check(area, CheckLevel.Warn,
                $"Installation hat sich geaendert: eingetragen war Build {v.Build}, dort liegt {build}. " +
                "Vermutlich hat Steam den Ordner aktualisiert.",
                "Ergebnisse fuer diese Version noch einmal fahren; Build mit 'tb versions remove/add --path' neu eintragen."));

        var decoded = build is null ? null : VersionScanner.IdFromBuild(build);
        if (decoded is not null && !string.Equals(decoded, v.Id, StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check(area, CheckLevel.Warn,
                $"Der Ordner ist als '{v.Id}' eingetragen, die Installation meldet sich als {decoded} (Build {build}).",
                "Eintrag korrigieren, sonst meldet der Report eine Version, die nie getestet wurde."));

        // What the game itself said, last time it ran. The only statement that
        // cannot be a naming mistake.
        var reported = store.All()
            .FirstOrDefault(r => string.Equals(r.VersionId, v.Id, StringComparison.OrdinalIgnoreCase)
                                 && r.Analysis.GameVersionShort.Length > 0)?.Analysis.GameVersionShort;
        if (reported is not null && !string.Equals(reported, $"V {v.Id}", StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check(area, CheckLevel.Warn,
                $"Der letzte Lauf unter '{v.Id}' hat sich als '{reported}' gemeldet.",
                "Eintrag oder Installation pruefen."));
    }

    private static void CheckDir(List<Check> checks, string area, string path, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            checks.Add(new Check(area, required ? CheckLevel.Fail : CheckLevel.Warn, "Nicht gesetzt."));
            return;
        }
        if (Directory.Exists(path)) { checks.Add(new Check(area, CheckLevel.Ok, path)); return; }

        checks.Add(required
            ? new Check(area, CheckLevel.Fail, $"Verzeichnis fehlt: {path}")
            : new Check(area, CheckLevel.Warn, $"Verzeichnis fehlt noch: {path}", "Wird beim ersten Lauf angelegt."));
    }

    public static CheckLevel Worst(IEnumerable<Check> checks) =>
        checks.Any(c => c.Level == CheckLevel.Fail) ? CheckLevel.Fail
        : checks.Any(c => c.Level == CheckLevel.Warn) ? CheckLevel.Warn
        : CheckLevel.Ok;
}
