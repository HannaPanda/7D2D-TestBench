using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testbench.Core.Config;

/// <summary>Raised for anything the user can fix in a config file or on disk.</summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}

/// <summary>
/// Loads and saves the machine config and the registered mod configs.
///
/// JSON rather than the PowerShell .psd1 the scripts used: the GUI, the CLI and
/// an agent all have to be able to read AND write the same file, which
/// Import-PowerShellDataFile can only do in one direction.
/// </summary>
public static class ConfigStore
{
    public const string MachineFileName = "testbench.json";
    public const string ModFileName = "testbench.mod.json";

    /// <summary>Environment variable that overrides config discovery.</summary>
    public const string ConfigEnvVar = "TESTBENCH_CONFIG";

    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // Humans edit these files by hand, so a stray trailing comma or a
        // comment must not make the tool unusable.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Without this every regex in the config comes out as + and < and
        // nobody can edit the file by hand any more. Nothing here is ever embedded
        // in HTML, so the relaxed encoder is the right one.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Where the machine config lives, in order of precedence:
    /// explicit path, TESTBENCH_CONFIG, next to the exe, one level above it
    /// (the published layout is &lt;bench&gt;\bin\tb.exe next to &lt;bench&gt;\testbench.json).
    /// </summary>
    public static string ResolveMachinePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);

        var env = Environment.GetEnvironmentVariable(ConfigEnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

        var exeDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(exeDir, MachineFileName),
                     Path.Combine(exeDir, "..", MachineFileName),
                 })
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        // Nothing found: name the location "tb init" would use, so the error
        // message can say where to create it.
        return Path.GetFullPath(Path.Combine(exeDir, "..", MachineFileName));
    }

    public static MachineConfig LoadMachine(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConfigException(
                $"Keine Maschinenkonfiguration unter '{path}'. Anlegen mit: tb init");
        }

        MachineConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<MachineConfig>(File.ReadAllText(path), Json);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"'{path}' ist kein gueltiges JSON: {ex.Message}");
        }

        if (cfg is null) throw new ConfigException($"'{path}' ist leer.");
        Validate(cfg, path);
        return cfg;
    }

    public static void SaveMachine(MachineConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteAtomic(path, JsonSerializer.Serialize(cfg, Json));
    }

    public static ModConfig LoadMod(string path)
    {
        if (!File.Exists(path)) throw new ConfigException($"Keine Mod-Konfiguration unter '{path}'.");

        ModConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<ModConfig>(File.ReadAllText(path), Json);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"'{path}' ist kein gueltiges JSON: {ex.Message}");
        }

        if (cfg is null) throw new ConfigException($"'{path}' ist leer.");
        if (string.IsNullOrWhiteSpace(cfg.ModId)) throw new ConfigException($"'{path}' hat kein modId.");
        if (cfg.Variants.Count == 0) throw new ConfigException($"'{path}' hat keine Variante.");

        // A mod config that does not say where its repo is only works from the
        // right working directory, which is exactly the class of failure this
        // tool exists to remove. Default to the folder above test\.
        if (string.IsNullOrWhiteSpace(cfg.Repo))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            cfg.Repo = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        return cfg;
    }

    public static void SaveMod(ModConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteAtomic(path, JsonSerializer.Serialize(cfg, Json));
    }

    /// <summary>Loads every registered mod config, skipping ones that no longer exist.</summary>
    public static List<(ModConfig Config, string Path)> LoadRegisteredMods(MachineConfig machine, out List<string> missing)
    {
        missing = new List<string>();
        var result = new List<(ModConfig, string)>();
        foreach (var p in machine.ModConfigs)
        {
            var full = Path.GetFullPath(p);
            if (!File.Exists(full)) { missing.Add(full); continue; }
            result.Add((LoadMod(full), full));
        }
        return result;
    }

    public static (ModConfig Config, string Path) RequireMod(MachineConfig machine, string modIdOrPath)
    {
        // A path always wins, so a mod that was never registered can still be
        // run without a setup step.
        if (modIdOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(modIdOrPath))
        {
            var full = Path.GetFullPath(modIdOrPath);
            return (LoadMod(full), full);
        }

        var mods = LoadRegisteredMods(machine, out var missing);
        var hit = mods.FirstOrDefault(m => string.Equals(m.Config.ModId, modIdOrPath, StringComparison.OrdinalIgnoreCase));
        if (hit.Config is not null) return hit;

        // An unambiguous fragment is enough. Mod ids come from ModInfo.xml and are
        // long ("sevendashestodie"); requiring the full string would put exactly
        // the kind of typing back that this tool removes.
        var partial = mods.Where(m => m.Config.ModId.Contains(modIdOrPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (partial.Count == 1) return partial[0];
        if (partial.Count > 1)
            throw new ConfigException(
                $"'{modIdOrPath}' passt auf mehrere Mods: {string.Join(", ", partial.Select(m => m.Config.ModId))}.");

        var known = mods.Count == 0 ? "(keine registriert)" : string.Join(", ", mods.Select(m => m.Config.ModId));
        var note = missing.Count > 0 ? $" Nicht mehr vorhanden: {string.Join(", ", missing)}." : "";
        throw new ConfigException($"Kein Mod '{modIdOrPath}'. Bekannt: {known}.{note}");
    }

    private static void Validate(MachineConfig cfg, string path)
    {
        if (string.IsNullOrWhiteSpace(cfg.GameRoot)) throw new ConfigException($"'{path}': gameRoot fehlt.");
        if (string.IsNullOrWhiteSpace(cfg.UserDataRoot)) throw new ConfigException($"'{path}': userDataRoot fehlt.");

        // The single most destructive mistake this tool could make: pointing the
        // isolated user data folder at the live one. Checked here so it cannot
        // reach a launcher, no matter which surface asked for the run.
        var live = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7DaysToDie");
        if (PathsEqual(cfg.UserDataRoot, live))
            throw new ConfigException($"'{path}': userDataRoot zeigt auf die LIVE-Daten ({live}).");

        foreach (var dup in cfg.Versions.GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            throw new ConfigException($"'{path}': Version '{dup.Key}' ist doppelt eingetragen.");

        foreach (var (key, dep) in cfg.DependencyLibrary)
        {
            if (string.IsNullOrWhiteSpace(dep.Folder))
                throw new ConfigException($"'{path}': Abhaengigkeit '{key}' hat kein folder.");
            foreach (var req in dep.Requires)
            {
                if (!cfg.DependencyLibrary.ContainsKey(req))
                    throw new ConfigException($"'{path}': '{key}' verlangt '{req}', das nicht in dependencyLibrary steht.");
            }
        }
    }

    public static bool PathsEqual(string a, string b)
    {
        static string N(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))
            .Replace('/', '\\');
        return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Write to a temp file and move it into place. The GUI and an agent can
    /// both be writing config or run state; a half-written JSON file would make
    /// the tool unusable until someone repaired it by hand.
    /// </summary>
    public static void WriteAtomic(string path, string content)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = full + ".tmp";
        File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
        File.Move(tmp, full, overwrite: true);
    }
}
