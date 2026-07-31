using Testbench.Core.Config;
using Testbench.Core.Diagnostics;
using Testbench.Core.I18n;

namespace Testbench.Cli.Commands;

/// <summary>Commands that read or change configuration, plus doctor.</summary>
public static class ConfigCommands
{
    public static int Init(CommandContext ctx)
    {
        var path = ctx.MachinePath;
        if (File.Exists(path) && !ctx.Args.Flag("force"))
        {
            ctx.Out.Warn(Loc.T("cli.init.exists", path));
            return ctx.Out.Finish("init", ExitCodes.SetupError, new { path, existed = true });
        }

        var benchRoot = ctx.Args.Get("bench-root")
                        ?? Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? Directory.GetCurrentDirectory();

        // Everything under one folder, derived from where the tool was unpacked.
        // Nothing here may assume a drive letter or a user name: this is the first
        // thing a stranger runs.
        var cfg = MachineConfig.ForBenchRoot(benchRoot, ctx.Args.Get("game-root"));
        if (ctx.Args.Get("user-data-root") is { } udr) cfg.UserDataRoot = udr;
        if (ctx.Args.Get("lang") is { } lang) cfg.Language = Loc.Resolve(lang);

        Directory.CreateDirectory(cfg.GameRoot);
        ConfigStore.SaveMachine(cfg, path);

        ctx.Out.Good(Loc.T("cli.init.created", path));
        ctx.Out.Detail($"gameRoot     {cfg.GameRoot}");
        ctx.Out.Detail($"userDataRoot {cfg.UserDataRoot}");

        // Where the played installation is, so it can be kept out of this. A test
        // run sweeps foreign mods aside; pointed at that folder it would take
        // somebody's modlist apart.
        var live = SteamLocator.LiveInstall();
        if (live is not null)
        {
            ctx.Out.Info(Loc.T("cli.init.steamFound", live));
            ctx.Out.Warn(Loc.T("cli.init.steamWarn"));
        }

        ctx.Out.Info(Loc.T("cli.init.next", cfg.GameRoot));
        return ctx.Out.Finish("init", ExitCodes.Ok, new
        {
            path,
            gameRoot = cfg.GameRoot,
            userDataRoot = cfg.UserDataRoot,
            liveInstall = live,
        });
    }

    /// <summary>
    /// Takes an old .psd1 over. Splits it into the machine part (merged into an
    /// existing testbench.json) and the mod part, which is written next to the
    /// .psd1 as testbench.mod.json and registered.
    /// </summary>
    public static int Import(CommandContext ctx)
    {
        if (ctx.Args.Get("gui-verified") is { } guiStore) return ImportGuiVerified(ctx, guiStore);

        var psd1 = Path.GetFullPath(ctx.Args.Require("psd1"));

        MachineConfig? existing = null;
        if (File.Exists(ctx.MachinePath)) existing = ConfigStore.LoadMachine(ctx.MachinePath);

        var imported = Psd1Importer.Import(psd1, existing);

        var modOut = ctx.Args.Get("mod-out")
                     ?? Path.Combine(Path.GetDirectoryName(psd1)!, ConfigStore.ModFileName);
        modOut = Path.GetFullPath(modOut);

        if (File.Exists(modOut) && !ctx.Args.Flag("force"))
        {
            ctx.Out.Warn(Loc.T("cli.init.exists", modOut));
            return ctx.Out.Finish("import", ExitCodes.SetupError, new { modOut, existed = true });
        }

        ConfigStore.SaveMod(imported.Mod, modOut);

        var machine = imported.Machine;
        var registered = machine.ModConfigs.Any(p => ConfigStore.PathsEqual(p, modOut));
        if (!registered) machine.ModConfigs.Add(modOut);
        ConfigStore.SaveMachine(machine, ctx.MachinePath);

        ctx.Out.Good(Loc.T("cli.import.machineConfig", ctx.MachinePath));
        ctx.Out.Good(Loc.T("cli.import.modConfig", modOut));
        ctx.Out.Info(Loc.T("cli.import.modLine", imported.Mod.ModId,
            string.Join(", ", imported.Mod.Variants.Select(v => v.Name))));
        ctx.Out.Info(Loc.T("cli.import.dependencies", imported.Mod.Dependencies.Count == 0
            ? Loc.T("cli.none")
            : string.Join(", ", imported.Mod.Dependencies)));
        ctx.Out.Info(Loc.T("cli.import.profiles", string.Join(", ", imported.Mod.Profiles.Select(p => p.Name))));
        foreach (var note in imported.Notes) ctx.Out.Warn(note);

        return ctx.Out.Finish("import", ExitCodes.Ok, new
        {
            machinePath = ctx.MachinePath,
            modPath = modOut,
            modId = imported.Mod.ModId,
            variants = imported.Mod.Variants.Select(v => v.Name),
            dependencies = imported.Mod.Dependencies,
            profiles = imported.Mod.Profiles.Select(p => p.Name),
            notes = imported.Notes,
        });
    }

    /// <summary>
    /// Takes gui-verified.json over into the run store, so confirmations already
    /// given by a human are not lost. Which mod they belong to has to be said: the
    /// old file only recorded the folder name.
    /// </summary>
    private static int ImportGuiVerified(CommandContext ctx, string path)
    {
        var (mod, _) = ctx.RequireMod();
        var full = Path.GetFullPath(path);
        var runs = ctx.Store.ImportGuiVerified(full, mod.ModId);

        foreach (var r in runs)
            ctx.Out.Good(Loc.T("cli.importGui.line", r.VersionId, r.Variant, r.ModVersion,
                r.Visual.ToString(), r.EvidenceOk?.ToString() ?? "-", r.Id));

        if (runs.Count == 0) ctx.Out.Warn(Loc.T("cli.importGui.nothing", full));
        else ctx.Out.Info(Loc.T("cli.importGui.count", runs.Count, mod.ModId));

        return ctx.Out.Finish("import.guiVerified", ExitCodes.Ok, new
        {
            source = full,
            modId = mod.ModId,
            imported = runs.Select(r => new { r.Id, r.VersionId, r.Variant, r.ModVersion, visual = r.Visual.ToString() }),
        });
    }

    public static int Doctor(CommandContext ctx)
    {
        var checks = Core.Diagnostics.Doctor.Run(ctx.Machine, ctx.MachinePath);
        var worst = Core.Diagnostics.Doctor.Worst(checks);

        foreach (var c in checks)
        {
            var line = $"[{c.Area}] {c.Message}";
            switch (c.Level)
            {
                case CheckLevel.Ok: ctx.Out.Detail("  ok   " + line); break;
                case CheckLevel.Warn: ctx.Out.Warn("  warn " + line); break;
                default: ctx.Out.Bad("  FAIL " + line); break;
            }
            if (c.Fix is not null && c.Level != CheckLevel.Ok) ctx.Out.Info($"         -> {c.Fix}");
        }

        var code = worst == CheckLevel.Fail ? ExitCodes.SetupError : ExitCodes.Ok;
        if (!ctx.Out.IsJson)
        {
            Console.WriteLine();
            if (worst == CheckLevel.Ok) ctx.Out.Good(Loc.T("cli.doctor.allGood"));
            else if (worst == CheckLevel.Warn) ctx.Out.Warn(Loc.T("cli.doctor.withNotes"));
            else ctx.Out.Bad(Loc.T("cli.doctor.noRunPossible"));
        }

        return ctx.Out.Finish("doctor", code, new
        {
            worst = worst.ToString().ToLowerInvariant(),
            checks = checks.Select(c => new { area = c.Area, level = c.Level.ToString().ToLowerInvariant(), message = c.Message, fix = c.Fix }),
        });
    }

    public static int Versions(CommandContext ctx)
    {
        var sub = ctx.Args.Verb(1)?.ToLowerInvariant();

        if (sub is "scan") return VersionsScan(ctx);

        if (sub is "add")
        {
            var pathArg = ctx.Args.Get("path");
            var id = ctx.Args.Verb(2) ?? ctx.Args.Get("version");

            if (id is null && pathArg is null)
                throw new UsageException(Loc.T("cli.versions.addUsage"));

            // With a folder, the installation is asked what it is instead of the
            // person being asked to type it correctly.
            VersionCandidate? found = null;
            if (pathArg is not null)
            {
                var probe = Path.GetFullPath(pathArg);
                if (!Directory.Exists(probe)) throw new ConfigException(Loc.T("cli.folderMissing", probe));
                found = VersionScanner.Inspect(probe, ctx.Machine);
                pathArg = probe;

                if (id is null)
                {
                    id = found.ProposedId
                         ?? throw new ConfigException(Loc.T("cli.versions.notDetected", probe));
                    ctx.Out.Info(Loc.T("cli.versions.detected", id, found.Explain()));
                }

                if (found.Mismatch && !ctx.Args.Flag("force"))
                {
                    ctx.Out.Bad($"{probe}: {found.Explain()}");
                    ctx.Out.Info(Loc.T("cli.versions.mismatchHint"));
                    return ctx.Out.Finish("versions.add", ExitCodes.SetupError, new
                    {
                        dir = probe, mismatch = true, idFromFolder = found.IdFromFolder, idFromBuild = found.IdFromBuild,
                    });
                }
            }

            if (ctx.Machine.FindVersion(id!) is not null)
            {
                ctx.Out.Warn(Loc.T("cli.versions.exists", id));
                return ctx.Out.Finish("versions.add", ExitCodes.SetupError, new { id, existed = true });
            }

            var entry = new GameVersion
            {
                Id = id!,
                Path = pathArg,
                Branch = ctx.Args.Get("branch"),
                Notes = ctx.Args.Get("notes"),
                Build = found?.Build,
            };

            // Registering it under the default folder name needs no explicit path.
            if (entry.Path is not null &&
                ConfigStore.PathsEqual(entry.Path, Path.Combine(ctx.Machine.GameRoot, $"7DTD-{id}")))
                entry.Path = null;

            // Without a folder there is still an installation to look at, at the
            // place the id implies.
            if (found is null)
            {
                var guess = ctx.Machine.GameDir(entry.Id);
                if (Directory.Exists(guess)) entry.Build = VersionScanner.ReadBuild(guess);
            }

            ctx.Machine.Versions.Add(entry);
            ctx.SaveMachine();

            var dir = ctx.Machine.GameDir(id!);
            ctx.Out.Good(Loc.T("cli.versions.added", id, dir));
            if (entry.Build is not null) ctx.Out.Detail($"Build {entry.Build}");

            if (!File.Exists(Path.Combine(dir, "7DaysToDie.exe")))
            {
                Directory.CreateDirectory(dir);
                ctx.Out.Warn(Loc.T("cli.versions.noExeYet"));
                // Deliberately only printed, never run: DepotDownloader asks for
                // the Steam password and the Steam Guard code, and those belong to
                // the person, not to this tool.
                var branch = entry.Branch ?? $"v{id}";
                ctx.Out.Info(Loc.T("cli.versions.depotIntro"));
                ctx.Out.Info($"  DepotDownloader -app 251570 -depot 251576 -branch {branch} -dir \"{dir}\" -username <your-steam-name>");
                ctx.Out.Info(Loc.T("cli.versions.depotHint"));
            }

            return ctx.Out.Finish("versions.add", ExitCodes.Ok, new { id, dir, branch = entry.Branch, build = entry.Build });
        }

        if (sub is "remove" or "rm")
        {
            var id = ctx.Args.Verb(2) ?? ctx.Args.Get("version")
                     ?? throw new UsageException(Loc.T("cli.versions.removeUsage"));
            var hit = ctx.Machine.FindVersion(id);
            if (hit is null)
            {
                ctx.Out.Warn(Loc.T("cli.versions.notRegistered", id));
                return ctx.Out.Finish("versions.remove", ExitCodes.SetupError, new { id });
            }
            ctx.Machine.Versions.Remove(hit);
            ctx.SaveMachine();
            ctx.Out.Good(Loc.T("cli.versions.removed", id, ctx.Machine.GameDir(id)));
            return ctx.Out.Finish("versions.remove", ExitCodes.Ok, new { id });
        }

        var rows = new List<string[]>();
        var data = new List<object>();
        foreach (var v in ctx.Machine.Versions)
        {
            var dir = ctx.Machine.GameDir(v.Id);
            var installed = File.Exists(Path.Combine(dir, VersionScanner.ExeName));
            var build = installed ? VersionScanner.ReadBuild(dir) : null;
            var drifted = v.Build is not null && build is not null && v.Build != build;

            rows.Add(new[]
            {
                v.Id,
                installed
                    ? Loc.T(drifted ? "cli.versions.statusChanged" : "cli.versions.statusInstalled")
                    : Loc.T("cli.versions.statusMissing"),
                build ?? v.Build ?? "",
                v.Branch ?? "",
                dir,
                v.Notes ?? "",
            });
            data.Add(new { id = v.Id, installed, build, registeredBuild = v.Build, drifted, branch = v.Branch, dir, notes = v.Notes });
        }
        ctx.Out.Table(rows, Loc.T("col.version"), Loc.T("col.status"), "Build", Loc.T("col.branch"),
            Loc.T("col.folder"), Loc.T("col.notes"));
        if (rows.Count == 0) ctx.Out.Warn(Loc.T("cli.versions.none"));

        return ctx.Out.Finish("versions", ExitCodes.Ok, new { versions = data });
    }

    /// <summary>
    /// Looks for installations on disk instead of having their versions typed in.
    /// Prints what it found and, with --add, registers everything it is sure
    /// about. Folders whose name contradicts their build are never registered
    /// silently: that is exactly how a report starts claiming a version it never
    /// tested.
    /// </summary>
    private static int VersionsScan(CommandContext ctx)
    {
        var root = ctx.Args.Get("root") ?? ctx.Args.Get("path") ?? ctx.Args.Verb(2) ?? ctx.Machine.GameRoot;
        root = Path.GetFullPath(root);
        var depth = int.TryParse(ctx.Args.Get("depth"), out var d) ? d : 2;

        if (!Directory.Exists(root)) throw new ConfigException(Loc.T("cli.folderMissing", root));

        var found = VersionScanner.Scan(root, ctx.Machine, depth);
        ctx.Out.Info(Loc.T("cli.scan.searched", root, depth, found.Count));

        var rows = found.Select(c => new[]
        {
            c.ProposedId ?? "?",
            Loc.T(c.Registered ? "cli.versions.statusRegistered"
                : c.IsLiveInstall ? "cli.versions.statusLive"
                : c.Mismatch ? "cli.versions.statusConflict"
                : c.ProposedId is null ? "cli.versions.statusUnclear"
                : "cli.versions.statusNew"),
            c.Dir,
            c.Explain(),
        }).ToList();
        ctx.Out.Table(rows, Loc.T("col.version"), Loc.T("col.status"), Loc.T("col.folder"), Loc.T("col.source"));

        var addable = found.Where(c => c is { Blocked: false, Registered: false, ProposedId: not null }
                                       && (!c.Mismatch || ctx.Args.Flag("force"))).ToList();

        var added = new List<object>();
        if (ctx.Args.Flag("add"))
        {
            // Registered folders whose build was never written down get it now.
            // Without it the drift check has nothing to compare against.
            var noted = 0;
            foreach (var c in found.Where(c => c.Registered && c.Build is not null))
            {
                var entry = ctx.Machine.FindVersion(c.RegisteredAs!);
                if (entry?.Build is not null) continue;
                entry!.Build = c.Build;
                noted++;
            }
            if (noted > 0) ctx.Out.Detail(Loc.T("cli.scan.notedBuilds", noted));

            foreach (var c in addable)
            {
                if (ctx.Machine.FindVersion(c.ProposedId!) is not null)
                {
                    ctx.Out.Warn(Loc.T("cli.scan.skipDifferentFolder", c.ProposedId!, c.Dir));
                    continue;
                }

                var isDefaultDir = ConfigStore.PathsEqual(c.Dir, Path.Combine(ctx.Machine.GameRoot, $"7DTD-{c.ProposedId}"));
                ctx.Machine.Versions.Add(new GameVersion
                {
                    Id = c.ProposedId!,
                    Path = isDefaultDir ? null : c.Dir,
                    Build = c.Build,
                    Branch = $"v{c.ProposedId}",
                });
                ctx.Out.Good(Loc.T("cli.scan.added", c.ProposedId!, c.Dir));
                added.Add(new { id = c.ProposedId, dir = c.Dir, build = c.Build });
            }

            if (added.Count > 0 || noted > 0)
            {
                ctx.Machine.Versions.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
                ctx.SaveMachine();
            }
            if (added.Count == 0) ctx.Out.Info(Loc.T("cli.scan.noNew"));
        }
        else if (addable.Count > 0)
        {
            ctx.Out.Info(Loc.T("cli.scan.newCount", addable.Count, root));
        }

        foreach (var c in found.Where(c => c.Mismatch))
            ctx.Out.Warn(Loc.T("cli.scan.refused", c.Dir, c.Explain()));

        foreach (var c in found.Where(c => c.IsLiveInstall))
            ctx.Out.Warn(Loc.T("cli.scan.liveRefused", c.Dir));

        return ctx.Out.Finish("versions.scan", ExitCodes.Ok, new
        {
            root,
            depth,
            found = found.Select(c => new
            {
                dir = c.Dir,
                proposedId = c.ProposedId,
                source = c.Source.ToString().ToLowerInvariant(),
                build = c.Build,
                idFromFolder = c.IdFromFolder,
                idFromBuild = c.IdFromBuild,
                mismatch = c.Mismatch,
                isLiveInstall = c.IsLiveInstall,
                registeredAs = c.RegisteredAs,
                hasHarmony = c.HasHarmony,
                mods = c.Mods,
            }),
            added,
        });
    }

    public static int Mods(CommandContext ctx)
    {
        var sub = ctx.Args.Verb(1)?.ToLowerInvariant();

        if (sub is "add")
        {
            var raw = ctx.Args.Verb(2) ?? ctx.Args.Get("path")
                      ?? throw new UsageException(Loc.T("cli.mods.addUsage"));
            var path = Path.GetFullPath(raw);

            // A directory is accepted too, because "the mod's test folder" is what
            // anyone has in mind when they type this.
            if (Directory.Exists(path))
            {
                var guess = new[]
                {
                    Path.Combine(path, ConfigStore.ModFileName),
                    Path.Combine(path, "test", ConfigStore.ModFileName),
                }.FirstOrDefault(File.Exists);
                if (guess is null) throw new ConfigException(Loc.T("cli.mods.noModJson", path, ConfigStore.ModFileName));
                path = guess;
            }

            var mod = ConfigStore.LoadMod(path);
            if (ctx.Machine.ModConfigs.Any(p => ConfigStore.PathsEqual(p, path)))
            {
                ctx.Out.Warn(Loc.T("cli.mods.alreadyRegistered", path));
            }
            else
            {
                ctx.Machine.ModConfigs.Add(path);
                ctx.SaveMachine();
                ctx.Out.Good(Loc.T("cli.mods.registered", mod.ModId, path));
            }
            return ctx.Out.Finish("mods.add", ExitCodes.Ok, new { modId = mod.ModId, path });
        }

        if (sub is "remove" or "rm")
        {
            var what = ctx.Args.Verb(2) ?? throw new UsageException(Loc.T("cli.mods.removeUsage"));
            var before = ctx.Machine.ModConfigs.Count;

            ctx.Machine.ModConfigs.RemoveAll(p =>
            {
                if (ConfigStore.PathsEqual(p, what)) return true;
                try { return string.Equals(ConfigStore.LoadMod(p).ModId, what, StringComparison.OrdinalIgnoreCase); }
                catch (ConfigException) { return false; }
            });

            if (ctx.Machine.ModConfigs.Count == before)
            {
                ctx.Out.Warn(Loc.T("cli.mods.nothingRemoved", what));
                return ctx.Out.Finish("mods.remove", ExitCodes.SetupError, new { what });
            }
            ctx.SaveMachine();
            ctx.Out.Good(Loc.T("cli.mods.removed", what));
            return ctx.Out.Finish("mods.remove", ExitCodes.Ok, new { what });
        }

        var mods = ConfigStore.LoadRegisteredMods(ctx.Machine, out var missing);
        var rows = mods.Select(m => new[]
        {
            m.Config.ModId,
            m.Config.DisplayName,
            string.Join(", ", m.Config.Variants.Select(v => v.Name)),
            string.Join(", ", m.Config.Dependencies),
            string.Join(", ", m.Config.Profiles.Select(p => p.Name)),
        }).ToList();
        ctx.Out.Table(rows, "modId", Loc.T("col.name"), Loc.T("col.variants"), Loc.T("col.dependencies"),
            Loc.T("col.profiles"));
        foreach (var m in missing) ctx.Out.Warn(Loc.T("cli.mods.fileMissing", m));
        if (rows.Count == 0) ctx.Out.Warn(Loc.T("cli.mods.none"));

        return ctx.Out.Finish("mods", ExitCodes.Ok, new
        {
            mods = mods.Select(m => new
            {
                modId = m.Config.ModId,
                displayName = m.Config.DisplayName,
                repo = m.Config.Repo,
                path = m.Path,
                variants = m.Config.Variants.Select(v => new { v.Name, v.Folder }),
                dependencies = m.Config.Dependencies,
                profiles = m.Config.Profiles.Select(p => new { p.Name, p.Variant, p.Versions, stages = p.Stages.Select(s => s.ToString().ToLowerInvariant()) }),
            }),
            missing,
        });
    }

    /// <summary>
    /// Shows, sets and checks the language. The check exists because a catalog is
    /// a hand-editable file: anyone can drop one into lang\ and needs to be told
    /// which keys they have not filled in yet.
    /// </summary>
    public static int Lang(CommandContext ctx)
    {
        var wanted = ctx.Args.Verb(1) ?? ctx.Args.Get("set");

        if (ctx.Args.Flag("check"))
        {
            var target = wanted is null ? Loc.Current : Loc.Resolve(wanted);
            var missing = Loc.MissingKeys(target);

            if (missing.Count == 0) ctx.Out.Good(Loc.T("cli.lang.complete", target));
            else
            {
                ctx.Out.Warn(Loc.T("cli.lang.missing", target, missing.Count));
                foreach (var key in missing) ctx.Out.Detail("  " + key);
            }

            return ctx.Out.Finish("lang.check", ExitCodes.Ok, new { language = target, missing });
        }

        if (wanted is not null)
        {
            var code = Loc.Resolve(wanted);
            Loc.Use(code);

            // Written to the config only when there is one; "tb lang english"
            // before "tb init" should still work and just affect this call.
            var saved = false;
            if (File.Exists(ctx.MachinePath))
            {
                ctx.Machine.Language = code;
                ctx.SaveMachine();
                saved = true;
            }

            ctx.Out.Good(Loc.T("cli.lang.set", Loc.NativeName(code), code));
            if (!saved) ctx.Out.Warn(Loc.T("cli.lang.notSaved", ctx.MachinePath));
            return ctx.Out.Finish("lang", ExitCodes.Ok, new { language = code, saved });
        }

        var rows = new List<string[]>();
        foreach (var code in Loc.Available())
        {
            var state = new List<string>();
            if (code == Loc.Current) state.Add(Loc.T("cli.lang.inUse"));
            if (code == Loc.FromSystem()) state.Add(Loc.T("cli.lang.system"));
            var gaps = Loc.MissingKeys(code).Count;
            if (gaps > 0) state.Add(Loc.T("cli.lang.gaps", gaps));

            rows.Add(new[] { code, Loc.NativeName(code), string.Join(", ", state) });
        }
        ctx.Out.Table(rows, Loc.T("col.language"), Loc.T("col.nativeName"), Loc.T("col.state"));
        ctx.Out.Info(Loc.T("cli.lang.hint", Loc.LangDir));

        return ctx.Out.Finish("lang", ExitCodes.Ok, new
        {
            current = Loc.Current,
            system = Loc.FromSystem(),
            langDir = Loc.LangDir,
            languages = Loc.Available().Select(c => new
            {
                code = c,
                nativeName = Loc.NativeName(c),
                missingKeys = Loc.MissingKeys(c).Count,
            }),
        });
    }

    public static int Profiles(CommandContext ctx)
    {
        var (mod, path) = ctx.RequireMod();
        var rows = mod.Profiles.Select(p => new[]
        {
            p.Name,
            p.Variant ?? mod.Variants.FirstOrDefault()?.Name ?? "",
            p.Versions.Count == 0 ? Loc.T("cli.profiles.all") : string.Join(", ", p.Versions),
            string.Join("+", p.Stages.Select(s => s.ToString().ToLowerInvariant())),
            p.Notes ?? "",
        }).ToList();

        ctx.Out.Table(rows, Loc.T("col.profile"), Loc.T("col.variant"), Loc.T("col.versions"),
            Loc.T("col.stages"), Loc.T("col.notes"));
        if (rows.Count == 0) ctx.Out.Warn(Loc.T("cli.profiles.none", mod.ModId, path));

        return ctx.Out.Finish("profiles", ExitCodes.Ok, new
        {
            modId = mod.ModId,
            profiles = mod.Profiles.Select(p => new
            {
                p.Name,
                variant = p.Variant,
                versions = p.Versions,
                stages = p.Stages.Select(s => s.ToString().ToLowerInvariant()),
                notes = p.Notes,
            }),
        });
    }
}
