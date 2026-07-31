using Testbench.Core.Config;
using Testbench.Core.Model;

namespace Testbench.Core.Deploy;

public sealed record DeployResult(
    string ModFolderPath,
    ModInfo ModInfo,
    List<DependencyResult> Dependencies,
    List<string> Disabled,
    List<string> Warnings);

/// <summary>
/// Brings the Mods folder of one test installation to a defined state: the mod
/// under test, its dependencies, nothing else.
/// </summary>
public sealed class ModDeployer
{
    public const string TrashFolder = "_Mods-deaktiviert";

    private readonly MachineConfig _machine;
    private readonly Action<string> _log;

    public ModDeployer(MachineConfig machine, Action<string>? log = null)
    {
        _machine = machine;
        _log = log ?? (_ => { });
    }

    public DeployResult Deploy(string gameDir, ModConfig mod, ModVariant variant)
    {
        var modsDir = Path.Combine(gameDir, "Mods");
        Directory.CreateDirectory(modsDir);

        var source = mod.VariantSource(variant);
        if (!Directory.Exists(source))
            throw new ConfigException(I18n.Loc.T("deploy.modSourceMissing", source));

        var modFolder = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        var deps = ResolveDependencies(mod);
        var warnings = new List<string>();

        // ---- 1. everything that does not belong here goes away -------------
        var keep = new HashSet<string>(_machine.KeepMods, StringComparer.OrdinalIgnoreCase) { modFolder };
        foreach (var d in deps) keep.Add(d.Folder);

        var disabled = DisableForeignMods(gameDir, modsDir, keep);

        // ---- 2. the mod under test ----------------------------------------
        var target = Path.Combine(modsDir, modFolder);
        DirectoryMirror.Replace(source, target);
        _log($"Deployed: {variant.Name} -> {target}");

        // ---- 3. the dependencies ------------------------------------------
        // Always mirrored, not just when the folder is absent: otherwise each
        // installation could end up with a different Gears version depending on
        // its history, and those silent differences are exactly what a
        // multiversion test is supposed to rule out.
        var results = new List<DependencyResult>();
        foreach (var dep in deps)
        {
            var r = new DependencyResult { Key = dep.Key, Folder = dep.Folder };
            var depTarget = Path.Combine(modsDir, dep.Folder);

            if (!string.IsNullOrWhiteSpace(dep.Source) && Directory.Exists(dep.Source))
            {
                DirectoryMirror.Replace(dep.Source, depTarget);
                r.Deployed = true;
                _log($"Deployed: {dep.Folder} -> {depTarget}");
            }
            else
            {
                // An existing older copy is NOT deleted, so the run at least
                // happens with what is there, but it must be reported instead of
                // passing for fresh.
                r.Problem = I18n.Loc.T("deploy.sourceMissing", dep.Source);
                warnings.Add(I18n.Loc.T("deploy.depSourceMissing", dep.Key, dep.Source));
            }

            if (Directory.Exists(depTarget)) r.ReportedName = ModInfoReader.Read(depTarget).Name;
            else if (r.Problem is null) r.Problem = I18n.Loc.T("dep.notInstalled");

            results.Add(r);
        }

        return new DeployResult(target, ModInfoReader.Read(target), results, disabled, warnings);
    }

    /// <summary>
    /// Resolves the mod's dependency keys against the library, pulling in what
    /// they require and keeping the order the library defines. Gears without
    /// Quartz loads nothing, and nobody should have to remember that per mod.
    /// </summary>
    public List<ResolvedDependency> ResolveDependencies(ModConfig mod)
    {
        var wanted = new List<string>();

        void Add(string key, int depth)
        {
            if (depth > 8) throw new ConfigException($"Abhaengigkeiten von '{key}' sind zirkulaer.");
            if (!_machine.DependencyLibrary.TryGetValue(key, out var def))
                throw new ConfigException(I18n.Loc.T("deploy.unknownDependency", mod.ModId, key));
            foreach (var req in def.Requires) Add(req, depth + 1);
            if (!wanted.Contains(key, StringComparer.OrdinalIgnoreCase)) wanted.Add(key);
        }

        foreach (var key in mod.Dependencies) Add(key, 0);

        return wanted
            .Select(k => new ResolvedDependency(k, _machine.DependencyLibrary[k].Folder, _machine.DependencyLibrary[k].Source))
            .ToList();
    }

    /// <summary>
    /// Moves every mod that is not on the keep list into _Mods-deaktiviert.
    ///
    /// Two traps live in here. Move-Item -Force does NOT overwrite an existing
    /// directory, it fails on it, and with -ErrorAction SilentlyContinue it failed
    /// silently: a mod that had been disabled once stayed in the Mods folder on
    /// every later run and loaded along without anything turning red. So the
    /// target is cleared first (the trash is scrap by definition) and a failure is
    /// reported instead of swallowed.
    /// </summary>
    private List<string> DisableForeignMods(string gameDir, string modsDir, HashSet<string> keep)
    {
        var trash = Path.Combine(gameDir, TrashFolder);
        Directory.CreateDirectory(trash);

        var disabled = new List<string>();
        foreach (var dir in Directory.GetDirectories(modsDir))
        {
            var name = Path.GetFileName(dir);
            if (keep.Contains(name)) continue;

            var to = Path.Combine(trash, name);
            DirectoryMirror.DeleteIfExists(to);
            Directory.Move(dir, to);

            if (Directory.Exists(dir))
                throw new IOException(I18n.Loc.T("deploy.cannotRemove", name));

            disabled.Add(name);
            _log($"Deaktiviert: {name}");
        }
        return disabled;
    }
}

public sealed record ResolvedDependency(string Key, string Folder, string Source);
