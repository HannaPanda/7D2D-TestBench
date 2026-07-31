using Testbench.Core.Config;
using Testbench.Core.I18n;
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
/// _Mods-disabled, prefs left at a build's defaults.
/// </summary>
public static class Doctor
{
    public static List<Check> Run(MachineConfig machine, string machinePath)
    {
        var checks = new List<Check>();

        checks.Add(new Check("config", CheckLevel.Ok, Loc.T("doctor.configAt", machinePath)));

        // ---- paths --------------------------------------------------------
        CheckDir(checks, "gameRoot", machine.GameRoot, required: true);
        CheckDir(checks, "userDataRoot", machine.UserDataRoot, required: false);
        CheckDir(checks, "resultRoot", machine.ResultRoot, required: false);
        CheckDir(checks, "prefs.backupDir", machine.Prefs.BackupDir, required: false);

        var store = new RunStore(machine);

        // ---- versions -----------------------------------------------------
        if (machine.Versions.Count == 0)
        {
            checks.Add(new Check("versions", CheckLevel.Fail, Loc.T("doctor.noVersions"),
                Loc.T("doctor.fix.scanAdd")));
        }
        foreach (var v in machine.Versions)
        {
            var dir = machine.GameDir(v.Id);
            var exe = Path.Combine(dir, VersionScanner.ExeName);
            var area = $"version {v.Id}";

            if (File.Exists(exe))
            {
                // The one that would cost somebody their modlist rather than a test
                // run: a version entry pointing at the copy they play.
                if (SteamLocator.IsLiveInstall(dir))
                {
                    checks.Add(new Check(area, CheckLevel.Fail, Loc.T("doctor.version.isLive", dir),
                        Loc.T("doctor.fix.isLive")));
                    continue;
                }

                var harmony = Directory.Exists(Path.Combine(dir, "Mods", "0_TFP_Harmony"));
                checks.Add(harmony
                    ? new Check(area, CheckLevel.Ok, dir)
                    : new Check(area, CheckLevel.Warn, Loc.T("doctor.version.noHarmony", dir),
                        Loc.T("doctor.fix.harmony")));

                CheckIdentity(checks, store, v, dir);
            }
            else
            {
                checks.Add(new Check(area, CheckLevel.Fail, Loc.T("doctor.version.noExe", dir),
                    v.Branch is null
                        ? Loc.T("doctor.fix.getInstall")
                        : Loc.T("doctor.fix.depot", v.Branch, dir)));
            }
        }

        // ---- dependency library -------------------------------------------
        foreach (var (key, dep) in machine.DependencyLibrary)
        {
            var area = $"dep {key}";
            if (string.IsNullOrWhiteSpace(dep.Source))
                checks.Add(new Check(area, CheckLevel.Warn, Loc.T("doctor.dep.noSource")));
            else if (!Directory.Exists(dep.Source))
                checks.Add(new Check(area, CheckLevel.Fail, Loc.T("doctor.dep.sourceMissing", dep.Source),
                    Loc.T("doctor.fix.depSource")));
            else
                checks.Add(new Check(area, CheckLevel.Ok, $"{dep.Folder} <- {dep.Source}"));
        }

        // ---- registered mods ----------------------------------------------
        var mods = ConfigStore.LoadRegisteredMods(machine, out var missingMods);
        foreach (var m in missingMods)
            checks.Add(new Check("mods", CheckLevel.Warn, Loc.T("doctor.mods.configMissing", m),
                Loc.T("doctor.fix.modsRemove")));

        foreach (var (mod, path) in mods)
        {
            var area = $"mod {mod.ModId}";
            foreach (var variant in mod.Variants)
            {
                var src = mod.VariantSource(variant);
                checks.Add(Directory.Exists(src)
                    ? new Check(area, CheckLevel.Ok, $"{variant.Name} <- {src}")
                    : new Check(area, CheckLevel.Fail,
                        Loc.T("doctor.mod.variantSourceMissing", variant.Name, src),
                        Loc.T("doctor.fix.fixPathIn", path)));
            }

            foreach (var key in mod.Dependencies.Where(k => !machine.DependencyLibrary.ContainsKey(k)))
                checks.Add(new Check(area, CheckLevel.Fail,
                    Loc.T("doctor.mod.unknownDependency", key), Loc.T("doctor.fix.addToLibrary", machinePath)));

            foreach (var p in mod.Stage2?.EvidencePatterns ?? new List<string>())
                if (!LogAnalyzer.IsValidRegex(p))
                    checks.Add(new Check(area, CheckLevel.Fail, Loc.T("doctor.mod.badEvidenceRegex", p)));
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
                checks.Add(new Check("patterns", CheckLevel.Fail, Loc.T("doctor.pattern.badRegex", name)));
        }

        // The one that cost a whole test run: waiting for a marker that appears
        // seconds into startup reports green having tested nothing.
        if (machine.ReadyPattern.Contains("Telnet", StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check("patterns", CheckLevel.Fail, Loc.T("doctor.pattern.telnet"),
                Loc.T("doctor.fix.telnet")));

        // ---- environment ---------------------------------------------------
        var running = GameLauncher.RunningInstances();
        if (running.Length > 0)
        {
            checks.Add(new Check("environment", CheckLevel.Fail,
                Loc.T("doctor.env.gameRunning", running[0].Id), Loc.T("doctor.fix.closeGame")));
        }
        foreach (var p in running) p.Dispose();

        var holder = RunLock.CurrentHolder(machine.StateRoot);
        if (holder is not null)
            checks.Add(new Check("environment", CheckLevel.Warn,
                Loc.T("doctor.env.runActive", holder.Owner, holder.What, holder.Since.ToString("HH:mm"), holder.Pid)));

        // ---- prefs ---------------------------------------------------------
        var prefsChecks = new PrefsGuard(machine.Prefs).Verify();
        foreach (var c in prefsChecks)
        {
            checks.Add(c.Ok
                ? new Check("prefs", CheckLevel.Ok, $"{c.Name} = {c.Actual}")
                : new Check("prefs", CheckLevel.Warn,
                    Loc.T("doctor.prefs.valueOff", c.Name, c.Actual?.ToString() ?? Loc.T("prefs.unreadable"),
                        c.Expected, c.Problem ?? ""),
                    machine.Prefs.GoldenReg is { } g ? Loc.T("doctor.fix.regImport", g) : null));
        }

        // ---- pending human work --------------------------------------------
        var pending = store.PendingVisual();
        if (pending.Count > 0)
            checks.Add(new Check("stage2", CheckLevel.Warn,
                Loc.T("doctor.stage2.pending", pending.Count,
                    string.Join(", ", pending.Select(p => $"{p.ModId} {p.VersionId}"))),
                Loc.T("doctor.fix.verify")));

        return checks;
    }

    /// <summary>
    /// Asks whether an installation is still the version it is registered as.
    /// Three independent statements exist: the id someone typed, the build in
    /// MicrosoftGame.Config, and the "INF Version:" line of the last actual run.
    /// When they disagree, every report about that version is wrong, and nothing
    /// in a log would ever say so.
    /// </summary>
    private static void CheckIdentity(List<Check> checks, RunStore store, GameVersion v, string dir)
    {
        var area = $"version {v.Id}";
        var build = VersionScanner.ReadBuild(dir);

        if (v.Build is not null && build is not null && v.Build != build)
            checks.Add(new Check(area, CheckLevel.Warn,
                Loc.T("doctor.version.buildChanged", v.Build, build),
                Loc.T("doctor.fix.buildChanged")));

        var decoded = build is null ? null : VersionScanner.IdFromBuild(build);
        if (decoded is not null && !string.Equals(decoded, v.Id, StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check(area, CheckLevel.Warn,
                Loc.T("doctor.version.idMismatch", v.Id, decoded, build ?? ""),
                Loc.T("doctor.fix.idMismatch")));

        // What the game itself said, last time it ran. The only statement that
        // cannot be a naming mistake.
        var reported = store.All()
            .FirstOrDefault(r => string.Equals(r.VersionId, v.Id, StringComparison.OrdinalIgnoreCase)
                                 && r.Analysis.GameVersionShort.Length > 0)?.Analysis.GameVersionShort;
        if (reported is not null && !string.Equals(reported, $"V {v.Id}", StringComparison.OrdinalIgnoreCase))
            checks.Add(new Check(area, CheckLevel.Warn,
                Loc.T("doctor.version.reportedOther", v.Id, reported),
                Loc.T("doctor.fix.checkEntry")));
    }

    private static void CheckDir(List<Check> checks, string area, string path, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            checks.Add(new Check(area, required ? CheckLevel.Fail : CheckLevel.Warn, Loc.T("doctor.dirNotSet"),
                required ? Loc.T("doctor.fix.init") : null));
            return;
        }
        if (Directory.Exists(path)) { checks.Add(new Check(area, CheckLevel.Ok, path)); return; }

        checks.Add(required
            ? new Check(area, CheckLevel.Fail, Loc.T("doctor.dirMissing", path))
            : new Check(area, CheckLevel.Warn, Loc.T("doctor.dirMissingYet", path),
                Loc.T("doctor.fix.createdOnFirstRun")));
    }

    public static CheckLevel Worst(IEnumerable<Check> checks) =>
        checks.Any(c => c.Level == CheckLevel.Fail) ? CheckLevel.Fail
        : checks.Any(c => c.Level == CheckLevel.Warn) ? CheckLevel.Warn
        : CheckLevel.Ok;
}
