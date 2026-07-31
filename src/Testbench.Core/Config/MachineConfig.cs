using System.Text.Json.Serialization;

namespace Testbench.Core.Config;

/// <summary>
/// Everything that describes THIS MACHINE, not a mod: where the isolated game
/// installations live, where their user data goes, how the shared GamePrefs are
/// protected, and which foreign mods are available as dependencies.
///
/// Split off from the mod configuration on purpose. In the PowerShell testbench
/// both lived in the same .psd1, so every mod carried its own copy of
/// GameRoot/UserDataRoot/PrefsKey/Dependencies and any of those copies could
/// silently go stale.
/// </summary>
public sealed class MachineConfig
{
    /// <summary>
    /// Language of every message the tool prints, by the names 7DTD itself uses
    /// ("english", "german", "schinese"). "auto" follows the system language and
    /// falls back to English.
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>Isolated installations live under &lt;GameRoot&gt;\7DTD-&lt;version&gt;.</summary>
    public string GameRoot { get; set; } = "";

    /// <summary>
    /// Per-version user data: &lt;UserDataRoot&gt;\&lt;version&gt; for headless runs,
    /// &lt;UserDataRoot&gt;\&lt;version&gt;-gui for GUI runs. Kept between runs so the
    /// Navezgane world does not have to be regenerated every time.
    /// </summary>
    public string UserDataRoot { get; set; } = "";

    /// <summary>Logs and markdown reports.</summary>
    public string ResultRoot { get; set; } = "";

    /// <summary>Run records, the lock file and the tool's own state.</summary>
    public string StateRoot { get; set; } = "";

    public PrefsConfig Prefs { get; set; } = new();

    /// <summary>Every game version the bench knows about.</summary>
    public List<GameVersion> Versions { get; set; } = new();

    /// <summary>
    /// Paths to the registered testbench.mod.json files. Each mod's config lives
    /// in its own repository; this list is only how the bench finds them, so
    /// "tb mods" can answer without anyone typing a path.
    /// </summary>
    public List<string> ModConfigs { get; set; } = new();

    /// <summary>
    /// Foreign mods a mod under test can depend on, addressed by key
    /// ("gears", "quartz"). Defined once here, referenced by key from every
    /// mod configuration.
    /// </summary>
    public Dictionary<string, DependencyDef> DependencyLibrary { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Mods allowed to stay in a test installation. Everything else is moved to
    /// _Mods-disabled before a run so a failure provably belongs to the mod
    /// under test. The mod itself and its dependencies are added automatically.
    /// </summary>
    public List<string> KeepMods { get; set; } = new() { "0_TFP_Harmony" };

    /// <summary>Abort a headless run if the server is not up within this time.</summary>
    public int TimeoutSeconds { get; set; } = 420;

    /// <summary>
    /// Log line at which startup counts as complete. Deliberately late.
    ///
    /// Do NOT use "Started Telnet": it appears after ~2.7 s, long before the
    /// XMLs are parsed, and would report every run as green having tested
    /// essentially nothing.
    /// </summary>
    public string ReadyPattern { get; set; } = "INF StartGame done";

    /// <summary>
    /// Patterns that end a run immediately as FATAL. Kept in the config so a
    /// new build with different wording does not need a code change.
    /// </summary>
    public string FatalPattern { get; set; } =
        @"(?i)(Fatal error|System\.(NullReference|Type|Missing|Argument)\w*Exception|HarmonyException)";

    /// <summary>
    /// XML trouble. The patterns come from the real message texts in
    /// Assembly-CSharp (found via a UTF-16 byte search), not from guessing.
    /// Successful patches are not logged by 7DTD at all, so zero hits means
    /// "nothing broke", never "verified".
    /// </summary>
    public string XmlProblemPattern { get; set; } =
        @"XML loader:|XML patch for .+ did not apply|XML\.Patch \(.+Patch type|No element <\w+> found!";

    /// <summary>
    /// Known noise that shows up on every dedicated start and has nothing to do
    /// with any mod. Hits are counted and reported as "ignored", never silently
    /// dropped: a green tick that got there by filtering means nothing.
    /// </summary>
    public List<string> IgnorePatterns { get; set; } = new()
    {
        @"\[EOS\]",
        @"\[Discord\]",
        "Retrieving remote news file",
    };

    /// <summary>
    /// A configuration whose paths all sit under one folder. This is what
    /// "tb init" writes, and it is deliberately relative to where the tool was
    /// unpacked: nothing here may assume a particular drive letter or user name.
    /// </summary>
    public static MachineConfig ForBenchRoot(string benchRoot, string? gameRoot = null)
    {
        benchRoot = System.IO.Path.GetFullPath(benchRoot);
        return new MachineConfig
        {
            GameRoot = gameRoot is null ? System.IO.Path.Combine(benchRoot, "Games") : System.IO.Path.GetFullPath(gameRoot),
            UserDataRoot = System.IO.Path.Combine(benchRoot, "UserData"),
            ResultRoot = System.IO.Path.Combine(benchRoot, "results"),
            StateRoot = System.IO.Path.Combine(benchRoot, "state"),
            Prefs = new PrefsConfig { BackupDir = System.IO.Path.Combine(benchRoot, "prefs-backup") },
        };
    }

    /// <summary>Absolute path of the installation folder for a version id.</summary>
    public string GameDir(string versionId)
    {
        var v = FindVersion(versionId);
        if (v is not null && !string.IsNullOrWhiteSpace(v.Path)) return v.Path!;
        return System.IO.Path.Combine(GameRoot, $"7DTD-{versionId}");
    }

    public GameVersion? FindVersion(string versionId) =>
        Versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ready pattern for a version, with the per-version override applied.</summary>
    public string ReadyPatternFor(string versionId)
    {
        var v = FindVersion(versionId);
        return string.IsNullOrWhiteSpace(v?.ReadyPattern) ? ReadyPattern : v!.ReadyPattern!;
    }
}

public sealed class GameVersion
{
    /// <summary>What the user types: "3.0.1".</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Installation folder. Empty means &lt;GameRoot&gt;\7DTD-&lt;Id&gt;, which is the
    /// normal case; set it only for an installation that sits somewhere else.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Steam branch this was pulled from, for reproducing the install.</summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Identity version of MicrosoftGame.Config at the time this version was
    /// registered, e.g. "1.301.4.0". Recorded so Doctor can notice when the
    /// installation has become a different build while keeping its folder name,
    /// which is what happens when Steam updates it in place.
    /// </summary>
    public string? Build { get; set; }

    /// <summary>Per-version override; empty means the global ReadyPattern.</summary>
    public string? ReadyPattern { get; set; }

    /// <summary>Free text, e.g. "live version, played daily".</summary>
    public string? Notes { get; set; }
}

public sealed class DependencyDef
{
    /// <summary>
    /// Folder name inside Mods. Load order in 7DTD comes from the folder name
    /// ("0-", "00000-"), so these names are load-bearing and must not be
    /// prettified.
    /// </summary>
    public string Folder { get; set; } = "";

    /// <summary>Where the mod is mirrored from before every run.</summary>
    public string Source { get; set; } = "";

    /// <summary>Other dependency keys this one needs (Gears needs Quartz).</summary>
    public List<string> Requires { get; set; } = new();

    /// <summary>Human label for messages; falls back to the folder name.</summary>
    public string? DisplayName { get; set; }
}

public sealed class PrefsConfig
{
    /// <summary>
    /// 7DTD stores its options as Unity PlayerPrefs in the registry, OUTSIDE
    /// the user data folder, shared by every installation, and NOT redirectable
    /// by any launch parameter. A freshly unpacked build writes its defaults
    /// there on first start and silently overwrites the tuned live settings.
    /// </summary>
    public string Key { get; set; } = @"HKCU\Software\The Fun Pimps\7 Days To Die";

    public string BackupDir { get; set; } = "";

    /// <summary>Reference export of your own settings, for comparison and manual repair.</summary>
    public string? GoldenReg { get; set; }

    /// <summary>
    /// Optional: single settings to check by name after the restore, for anyone
    /// who has tuned something they refuse to lose.
    ///
    /// Empty by default, and that costs nothing: every restore is verified
    /// generically by exporting the key again and comparing it against the
    /// backup, see <see cref="Prefs.PrefsGuard.RoundTrip"/>. These entries only
    /// add "and this particular value must read exactly this".
    ///
    /// Unity mangles PlayerPrefs value names into "&lt;name&gt;_h&lt;hash&gt;", which is
    /// why they are matched as a prefix, not compared literally.
    /// </summary>
    public Dictionary<string, int> GoldenValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
