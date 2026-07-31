using System.Diagnostics;
using System.Text.Json;

namespace Testbench.Core.Config;

/// <summary>
/// One-shot migration of the PowerShell testbench's .psd1 files into the split
/// machine/mod configuration.
///
/// The .psd1 is read by PowerShell itself rather than by a hand-written parser:
/// Import-PowerShellDataFile is the only thing that is guaranteed to agree with
/// what the old scripts saw, and getting this wrong would silently change what
/// gets tested.
/// </summary>
public static class Psd1Importer
{
    public sealed record Imported(MachineConfig Machine, ModConfig Mod, List<string> Notes);

    /// <summary>
    /// Splits one .psd1 into the machine part and the mod part. Pass an existing
    /// machine config to merge into (versions and dependencies are unioned), or
    /// null to start from the file's values.
    /// </summary>
    public static Imported Import(string psd1Path, MachineConfig? mergeInto = null)
    {
        var notes = new List<string>();
        using var doc = ReadPsd1(psd1Path);
        var root = doc.RootElement;

        var machine = mergeInto ?? new MachineConfig();

        if (Str(root, "GameRoot") is { } gameRoot) machine.GameRoot = gameRoot;
        if (Str(root, "UserDataRoot") is { } udr) machine.UserDataRoot = udr;
        if (Str(root, "ResultRoot") is { } rr) machine.ResultRoot = rr;
        if (Str(root, "PrefsKey") is { } pk) machine.Prefs.Key = pk;
        if (Str(root, "PrefsBackupDir") is { } pbd) machine.Prefs.BackupDir = pbd;
        if (Str(root, "ReadyPattern") is { } rp) machine.ReadyPattern = rp;
        if (Str(root, "XmlProblemPattern") is { } xpp) machine.XmlProblemPattern = xpp;
        if (Int(root, "TimeoutSeconds") is { } ts) machine.TimeoutSeconds = ts;
        if (StrList(root, "KeepMods") is { Count: > 0 } keep) machine.KeepMods = keep;
        if (StrList(root, "IgnorePatterns") is { Count: > 0 } ign) machine.IgnorePatterns = ign;

        foreach (var id in StrList(root, "Versions"))
        {
            if (machine.FindVersion(id) is not null) continue;
            machine.Versions.Add(new GameVersion { Id = id });
        }

        // ---- dependencies: folder + source in the file, addressed by key here ----
        var depKeys = new List<string>();
        if (root.TryGetProperty("Dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
        {
            foreach (var dep in deps.EnumerateArray())
            {
                var folder = Str(dep, "Name");
                var source = Str(dep, "Source");
                if (string.IsNullOrWhiteSpace(folder)) continue;

                var key = DependencyKey(folder!);
                depKeys.Add(key);

                if (machine.DependencyLibrary.TryGetValue(key, out var existing))
                {
                    if (!string.Equals(existing.Folder, folder, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existing.Source, source, StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add($"Abhaengigkeit '{key}' war schon registriert ({existing.Folder} <- {existing.Source}); " +
                                  $"die abweichende Angabe aus '{Path.GetFileName(psd1Path)}' ({folder} <- {source}) wurde NICHT uebernommen.");
                    }
                    continue;
                }

                machine.DependencyLibrary[key] = new DependencyDef
                {
                    Folder = folder!,
                    Source = source ?? "",
                    DisplayName = key == "gears" ? "Gears" : key == "quartz" ? "Quartz" : null,
                };
            }
        }

        // Gears needs Quartz. Recorded once here instead of relying on both being
        // listed in every mod config in the right order.
        if (machine.DependencyLibrary.TryGetValue("gears", out var gears) &&
            machine.DependencyLibrary.ContainsKey("quartz") &&
            !gears.Requires.Contains("quartz", StringComparer.OrdinalIgnoreCase))
        {
            gears.Requires.Add("quartz");
        }

        // ---- the mod part ----
        var repo = Str(root, "ModRepo") ?? "";
        var mod = new ModConfig
        {
            Repo = repo,
            Dependencies = depKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };

        // Editions was a fixed Survival/Creative pair. A mod without editions had
        // to point both keys at the same folder, so identical folders collapse
        // into one variant here instead of carrying the crutch forward.
        if (root.TryGetProperty("Editions", out var ed) && ed.ValueKind == JsonValueKind.Object)
        {
            var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in ed.EnumerateObject())
            {
                var folder = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(folder)) continue;
                if (!byFolder.TryGetValue(folder!, out var names)) byFolder[folder!] = names = new List<string>();
                names.Add(prop.Name);
            }

            foreach (var (folder, names) in byFolder)
            {
                var name = names.Count > 1 ? "Default" : names[0];
                mod.Variants.Add(new ModVariant
                {
                    Name = name,
                    Folder = folder,
                    Notes = names.Count > 1
                        ? $"Frueher fuer alle Editionen derselbe Ordner ({string.Join(", ", names.OrderBy(n => n))})."
                        : null,
                });
            }
            if (byFolder.Count > 1) mod.Variants = mod.Variants.OrderBy(v => v.Name).ToList();
        }

        // Identity comes from ModInfo.xml, not from the folder name: <Name> is what
        // the log will say and therefore the only name worth addressing the mod by.
        // Falls back to the repository folder when the source is not reachable.
        var first = mod.Variants.FirstOrDefault();
        if (first is not null && Directory.Exists(mod.VariantSource(first)))
        {
            var info = Deploy.ModInfoReader.Read(mod.VariantSource(first));
            mod.ModId = info.Name.ToLowerInvariant();
            mod.DisplayName = info.DisplayName;
        }
        else
        {
            mod.ModId = ModIdFromRepo(repo, psd1Path);
            mod.DisplayName = Path.GetFileName(repo.TrimEnd('\\', '/'));
            notes.Add($"Mod-Quelle nicht erreichbar; modId aus dem Repo-Namen abgeleitet ('{mod.ModId}').");
        }

        if (root.TryGetProperty("Stage2", out var s2) && s2.ValueKind == JsonValueKind.Object)
        {
            mod.Stage2 = new Stage2Config
            {
                LogFilter = Str(s2, "LogFilter"),
                EvidencePatterns = StrList(s2, "EvidencePatterns"),
                EvidenceLabel = Str(s2, "EvidenceLabel") ?? "Im Log belegt",
                VisualQuestion = Str(s2, "VisualQuestion") ?? "Sah/verhielt sich alles wie erwartet?",
            };
        }

        // Two profiles that cover what the old command lines did, so the first
        // thing anyone sees after the import is a working button.
        var versions = StrList(root, "Versions");
        var mainVariant = mod.Variants.FirstOrDefault()?.Name;
        mod.Profiles.Add(new TestProfile
        {
            Name = "matrix",
            Variant = mainVariant,
            Versions = versions.ToList(),
            Stages = new List<TestStage> { TestStage.Headless },
            Notes = "Ersetzt Invoke-TestMatrix.ps1.",
        });
        if (versions.Count > 0)
        {
            mod.Profiles.Add(new TestProfile
            {
                Name = "gui",
                Variant = mainVariant,
                Versions = new List<string> { versions[^1] },
                Stages = new List<TestStage> { TestStage.Gui },
                Notes = "Sichtpruefung auf der neuesten Version. Ersetzt Start-Gui.ps1.",
            });
        }

        return new Imported(machine, mod, notes);
    }

    /// <summary>
    /// "0-Quartz" -> "quartz", "00000-Gears" -> "gears". The load-order prefix
    /// stays in the folder name (7DTD derives load order from it) but must not
    /// end up in the key anyone has to type.
    /// </summary>
    public static string DependencyKey(string folder)
    {
        var s = folder.Trim();
        var i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
        var core = i < s.Length ? s[i..] : s;
        return core.ToLowerInvariant().Replace(" ", "-");
    }

    private static string ModIdFromRepo(string repo, string psd1Path)
    {
        var leaf = Path.GetFileName(repo.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf)) leaf = Path.GetFileNameWithoutExtension(psd1Path);
        // "7D2D-7DashesToDie" -> "7dashestodie"
        if (leaf.StartsWith("7D2D-", StringComparison.OrdinalIgnoreCase)) leaf = leaf[5..];
        return leaf.ToLowerInvariant();
    }

    private static JsonDocument ReadPsd1(string path)
    {
        if (!File.Exists(path)) throw new ConfigException($"Keine .psd1 unter '{path}'.");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            $"Import-PowerShellDataFile -LiteralPath '{path.Replace("'", "''")}' | ConvertTo-Json -Depth 8 -Compress");

        using var p = Process.Start(psi) ?? throw new ConfigException("powershell.exe liess sich nicht starten.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new ConfigException($"'{path}' liess sich nicht lesen: {stderr.Trim()}");

        try { return JsonDocument.Parse(stdout); }
        catch (JsonException ex) { throw new ConfigException($"Konvertierung von '{path}' unlesbar: {ex.Message}"); }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    /// <summary>
    /// ConvertTo-Json turns a single-element PowerShell array into a bare scalar,
    /// so a one-entry list must be accepted as a string, not only as an array.
    /// </summary>
    private static List<string> StrList(JsonElement e, string name)
    {
        var result = new List<string>();
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return result;

        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) result.Add(s!);
        }
        else if (v.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s!);
            }
        }
        return result;
    }
}
