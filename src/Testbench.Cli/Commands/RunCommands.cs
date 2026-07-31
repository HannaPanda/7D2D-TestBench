using Testbench.Core.Config;
using Testbench.Core.I18n;
using Testbench.Core.Model;
using Testbench.Core.Report;
using Testbench.Core.Run;

namespace Testbench.Cli.Commands;

/// <summary>Commands that run tests or read what runs produced.</summary>
public static class RunCommands
{
    public static int Run(CommandContext ctx)
    {
        var (mod, _) = ctx.RequireMod();

        // ---- what to run ---------------------------------------------------
        var profileName = ctx.Args.Get("profile");
        var versions = ctx.Args.GetAll("version");
        var stages = ParseStages(ctx.Args.GetAll("stage"));
        var variantName = ctx.Args.Get("variant");

        if (profileName is not null)
        {
            var profile = mod.FindProfile(profileName)
                          ?? throw new ConfigException(Loc.T("cli.run.noSuchProfile", mod.ModId, profileName,
                              mod.Profiles.Count == 0 ? Loc.T("cli.none") : string.Join(", ", mod.Profiles.Select(p => p.Name))));

            // Explicit arguments still win, so a profile can be used as a starting
            // point without editing it.
            if (versions.Count == 0) versions = profile.Versions.ToList();
            if (stages.Count == 0) stages = profile.Stages.ToList();
            variantName ??= profile.Variant;
        }

        if (versions.Count == 0) versions = ctx.Machine.Versions.Select(v => v.Id).ToList();
        if (stages.Count == 0) stages = new List<TestStage> { TestStage.Headless };
        if (versions.Count == 0) throw new ConfigException(Loc.T("cli.run.noVersions"));

        var variant = mod.FindVariant(variantName)
                      ?? throw new ConfigException(Loc.T("cli.run.noSuchVariant", mod.ModId, variantName ?? "",
                          string.Join(", ", mod.Variants.Select(v => v.Name))));

        var opts = new RunOptions
        {
            SkipDeploy = ctx.Args.Flag("skip-deploy"),
            Visual = ParseVisual(ctx.Args.Get("visual"), ctx.Out.IsJson),
            TimeoutSecondsOverride = ctx.Args.GetInt("timeout"),
            ReadyPatternOverride = ctx.Args.Get("ready-pattern"),
            Note = ctx.Args.Get("note"),
        };

        if (opts.Visual == VisualMode.Ask)
        {
            opts.AskVisual = question =>
            {
                Console.WriteLine();
                Console.Write($"{question} [{Loc.T("cli.yesKey")}/{Loc.T("cli.noKey")}] ");
                var answer = (Console.ReadLine() ?? "").TrimStart();
                // The localized yes-key and a plain "y" both count: a German
                // console user types j, and everybody types y sooner or later.
                return answer.StartsWith(Loc.T("cli.yesKey"), StringComparison.OrdinalIgnoreCase) ||
                       answer.StartsWith("y", StringComparison.OrdinalIgnoreCase);
            };
        }

        // ---- one run at a time, machine-wide -------------------------------
        var what = $"{mod.ModId} {string.Join(",", versions)} {string.Join("+", stages)}";
        using var runLock = RunLock.TryAcquire(ctx.Machine.StateRoot, ctx.Out.IsJson ? "tb (headless)" : "tb", what, out var holder);
        if (runLock is null)
        {
            var msg = holder is null
                ? Loc.T("cli.run.lockedUnknown")
                : Loc.T("cli.run.lockedBy", holder.Owner, holder.Since.ToString("HH:mm"), holder.What, holder.Pid);
            ctx.Out.Bad(Loc.T("cli.run.blocked", msg));
            return ctx.Out.Finish("run", ExitCodes.Blocked, new { blocked = true, holder });
        }

        // ---- go ------------------------------------------------------------
        var runner = new TestRunner(ctx.Machine, ctx.Store, ctx.Out.Info);
        var results = new List<RunRecord>();

        foreach (var version in versions)
        {
            foreach (var stage in stages)
            {
                if (!ctx.Out.IsJson)
                {
                    Console.WriteLine();
                    ctx.Out.Info($"=== {mod.ModId} / {variant.Name} / {version} / {stage.ToString().ToLowerInvariant()} ===");
                    if (stage == TestStage.Gui)
                        ctx.Out.Info(Loc.T("cli.run.guiHint"));
                }

                var run = runner.Run(mod, variant, version, stage, opts);
                results.Add(run);
                PrintRun(ctx, run);
            }
        }

        // A single failed version fails the whole invocation: an agent should not
        // have to add up the rows itself to find out whether anything is wrong.
        var worst = results.Any(r => r.Status is RunStatus.SetupError or RunStatus.Missing)
            ? ExitCodes.SetupError
            : results.All(r => r.Status == RunStatus.Ok)
                ? ExitCodes.Ok
                : ExitCodes.TestFailed;

        var pending = results.Where(r => r.Visual == VisualState.Pending).ToList();
        if (pending.Count > 0 && !ctx.Out.IsJson)
        {
            Console.WriteLine();
            ctx.Out.Warn(Loc.T("cli.run.pendingVisual", pending.Count));
            foreach (var p in pending)
                ctx.Out.Info($"  tb verify --run {p.Id} --visual ok      ({p.VisualQuestion})");
        }

        return ctx.Out.Finish("run", worst, new { runs = results.Select(Describe) });
    }

    public static int Status(CommandContext ctx)
    {
        var store = ctx.Store;
        var limit = ctx.Args.GetInt("limit") ?? 15;

        var runs = ctx.Args.Get("mod") is { } modId ? store.ForMod(modId) : store.All();
        if (ctx.Args.Flag("pending")) runs = runs.Where(r => r.Visual == VisualState.Pending).ToList();
        var shown = runs.Take(limit).ToList();

        var rows = shown.Select(r => new[]
        {
            r.Started.ToString("MM-dd HH:mm"),
            r.ModId,
            r.Variant,
            r.VersionId,
            r.Stage.ToString().ToLowerInvariant(),
            r.StatusText,
            VisualText(r),
            r.Id,
        }).ToList();
        ctx.Out.Table(rows, Loc.T("col.when"), Loc.T("col.mod"), Loc.T("col.variant"), Loc.T("col.version"),
            Loc.T("col.stage"), Loc.T("col.status"), Loc.T("col.visual"), "runId");

        var holder = RunLock.CurrentHolder(ctx.Machine.StateRoot);
        if (holder is not null)
            ctx.Out.Warn(Loc.T("cli.status.active", holder.Owner, holder.What, holder.Since.ToString("HH:mm"), holder.Pid));

        var pending = store.PendingVisual();
        if (pending.Count > 0 && !ctx.Args.Flag("pending"))
            ctx.Out.Warn(Loc.T("cli.status.pendingHint", pending.Count));

        if (rows.Count == 0) ctx.Out.Info(Loc.T("cli.status.noRuns"));

        return ctx.Out.Finish("status", ExitCodes.Ok, new
        {
            active = holder,
            pendingVisual = pending.Count,
            runs = shown.Select(Describe),
        });
    }

    /// <summary>
    /// Records the answer to a visual check afterwards. This is what makes an
    /// agent-started GUI run finishable by a human: the agent can start it and
    /// leave the verdict open, and only a person can close it.
    /// </summary>
    public static int Verify(CommandContext ctx)
    {
        var runId = ctx.Args.Require("run");
        var store = ctx.Store;
        var run = store.Load(runId) ?? throw new ConfigException(Loc.T("cli.noSuchRun", runId));

        var visual = (ctx.Args.Get("visual") ?? "").ToLowerInvariant();
        var state = visual switch
        {
            "ok" or "ja" or "j" or "yes" or "true" or "pass" => VisualState.Ok,
            "fail" or "nein" or "n" or "no" or "false" => VisualState.Failed,
            "" => throw new UsageException(Loc.T("cli.verify.visualMissing")),
            _ => throw new UsageException(Loc.T("cli.verify.visualUnknown", visual)),
        };

        if (run.Stage != TestStage.Gui)
        {
            // Headless executes nothing graphical and touches no menu or key, so
            // there is nothing an eye could have confirmed.
            ctx.Out.Bad(Loc.T("cli.verify.headlessHasNoVisual", runId));
            return ctx.Out.Finish("verify", ExitCodes.SetupError, new { runId, stage = run.Stage.ToString().ToLowerInvariant() });
        }

        run.Visual = state;
        run.VisualAt = DateTimeOffset.Now;
        if (ctx.Args.Get("note") is { } note) run.VisualNote = note;
        store.Save(run);

        ctx.Out.Good(Loc.T("cli.verify.recorded", runId,
            Loc.T(state == VisualState.Ok ? "visual.passed" : "visual.failed")));
        if (state == VisualState.Ok && run.EvidenceOk == false)
            ctx.Out.Warn(Loc.T("cli.verify.evidenceStillMissing",
                run.EvidenceLabel ?? Loc.T("report.evidenceFallback"), string.Join(", ", run.MissingEvidence)));

        return ctx.Out.Finish("verify", ExitCodes.Ok, new { runId, visual = state.ToString().ToLowerInvariant(), run = Describe(run) });
    }

    public static int Report(CommandContext ctx)
    {
        var (mod, _) = ctx.RequireMod();
        var variant = mod.FindVariant(ctx.Args.Get("variant"))
                      ?? throw new ConfigException(Loc.T("cli.run.noSuchVariant", mod.ModId,
                          ctx.Args.Get("variant") ?? "", string.Join(", ", mod.Variants.Select(v => v.Name))));

        // The current mod version comes from the source, not from a run: a release
        // that has not been tested yet must show up as untested, not inherit the
        // last run's confirmation.
        var modVersion = Core.Deploy.ModInfoReader.Read(mod.VariantSource(variant)).Version;

        var versions = ctx.Args.GetAll("version");
        if (versions.Count == 0)
        {
            var profile = mod.Profiles.FirstOrDefault(p => p.Stages.Contains(TestStage.Headless));
            versions = profile is { Versions.Count: > 0 } ? profile.Versions : ctx.Machine.Versions.Select(v => v.Id).ToList();
        }

        var report = ReportBuilder.Build(mod, variant.Name, modVersion, ctx.Store, versions);

        var rows = report.Rows.Select(r => new[]
        {
            r.VersionId,
            r.Headless?.GameVersion ?? "",
            r.Headless?.StatusText ?? RunStatusText.Of(RunStatus.Untested),
            r.Headless is null ? "" : $"{r.Headless.Analysis.Errors}/{r.Headless.Analysis.Exceptions}/{r.Headless.Analysis.XmlProblems}",
            r.GuiOk ? RunStatusText.Of(RunStatus.Ok) : r.GuiNote,
        }).ToList();
        ctx.Out.Table(rows, Loc.T("col.version"), Loc.T("col.reported"), Loc.T("col.headless"), "ERR/EXC/XML", "GUI");

        if (!ctx.Out.IsJson) Console.WriteLine();
        if (report.TestedVersions.Length > 0)
        {
            ctx.Out.Good($"TESTED_VERSIONS: \"{report.TestedVersions}\"");
        }
        else
        {
            ctx.Out.Warn(Loc.T("cli.report.nothingPassed"));
            foreach (var r in report.PartialOnly)
                ctx.Out.Info("  " + Loc.T("cli.report.stage1Only", r.VersionId, r.GuiNote));
        }

        string? written = null;
        if (ctx.Args.Flag("write"))
        {
            written = ReportBuilder.Write(report, ctx.Machine);
            ctx.Out.Info(Loc.T("cli.report.written", written));
        }

        return ctx.Out.Finish("report", ExitCodes.Ok, new
        {
            modId = mod.ModId,
            variant = variant.Name,
            modVersion,
            testedVersions = report.TestedVersions,
            reportPath = written,
            rows = report.Rows.Select(r => new
            {
                version = r.VersionId,
                headlessStatus = r.Headless?.StatusText ?? RunStatusText.Of(RunStatus.Untested),
                headlessRunId = r.Headless?.Id,
                gameVersion = r.Headless?.GameVersion,
                guiOk = r.GuiOk,
                guiNote = r.GuiNote,
                guiRunId = r.Gui?.Id,
                fullPass = r.FullPass,
            }),
        });
    }

    /// <summary>
    /// Reads a stored run's log without anyone having to find the file. Defaults to
    /// the interesting lines, which is what a diagnosis actually needs.
    /// </summary>
    public static int Log(CommandContext ctx)
    {
        var runId = ctx.Args.Require("run");
        var run = ctx.Store.Load(runId) ?? throw new ConfigException($"Kein Lauf '{runId}'. tb status");

        if (!File.Exists(run.LogPath))
        {
            ctx.Out.Bad(Loc.T("cli.log.missing", run.LogPath));
            return ctx.Out.Finish("log", ExitCodes.SetupError, new { runId, logPath = run.LogPath });
        }

        var lines = GameLauncher.ReadLogLines(run.LogPath);
        var wantAll = ctx.Args.Has("lines") && !ctx.Args.Flag("highlights");
        var take = ctx.Args.GetInt("lines") ?? 40;

        List<string> selected;
        if (wantAll)
        {
            selected = lines.TakeLast(take).ToList();
        }
        else
        {
            selected = run.Analysis.Highlights.Count > 0
                ? run.Analysis.Highlights.Take(take).ToList()
                : lines.Where(l => l.Contains(" ERR ") || l.Contains(" EXC ")).Take(take).ToList();
        }

        foreach (var l in selected) ctx.Out.Info(l);
        if (selected.Count == 0) ctx.Out.Good(Loc.T("cli.log.nothingNotable"));

        return ctx.Out.Finish("log", ExitCodes.Ok, new
        {
            runId,
            logPath = run.LogPath,
            totalLines = lines.Length,
            lines = selected,
        });
    }

    // ---- helpers ---------------------------------------------------------

    private static void PrintRun(CommandContext ctx, RunRecord run)
    {
        if (ctx.Out.IsJson) return;

        var a = run.Analysis;
        var line = $"{run.StatusText}  ({Loc.T("cli.run.stopReason", run.StopReason ?? "-")})";
        if (run.Status == RunStatus.Ok) ctx.Out.Good(line); else ctx.Out.Bad(line);

        if (run.Note is not null) ctx.Out.Info("  " + Loc.T("cli.run.note", run.Note));
        if (run.Status is RunStatus.Missing or RunStatus.SetupError) return;

        ctx.Out.Info("  " + Loc.T("cli.run.gameVersion", a.GameVersion.Length > 0 ? a.GameVersion : Loc.T("cli.unknown")));
        ctx.Out.Info("  " + Loc.T("cli.run.modLine", run.ModName, run.ModVersion,
            Loc.T(a.ModLoaded ? "cli.run.loaded" : "cli.run.notLoaded"),
            Loc.T(a.HarmonyApplied ? "report.yes" : "report.no")));
        ctx.Out.Info("  " + Loc.T("cli.run.counters", a.Errors, a.Exceptions, a.XmlProblems, a.Ignored, a.TotalLines));

        foreach (var d in run.Dependencies)
        {
            var text = "  " + Loc.T("cli.run.dependency", d.Folder, d.ReportedName ?? "?",
                d.Problem ?? Loc.T("cli.run.loaded"));
            if (d.Problem is null) ctx.Out.Detail(text); else ctx.Out.Bad(text);
        }

        if (run.Stage == TestStage.Gui)
        {
            if (run.EvidenceOk is null)
                ctx.Out.Warn("  " + Loc.T("cli.run.evidenceNoPattern", run.EvidenceLabel ?? Loc.T("report.evidenceFallback")));
            else if (run.EvidenceOk == true)
                ctx.Out.Good("  " + Loc.T("cli.run.evidenceYes", run.EvidenceLabel ?? Loc.T("report.evidenceFallback")));
            else
                ctx.Out.Bad("  " + Loc.T("cli.run.evidenceNo", run.EvidenceLabel ?? Loc.T("report.evidenceFallback"),
                    string.Join(", ", run.MissingEvidence)));
        }

        foreach (var h in a.Highlights.Take(10)) ctx.Out.Detail("  | " + h);
        ctx.Out.Detail($"  {Loc.T("col.log")}: {run.LogPath}");
        ctx.Out.Detail($"  runId: {run.Id}");
    }

    private static object Describe(RunRecord r) => new
    {
        runId = r.Id,
        started = r.Started,
        finished = r.Finished,
        modId = r.ModId,
        variant = r.Variant,
        modName = r.ModName,
        modVersion = r.ModVersion,
        version = r.VersionId,
        gameVersion = r.GameVersion,
        stage = r.Stage.ToString().ToLowerInvariant(),
        status = r.StatusText,
        statusCode = r.Status.ToString(),
        stopReason = r.StopReason,
        note = r.Note,
        modLoaded = r.Analysis.ModLoaded,
        harmony = r.Analysis.HarmonyApplied,
        errors = r.Analysis.Errors,
        exceptions = r.Analysis.Exceptions,
        xmlProblems = r.Analysis.XmlProblems,
        ignored = r.Analysis.Ignored,
        totalLines = r.Analysis.TotalLines,
        highlights = r.Analysis.Highlights,
        dependencies = r.Dependencies.Select(d => new { d.Key, d.Folder, reportedName = d.ReportedName, d.Deployed, d.Loaded, d.Problem }),
        evidenceOk = r.EvidenceOk,
        evidenceLabel = r.EvidenceLabel,
        missingEvidence = r.MissingEvidence,
        visual = r.Visual.ToString().ToLowerInvariant(),
        visualQuestion = r.VisualQuestion,
        visualNote = r.VisualNote,
        fullyVerified = r.FullyVerified,
        logPath = r.LogPath,
    };

    private static string VisualText(RunRecord r) => r.Visual switch
    {
        VisualState.Ok => Loc.T("visual.short.ok"),
        VisualState.Failed => Loc.T("visual.short.failed"),
        VisualState.Pending => Loc.T("visual.short.pending"),
        _ => "-",
    };

    private static List<TestStage> ParseStages(List<string> raw)
    {
        var stages = new List<TestStage>();
        foreach (var s in raw)
        {
            stages.Add(s.ToLowerInvariant() switch
            {
                "headless" or "smoke" or "1" or "stage1" => TestStage.Headless,
                "gui" or "2" or "stage2" => TestStage.Gui,
                _ => throw new UsageException(Loc.T("cli.stageUnknown", s)),
            });
        }
        return stages.Distinct().ToList();
    }

    private static VisualMode ParseVisual(string? raw, bool json)
    {
        if (raw is null)
        {
            // In JSON mode there is nobody at a console to answer, so the run has
            // to stay open rather than be booked as confirmed.
            return json ? VisualMode.Defer : VisualMode.Ask;
        }

        return raw.ToLowerInvariant() switch
        {
            "ask" or "frag" => VisualMode.Ask,
            "defer" or "offen" or "later" => VisualMode.Defer,
            "ok" or "assume-ok" or "confirm" => VisualMode.AssumeOk,
            _ => throw new UsageException(Loc.T("cli.visualModeUnknown", raw)),
        };
    }
}
