using Testbench.Core.Config;
using Testbench.Core.Diagnostics;

namespace Testbench.Cli.Commands;

/// <summary>Commands that read or change configuration, plus doctor.</summary>
public static class ConfigCommands
{
    public static int Init(CommandContext ctx)
    {
        var path = ctx.MachinePath;
        if (File.Exists(path) && !ctx.Args.Flag("force"))
        {
            ctx.Out.Warn($"'{path}' existiert schon. Mit --force ueberschreiben.");
            return ctx.Out.Finish("init", ExitCodes.SetupError, new { path, existed = true });
        }

        var benchRoot = ctx.Args.Get("bench-root")
                        ?? Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? Directory.GetCurrentDirectory();

        var cfg = new MachineConfig
        {
            GameRoot = ctx.Args.Get("game-root") ?? @"E:\Games",
            ResultRoot = Path.Combine(benchRoot, "results"),
            StateRoot = Path.Combine(benchRoot, "state"),
        };
        if (ctx.Args.Get("user-data-root") is { } udr) cfg.UserDataRoot = udr;

        ConfigStore.SaveMachine(cfg, path);
        ctx.Out.Good($"Angelegt: {path}");
        ctx.Out.Info("Weiter mit: tb import --psd1 <alte Testbench.psd1>   oder   tb versions add <version>");
        return ctx.Out.Finish("init", ExitCodes.Ok, new { path });
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
            ctx.Out.Warn($"'{modOut}' existiert schon. Mit --force ueberschreiben.");
            return ctx.Out.Finish("import", ExitCodes.SetupError, new { modOut, existed = true });
        }

        ConfigStore.SaveMod(imported.Mod, modOut);

        var machine = imported.Machine;
        var registered = machine.ModConfigs.Any(p => ConfigStore.PathsEqual(p, modOut));
        if (!registered) machine.ModConfigs.Add(modOut);
        ConfigStore.SaveMachine(machine, ctx.MachinePath);

        ctx.Out.Good($"Maschinenkonfiguration: {ctx.MachinePath}");
        ctx.Out.Good($"Mod-Konfiguration:      {modOut}");
        ctx.Out.Info($"Mod '{imported.Mod.ModId}', Varianten: {string.Join(", ", imported.Mod.Variants.Select(v => v.Name))}");
        ctx.Out.Info($"Abhaengigkeiten: {(imported.Mod.Dependencies.Count == 0 ? "keine" : string.Join(", ", imported.Mod.Dependencies))}");
        ctx.Out.Info($"Profile: {string.Join(", ", imported.Mod.Profiles.Select(p => p.Name))}");
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
            ctx.Out.Good($"{r.VersionId} / {r.Variant} / Mod {r.ModVersion}: Sicht {r.Visual}, Nachweis {r.EvidenceOk} -> {r.Id}");

        if (runs.Count == 0) ctx.Out.Warn($"Aus '{full}' war nichts zu uebernehmen.");
        else ctx.Out.Info($"{runs.Count} Eintrag/Eintraege als GUI-Laeufe von '{mod.ModId}' uebernommen.");

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
            if (worst == CheckLevel.Ok) ctx.Out.Good("Alles in Ordnung.");
            else if (worst == CheckLevel.Warn) ctx.Out.Warn("Laeuft, mit Anmerkungen.");
            else ctx.Out.Bad("So kann kein Lauf stattfinden.");
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

        if (sub is "add")
        {
            var id = ctx.Args.Verb(2) ?? ctx.Args.Get("version")
                     ?? throw new UsageException("Version fehlt: tb versions add <version>");

            if (ctx.Machine.FindVersion(id) is not null)
            {
                ctx.Out.Warn($"Version '{id}' ist schon eingetragen.");
                return ctx.Out.Finish("versions.add", ExitCodes.SetupError, new { id, existed = true });
            }

            var entry = new GameVersion
            {
                Id = id,
                Path = ctx.Args.Get("path"),
                Branch = ctx.Args.Get("branch"),
                Notes = ctx.Args.Get("notes"),
            };
            ctx.Machine.Versions.Add(entry);
            ctx.SaveMachine();

            var dir = ctx.Machine.GameDir(id);
            ctx.Out.Good($"Version '{id}' eingetragen: {dir}");

            if (!File.Exists(Path.Combine(dir, "7DaysToDie.exe")))
            {
                Directory.CreateDirectory(dir);
                ctx.Out.Warn($"Dort liegt noch keine 7DaysToDie.exe.");
                // Deliberately only printed, never run: DepotDownloader asks for
                // the Steam password and the Steam Guard code, and those belong to
                // the person, not to this tool.
                var branch = entry.Branch ?? $"v{id}";
                ctx.Out.Info("Installation holen (Passwort und Steam-Guard-Code gibst du selbst ein):");
                ctx.Out.Info($"  DepotDownloader -app 251570 -depot 251576 -branch {branch} -dir \"{dir}\" -username <dein-steam-name>");
                ctx.Out.Info("Danach in Mods\\ nur 0_TFP_Harmony lassen; ab dem zweiten Lauf raeumt der Bench selbst.");
            }

            return ctx.Out.Finish("versions.add", ExitCodes.Ok, new { id, dir, branch = entry.Branch });
        }

        if (sub is "remove" or "rm")
        {
            var id = ctx.Args.Verb(2) ?? ctx.Args.Get("version")
                     ?? throw new UsageException("Version fehlt: tb versions remove <version>");
            var hit = ctx.Machine.FindVersion(id);
            if (hit is null)
            {
                ctx.Out.Warn($"Version '{id}' war nicht eingetragen.");
                return ctx.Out.Finish("versions.remove", ExitCodes.SetupError, new { id });
            }
            ctx.Machine.Versions.Remove(hit);
            ctx.SaveMachine();
            ctx.Out.Good($"Version '{id}' entfernt. Die Installation unter {ctx.Machine.GameDir(id)} bleibt liegen.");
            return ctx.Out.Finish("versions.remove", ExitCodes.Ok, new { id });
        }

        var rows = new List<string[]>();
        var data = new List<object>();
        foreach (var v in ctx.Machine.Versions)
        {
            var dir = ctx.Machine.GameDir(v.Id);
            var installed = File.Exists(Path.Combine(dir, "7DaysToDie.exe"));
            rows.Add(new[] { v.Id, installed ? "installiert" : "FEHLT", v.Branch ?? "", dir, v.Notes ?? "" });
            data.Add(new { id = v.Id, installed, branch = v.Branch, dir, notes = v.Notes });
        }
        ctx.Out.Table(rows, "Version", "Status", "Branch", "Ordner", "Notiz");
        if (rows.Count == 0) ctx.Out.Warn("Keine Version eingetragen. tb versions add <version>");

        return ctx.Out.Finish("versions", ExitCodes.Ok, new { versions = data });
    }

    public static int Mods(CommandContext ctx)
    {
        var sub = ctx.Args.Verb(1)?.ToLowerInvariant();

        if (sub is "add")
        {
            var raw = ctx.Args.Verb(2) ?? ctx.Args.Get("path")
                      ?? throw new UsageException("Pfad fehlt: tb mods add <pfad-zur-testbench.mod.json>");
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
                if (guess is null) throw new ConfigException($"In '{path}' liegt keine {ConfigStore.ModFileName}.");
                path = guess;
            }

            var mod = ConfigStore.LoadMod(path);
            if (ctx.Machine.ModConfigs.Any(p => ConfigStore.PathsEqual(p, path)))
            {
                ctx.Out.Warn($"'{path}' war schon registriert.");
            }
            else
            {
                ctx.Machine.ModConfigs.Add(path);
                ctx.SaveMachine();
                ctx.Out.Good($"Registriert: {mod.ModId} <- {path}");
            }
            return ctx.Out.Finish("mods.add", ExitCodes.Ok, new { modId = mod.ModId, path });
        }

        if (sub is "remove" or "rm")
        {
            var what = ctx.Args.Verb(2) ?? throw new UsageException("tb mods remove <modId|pfad>");
            var before = ctx.Machine.ModConfigs.Count;

            ctx.Machine.ModConfigs.RemoveAll(p =>
            {
                if (ConfigStore.PathsEqual(p, what)) return true;
                try { return string.Equals(ConfigStore.LoadMod(p).ModId, what, StringComparison.OrdinalIgnoreCase); }
                catch (ConfigException) { return false; }
            });

            if (ctx.Machine.ModConfigs.Count == before)
            {
                ctx.Out.Warn($"Nichts entfernt: '{what}' war nicht registriert.");
                return ctx.Out.Finish("mods.remove", ExitCodes.SetupError, new { what });
            }
            ctx.SaveMachine();
            ctx.Out.Good($"Entfernt: {what}. Die Datei selbst bleibt liegen.");
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
        ctx.Out.Table(rows, "modId", "Name", "Varianten", "Abhaengigkeiten", "Profile");
        foreach (var m in missing) ctx.Out.Warn($"Registrierte Datei fehlt: {m}");
        if (rows.Count == 0) ctx.Out.Warn("Kein Mod registriert. tb mods add <pfad>");

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

    public static int Profiles(CommandContext ctx)
    {
        var (mod, path) = ctx.RequireMod();
        var rows = mod.Profiles.Select(p => new[]
        {
            p.Name,
            p.Variant ?? mod.Variants.FirstOrDefault()?.Name ?? "",
            p.Versions.Count == 0 ? "(alle)" : string.Join(", ", p.Versions),
            string.Join("+", p.Stages.Select(s => s.ToString().ToLowerInvariant())),
            p.Notes ?? "",
        }).ToList();

        ctx.Out.Table(rows, "Profil", "Variante", "Versionen", "Stufen", "Notiz");
        if (rows.Count == 0) ctx.Out.Warn($"'{mod.ModId}' hat keine Profile. In {path} unter 'profiles' anlegen.");

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
