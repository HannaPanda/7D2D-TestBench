using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Testbench.Core.Config;

/// <summary>Where a candidate's version id was taken from.</summary>
public enum IdSource
{
    /// <summary>Nothing said which version this is.</summary>
    None,

    /// <summary>MicrosoftGame.Config of the installation itself.</summary>
    GameConfig,

    /// <summary>Only the folder name.</summary>
    FolderName,
}

/// <summary>
/// One folder that looks like a 7DTD installation, with everything that is
/// knowable about it without starting it.
/// </summary>
public sealed class VersionCandidate
{
    public required string Dir { get; init; }

    public bool HasExe { get; init; }

    /// <summary>Without Mods\0_TFP_Harmony no DLL mod loads, so this is worth knowing early.</summary>
    public bool HasHarmony { get; init; }

    /// <summary>Identity version from MicrosoftGame.Config, e.g. "1.301.4.0".</summary>
    public string? Build { get; init; }

    /// <summary>Version derived from <see cref="Build"/>.</summary>
    public string? IdFromBuild { get; init; }

    /// <summary>Version derived from the folder name.</summary>
    public string? IdFromFolder { get; init; }

    /// <summary>Mod folders already lying in the installation.</summary>
    public List<string> Mods { get; init; } = new();

    /// <summary>Version id this folder is already registered under, if any.</summary>
    public string? RegisteredAs { get; set; }

    public string FolderName => Path.GetFileName(Dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public bool Registered => RegisteredAs is not null;

    /// <summary>
    /// The installation's own statement wins over the folder name. A folder can be
    /// renamed, copied or left behind after a Steam update; MicrosoftGame.Config
    /// ships with the build.
    /// </summary>
    public string? ProposedId => IdFromBuild ?? IdFromFolder;

    public IdSource Source =>
        IdFromBuild is not null ? IdSource.GameConfig
        : IdFromFolder is not null ? IdSource.FolderName
        : IdSource.None;

    /// <summary>
    /// Folder name and installation disagree. That is the interesting case: it
    /// means the folder is not what its name claims, usually because Steam
    /// updated it in place. Registering it under the folder's name would make
    /// every later report lie about which version was tested.
    /// </summary>
    public bool Mismatch =>
        IdFromBuild is not null && IdFromFolder is not null &&
        !string.Equals(IdFromBuild, IdFromFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when this folder is the installation the player actually plays. Such a
    /// folder must never become a test version: a run would sweep their mods away.
    /// </summary>
    public bool IsLiveInstall { get; init; }

    /// <summary>Anything that makes this folder unusable as a test version.</summary>
    public bool Blocked => !HasExe || IsLiveInstall;

    /// <summary>One line for a table or a list row.</summary>
    public string Explain()
    {
        if (!HasExe) return I18n.Loc.T("scan.noExe");
        if (IsLiveInstall) return I18n.Loc.T("scan.isLiveInstall");
        if (Mismatch) return I18n.Loc.T("scan.mismatch", IdFromFolder!, IdFromBuild!, Build ?? "");

        var basis = Source switch
        {
            IdSource.GameConfig => I18n.Loc.T("scan.fromGameConfig", Build ?? ""),
            IdSource.FolderName => I18n.Loc.T("scan.fromFolderName"),
            _ => I18n.Loc.T("scan.unknown"),
        };
        if (Registered) basis = I18n.Loc.T("scan.registeredAs", RegisteredAs!, basis);
        if (!HasHarmony) basis += I18n.Loc.T("scan.noHarmony");
        return basis;
    }
}

/// <summary>
/// Finds 7DTD installations on disk and works out which version they are.
///
/// This exists so a new version does not have to be typed into a config file.
/// Nothing here starts the game, so every answer is a reading of files, and the
/// only fully reliable statement about a build is still the "INF Version:" line
/// of an actual run. That is why the guessed id is recorded together with the
/// build string: <see cref="Diagnostics.Doctor"/> can then notice when an
/// installation has silently become a different one.
/// </summary>
public static class VersionScanner
{
    public const string ExeName = "7DaysToDie.exe";

    /// <summary>
    /// The version is not written as plain text anywhere in an installation
    /// (Assembly-CSharp.dll builds the string at runtime). MicrosoftGame.Config
    /// is: its Identity version encodes the version as
    /// 1.&lt;major&gt;&lt;minor, two digits&gt;.&lt;build&gt;.0, so 3.0.1 ships as
    /// "1.301.4.0", 3.1.0 as "1.310.14.0" and 2.6 as "1.206.14.0". How that minor
    /// is written out is a rule of the game's own, see <see cref="IdFromBuild"/>.
    /// </summary>
    public const string GameConfigName = "MicrosoftGame.Config";

    /// <summary>Folders that are never an installation of their own and cost time to walk.</summary>
    private static readonly string[] SkipDirs =
    {
        "Mods", Deploy.ModDeployer.TrashFolder, Deploy.ModDeployer.LegacyTrashFolder,
        "Data", "7DaysToDie_Data", "EasyAntiCheat",
        "logs", "Saves", ".git", "node_modules", "$RECYCLE.BIN", "System Volume Information",
    };

    public static bool LooksLikeInstall(string dir) =>
        File.Exists(Path.Combine(dir, ExeName));

    /// <summary>
    /// Walks <paramref name="root"/> for installations. Stops descending as soon
    /// as a folder is one, so the thousands of files inside a game are never
    /// enumerated.
    /// </summary>
    public static List<VersionCandidate> Scan(string root, MachineConfig machine, int maxDepth = 2)
    {
        var hits = new List<VersionCandidate>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return hits;

        Walk(Path.GetFullPath(root), 0);

        return hits
            .OrderBy(c => c.ProposedId ?? c.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Walk(string dir, int depth)
        {
            if (LooksLikeInstall(dir)) { hits.Add(Inspect(dir, machine)); return; }
            if (depth >= maxDepth) return;
            foreach (var sub in SubDirs(dir)) Walk(sub, depth + 1);
        }
    }

    private static IEnumerable<string> SubDirs(string dir)
    {
        string[] subs;
        try { subs = Directory.GetDirectories(dir); }
        catch (Exception) { return Array.Empty<string>(); }

        return subs.Where(s =>
        {
            var name = Path.GetFileName(s);
            if (SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
            // A junction can point back up and turn the walk into a loop.
            try { return !new DirectoryInfo(s).Attributes.HasFlag(FileAttributes.ReparsePoint); }
            catch (Exception) { return false; }
        });
    }

    /// <summary>Everything knowable about one folder, whether or not it is an installation.</summary>
    public static VersionCandidate Inspect(string dir, MachineConfig? machine = null)
    {
        dir = Path.GetFullPath(dir);
        var build = ReadBuild(dir);

        var candidate = new VersionCandidate
        {
            Dir = dir,
            HasExe = LooksLikeInstall(dir),
            HasHarmony = Directory.Exists(Path.Combine(dir, "Mods", "0_TFP_Harmony")),
            Build = build,
            IdFromBuild = build is null ? null : IdFromBuild(build),
            IdFromFolder = IdFromFolder(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))),
            Mods = ModFolders(dir),
            IsLiveInstall = Diagnostics.SteamLocator.IsLiveInstall(dir),
        };

        if (machine is not null) candidate.RegisteredAs = RegisteredAs(machine, dir);
        return candidate;
    }

    /// <summary>Version id a machine config already uses for this folder, or null.</summary>
    public static string? RegisteredAs(MachineConfig machine, string dir) =>
        machine.Versions.FirstOrDefault(v => ConfigStore.PathsEqual(machine.GameDir(v.Id), dir))?.Id;

    /// <summary>Identity version out of MicrosoftGame.Config, e.g. "1.301.4.0".</summary>
    public static string? ReadBuild(string dir)
    {
        var path = Path.Combine(dir, GameConfigName);
        if (!File.Exists(path)) return null;

        try
        {
            var identity = XDocument.Load(path).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Identity");
            var value = identity?.Attribute("Version")?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        catch (Exception)
        {
            // Malformed XML is not a reason to refuse the folder; fall through.
        }

        try
        {
            var m = Regex.Match(File.ReadAllText(path), @"<Identity\b[^>]*\bVersion\s*=\s*""(?<v>[\d.]+)""");
            return m.Success ? m.Groups["v"].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// "1.301.4.0" to "3.0.1", "1.206.14.0" to "2.6". The middle three digits are
    /// the major digit followed by a two-digit minor - but whether that minor is
    /// written as one number or split into two is the game's own rule, and it is
    /// not the same on both lines. VersionInformation's constructor:
    /// <code>
    /// if (releaseType == Alpha || (releaseType == V &amp;&amp; major &lt; 3))
    ///     "V {Major}.{Minor} (b{Build})"                  // V 2.6 (b14)
    /// else
    ///     "V {Major}.{Minor / 10}.{Minor % 10} (b{Build})" // V 3.0.1 (b4)
    /// </code>
    /// Splitting unconditionally is what the first version of this method did, and
    /// it decoded V 2.6 as "2.0.6" - a version that has never existed, attached to
    /// an installation that was really there. That is the failure this whole class
    /// is here to prevent, so the rule is mirrored instead of approximated.
    ///
    /// Only the three-digit form is decoded; anything else returns null instead of
    /// a plausible-looking wrong answer, and the folder name takes over.
    /// </summary>
    public static string? IdFromBuild(string build)
    {
        // [0-9] rather than \d: .NET's \d also matches non-ASCII digits, which
        // int.Parse would then be handed.
        var m = Regex.Match(build.Trim(), @"^[0-9]+\.([0-9])([0-9][0-9])\.");
        if (!m.Success) return null;

        var major = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

        return major >= 3
            ? $"{major}.{minor / 10}.{minor % 10}"
            : $"{major}.{minor}";
    }

    /// <summary>
    /// Digs a version out of a folder name: "7DTD-3.0.1", "v3.1.0",
    /// "7 Days To Die 3.0.1". The last number wins, so the "7" in "7DTD" cannot
    /// become part of it.
    /// </summary>
    public static string? IdFromFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        var matches = Regex.Matches(folderName, @"\d+\.\d+(?:\.\d+)*");
        return matches.Count == 0 ? null : matches[^1].Value;
    }

    private static List<string> ModFolders(string dir)
    {
        var mods = Path.Combine(dir, "Mods");
        if (!Directory.Exists(mods)) return new List<string>();
        try
        {
            return Directory.GetDirectories(mods).Select(Path.GetFileName).OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }
}
