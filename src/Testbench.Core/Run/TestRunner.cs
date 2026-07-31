using System.Diagnostics;
using Testbench.Core.Config;
using Testbench.Core.Deploy;
using Testbench.Core.I18n;
using Testbench.Core.Model;
using Testbench.Core.Prefs;
using Testbench.Core.Store;

namespace Testbench.Core.Run;

/// <summary>How the visual question of a stage 2 run gets answered.</summary>
public enum VisualMode
{
    /// <summary>Ask through the callback. Without a callback this behaves like Defer.</summary>
    Ask,

    /// <summary>
    /// Leave the run as "visual pending". This is the mode an agent uses: it can
    /// prepare and start a GUI run but cannot answer whether something looked
    /// right, and pretending otherwise would put unverified versions on a
    /// compatibility list.
    /// </summary>
    Defer,

    /// <summary>Book it as confirmed without asking. Only for a human who already looked.</summary>
    AssumeOk,
}

public sealed class RunOptions
{
    public bool SkipDeploy { get; set; }
    public VisualMode Visual { get; set; } = VisualMode.Defer;

    /// <summary>Answers the mod's visual question: true, false, or null for undecided.</summary>
    public Func<string, bool?>? AskVisual { get; set; }

    public int? TimeoutSecondsOverride { get; set; }
    public string? ReadyPatternOverride { get; set; }

    /// <summary>Free text stored with the run, e.g. why it was done.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Called with the log path as soon as it is known, before the game starts.
    /// The GUI uses it to tail the log live; without it a 35 second headless run
    /// would look like nothing is happening.
    /// </summary>
    public Action<string>? LogPathReady { get; set; }
}

/// <summary>
/// One run, end to end: bring the installation to a defined state, protect the
/// GamePrefs, start the game, evaluate the log, store the record.
///
/// This is the single place that knows the order of those steps. In the
/// PowerShell bench the order existed twice, in Invoke-SmokeTest.ps1 and
/// Start-Gui.ps1, and the two had already drifted apart.
/// </summary>
public sealed class TestRunner
{
    private readonly MachineConfig _machine;
    private readonly RunStore _store;
    private readonly Action<string> _log;

    public TestRunner(MachineConfig machine, RunStore? store = null, Action<string>? log = null)
    {
        _machine = machine;
        _store = store ?? new RunStore(machine);
        _log = log ?? (_ => { });
    }

    public RunRecord Run(
        ModConfig mod,
        ModVariant variant,
        string versionId,
        TestStage stage,
        RunOptions? options = null,
        CancellationToken cancel = default)
    {
        var opts = options ?? new RunOptions();
        var run = new RunRecord
        {
            Id = RunStore.NewId(mod.ModId, versionId, stage),
            Started = DateTimeOffset.Now,
            ModId = mod.ModId,
            ModDisplayName = string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.ModId : mod.DisplayName,
            Variant = variant.Name,
            VersionId = versionId,
            Stage = stage,
            Note = opts.Note,
        };

        var gameDir = _machine.GameDir(versionId);
        var exe = Path.Combine(gameDir, "7DaysToDie.exe");

        if (!File.Exists(exe))
            return Finish(run, RunStatus.Missing, Loc.T("run.noInstall", gameDir));

        // Refused, not warned about: a run sweeps every mod it did not install
        // into _Mods-disabled. Pointed at the copy somebody plays, it would
        // take their modlist apart, and they would find out days later.
        if (Diagnostics.SteamLocator.IsLiveInstall(gameDir))
            return Finish(run, RunStatus.SetupError, Loc.T("run.isLiveInstall", gameDir));

        var running = GameLauncher.RunningInstances();
        if (running.Length > 0)
        {
            var pid = running[0].Id;
            foreach (var p in running) p.Dispose();
            return Finish(run, RunStatus.SetupError, Loc.T("run.alreadyRunning", pid));
        }

        // The client needs a running Steam. Headless does not, so this is only a
        // warning for the stage where it actually bites.
        if (stage == TestStage.Gui && !IsSteamRunning())
            _log(Loc.T("run.steamNotRunning"));

        var userData = Path.Combine(_machine.UserDataRoot, stage == TestStage.Gui ? $"{versionId}-gui" : versionId);
        GuardUserDataFolder(userData);

        Directory.CreateDirectory(userData);
        Directory.CreateDirectory(_machine.ResultRoot);

        var stamp = PrefsGuard.Stamp();
        var prefix = stage == TestStage.Gui ? "gui" : "smoke";
        run.LogPath = Path.Combine(_machine.ResultRoot, $"{prefix}_{versionId}_{Safe(variant.Name)}_{stamp}.log");
        opts.LogPathReady?.Invoke(run.LogPath);

        // ---- deploy -------------------------------------------------------
        try
        {
            var deployer = new ModDeployer(_machine, _log);
            if (!opts.SkipDeploy)
            {
                var deploy = deployer.Deploy(gameDir, mod, variant);
                run.ModFolder = Path.GetFileName(deploy.ModFolderPath);
                run.ModName = deploy.ModInfo.Name;
                run.ModVersion = deploy.ModInfo.Version;
                run.Dependencies = deploy.Dependencies;
                foreach (var w in deploy.Warnings) _log(Loc.T("run.warning", w));
            }
            else
            {
                // Without a deploy the installed copy is still the authority on
                // what the log will say, so it is read either way.
                var installed = Path.Combine(gameDir, "Mods",
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(mod.VariantSource(variant))));
                var info = ModInfoReader.Read(installed);
                run.ModFolder = Path.GetFileName(installed);
                run.ModName = info.Name;
                run.ModVersion = info.Version;

                foreach (var dep in deployer.ResolveDependencies(mod))
                {
                    var depDir = Path.Combine(gameDir, "Mods", dep.Folder);
                    run.Dependencies.Add(new DependencyResult
                    {
                        Key = dep.Key,
                        Folder = dep.Folder,
                        Deployed = Directory.Exists(depDir),
                        ReportedName = Directory.Exists(depDir) ? ModInfoReader.Read(depDir).Name : null,
                        Problem = Directory.Exists(depDir) ? null : Loc.T("dep.notInstalled"),
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ConfigException)
        {
            return Finish(run, RunStatus.SetupError, ex.Message);
        }

        // ---- prefs, launch, restore ---------------------------------------
        var prefs = new PrefsGuard(_machine.Prefs, _log);
        string backup;
        try
        {
            backup = prefs.Backup(prefix);
        }
        catch (IOException ex)
        {
            return Finish(run, RunStatus.SetupError, ex.Message);
        }

        var launcher = new GameLauncher(_log);
        LaunchOutcome outcome;
        var launchError = (string?)null;
        try
        {
            outcome = stage == TestStage.Gui
                ? launcher.RunGui(gameDir, exe, run.LogPath, userData, cancel)
                : launcher.RunHeadless(
                    gameDir, exe, run.LogPath, userData,
                    opts.ReadyPatternOverride ?? _machine.ReadyPatternFor(versionId),
                    FatalPatternFor(mod),
                    opts.TimeoutSecondsOverride ?? _machine.TimeoutSeconds,
                    cancel);
        }
        catch (Exception ex)
        {
            outcome = new LaunchOutcome(StopReason.Exited, null);
            launchError = Loc.T("run.launchFailed", ex.Message);
        }
        finally
        {
            // Exactly once, and always, even after a failure: the tuned live
            // settings are the thing most easily lost here and the hardest to
            // notice afterwards.
            var (restored, checks, roundTrip) = prefs.Restore(backup);
            if (!restored) _log(Loc.T("prefs.restoreFailedShort"));

            foreach (var c in checks.Where(c => !c.Ok))
                _log(Loc.T("prefs.valueOff", c.Name, c.Actual?.ToString() ?? Loc.T("prefs.unreadable"),
                    c.Expected, c.Problem ?? ""));

            if (!roundTrip.Ok)
                _log(roundTrip.Problem is not null
                    ? Loc.T("prefs.roundtrip.unknown", roundTrip.Problem)
                    : Loc.T("prefs.roundtrip.lost", roundTrip.Differing.Count,
                        string.Join(", ", roundTrip.Differing.Take(8))));
        }

        if (launchError is not null) return Finish(run, RunStatus.SetupError, launchError);

        run.StopReason = outcome.StopReason;

        // ---- evaluate ------------------------------------------------------
        var lines = GameLauncher.ReadLogLines(run.LogPath);
        if (lines.Length == 0)
            return Finish(run, RunStatus.SetupError, Loc.T("run.noLog"));

        run.Analysis = LogAnalyzer.Analyze(
            LogAnalyzer.InputFor(lines, _machine, mod, run.ModName, useStage2Filter: stage == TestStage.Gui));
        run.GameVersion = run.Analysis.GameVersion;

        // Provided is not loaded. A dependency that quietly fails to come up makes
        // any test of the integration with it worthless, so it is proven in the log.
        foreach (var dep in run.Dependencies)
        {
            if (dep.ReportedName is null)
            {
                dep.Problem ??= Loc.T("dep.notInstalled");
                continue;
            }
            dep.Loaded = run.Analysis.LoadedMods.Contains(dep.ReportedName, StringComparer.OrdinalIgnoreCase);
            if (!dep.Loaded) dep.Problem ??= Loc.T("dep.notLoaded");
        }

        var status = LogAnalyzer.Verdict(run.Analysis, run.Dependencies, mod.Stage1.RequireHarmony, run.StopReason!);

        // ---- stage 2: evidence and the human ------------------------------
        if (stage == TestStage.Gui)
        {
            var s2 = mod.Stage2 ?? new Stage2Config();
            run.EvidenceLabel = s2.EvidenceLabel;
            run.VisualQuestion = s2.VisualQuestion;

            var missing = LogAnalyzer.MissingEvidence(lines, s2.EvidencePatterns);
            if (missing is null)
            {
                // No patterns configured means there is no log-provable evidence
                // for this mod. Reported as such, not booked as passed.
                run.EvidenceOk = null;
            }
            else
            {
                run.MissingEvidence = missing;
                run.EvidenceOk = missing.Count == 0;
            }

            ApplyVisual(run, opts, s2.VisualQuestion);
        }

        return Finish(run, status, run.Note);
    }

    private void ApplyVisual(RunRecord run, RunOptions opts, string question)
    {
        switch (opts.Visual)
        {
            case VisualMode.AssumeOk:
                run.Visual = VisualState.Ok;
                run.VisualAt = DateTimeOffset.Now;
                run.VisualNote = Loc.T("visual.assumedOk");
                break;

            case VisualMode.Ask when opts.AskVisual is not null:
                var answer = opts.AskVisual(question);
                run.Visual = answer switch
                {
                    true => VisualState.Ok,
                    false => VisualState.Failed,
                    _ => VisualState.Pending,
                };
                if (answer is not null) run.VisualAt = DateTimeOffset.Now;
                break;

            default:
                run.Visual = VisualState.Pending;
                break;
        }
    }

    /// <summary>
    /// The one mistake that would cost real data: pointing the isolated user data
    /// folder at the live one. Checked again right before launch, not only when the
    /// config is loaded, because a version entry can carry its own path.
    /// </summary>
    private static void GuardUserDataFolder(string userData)
    {
        var live = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7DaysToDie");
        if (ConfigStore.PathsEqual(userData, live))
            throw new ConfigException(Loc.T("error.userDataIsLive", live));
    }

    private string FatalPatternFor(ModConfig mod)
    {
        var extra = mod.Stage1.ExtraFatalPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return extra.Count == 0
            ? _machine.FatalPattern
            : _machine.FatalPattern + "|" + string.Join("|", extra);
    }

    private static bool IsSteamRunning()
    {
        var procs = Process.GetProcessesByName("steam");
        var any = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return any;
    }

    private RunRecord Finish(RunRecord run, RunStatus status, string? note)
    {
        run.Status = status;
        run.Finished = DateTimeOffset.Now;
        if (!string.IsNullOrWhiteSpace(note)) run.Note = note;
        _store.Save(run);
        return run;
    }

    private static string Safe(string s) =>
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
}
