namespace Testbench.Core.Model;

/// <summary>
/// What a log says, independent of how the run was started. Produced by the pure
/// <see cref="Run.LogAnalyzer"/>, which makes every counter here reproducible
/// from a stored log file without launching anything.
/// </summary>
public sealed class LogAnalysis
{
    public int TotalLines { get; set; }

    public bool ModLoaded { get; set; }
    public bool HarmonyApplied { get; set; }

    /// <summary>
    /// Everything the game said about itself up to the first comma, e.g.
    /// "V 3.1.0 (b14) Compatibility Version: V 3.1.0". The build number matters
    /// when a branch gets re-published under the same version number, which is
    /// why this keeps more than Invoke-SmokeTest.ps1 did (it cut after "V 3.1.0").
    /// </summary>
    public string GameVersion { get; set; } = "";

    /// <summary>Just the version number, e.g. "V 3.1.0".</summary>
    public string GameVersionShort =>
        System.Text.RegularExpressions.Regex.Match(GameVersion, @"^V [\d.]+") is { Success: true } m
            ? m.Value
            : GameVersion;

    public int Errors { get; set; }
    public int Exceptions { get; set; }
    public int XmlProblems { get; set; }

    /// <summary>
    /// Lines removed by IgnorePatterns. Counted and reported rather than dropped:
    /// silent filtering is how a green tick stops meaning anything.
    /// </summary>
    public int Ignored { get; set; }

    /// <summary>Mod names that appeared in a "[MODS] Loaded Mod: x" line.</summary>
    public List<string> LoadedMods { get; set; } = new();

    /// <summary>First few relevant lines, for showing a verdict without opening the log.</summary>
    public List<string> Highlights { get; set; } = new();
}
