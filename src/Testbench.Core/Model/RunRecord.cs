using Testbench.Core.Config;

namespace Testbench.Core.Model;

/// <summary>
/// Verdict of a run. The order of the checks that produce it is load-bearing:
/// a run whose mod never loaded says nothing about error counts, so
/// ModNotLoaded outranks Errors.
/// </summary>
public enum RunStatus
{
    Untested,

    /// <summary>No installation for that version.</summary>
    Missing,

    /// <summary>Something in the setup went wrong; the run never produced a verdict.</summary>
    SetupError,

    /// <summary>A fatal pattern hit the log; the run was cut short.</summary>
    Fatal,

    ModNotLoaded,
    DependencyMissing,
    HarmonyMissing,
    Exceptions,
    Errors,
    XmlWarnings,
    Timeout,
    Ok,
}

/// <summary>
/// Whether a human has looked at the run. Only stage 2 can produce anything
/// other than NotApplicable: -nographics executes nothing graphical, so a
/// headless run has nothing for an eye to confirm.
/// </summary>
public enum VisualState
{
    NotApplicable,

    /// <summary>GUI run finished, nobody has answered the question yet.</summary>
    Pending,

    Ok,
    Failed,
}

public static class RunStatusText
{
    /// <summary>
    /// German labels, identical to the strings the PowerShell scripts printed,
    /// so old reports and new ones can be compared without translating.
    /// </summary>
    public static string De(RunStatus s) => s switch
    {
        RunStatus.Ok => "OK",
        RunStatus.Fatal => "FATAL",
        RunStatus.ModNotLoaded => "MOD NICHT GELADEN",
        RunStatus.DependencyMissing => "ABHAENGIGKEIT FEHLT",
        RunStatus.HarmonyMissing => "HARMONY FEHLT",
        RunStatus.Exceptions => "EXCEPTIONS",
        RunStatus.Errors => "ERRORS",
        RunStatus.XmlWarnings => "XML-WARNUNGEN",
        RunStatus.Timeout => "TIMEOUT",
        RunStatus.Missing => "FEHLT",
        RunStatus.SetupError => "FEHLER",
        _ => "UNGETESTET",
    };

    /// <summary>A status that means "this version is fine as far as this stage can tell".</summary>
    public static bool IsPass(RunStatus s) => s == RunStatus.Ok;
}

public sealed class DependencyResult
{
    public string Key { get; set; } = "";
    public string Folder { get; set; } = "";

    /// <summary>
    /// Name read from the INSTALLED copy's ModInfo.xml. The folder is called
    /// "00000-Gears" while the mod reports itself as "Gears", and only the
    /// installed copy says which name will show up in the log.
    /// </summary>
    public string? ReportedName { get; set; }

    public bool Deployed { get; set; }

    /// <summary>Provided is not loaded. Proven by the log line, not by the copy.</summary>
    public bool Loaded { get; set; }

    public string? Problem { get; set; }
}

public sealed class RunRecord
{
    public string Id { get; set; } = "";

    public DateTimeOffset Started { get; set; }
    public DateTimeOffset? Finished { get; set; }

    public string ModId { get; set; } = "";
    public string ModDisplayName { get; set; } = "";
    public string Variant { get; set; } = "";

    /// <summary>Folder name inside Mods\.</summary>
    public string ModFolder { get; set; } = "";

    /// <summary>ModInfo.xml &lt;Name&gt; of the installed copy: what the log will say.</summary>
    public string ModName { get; set; } = "";

    public string ModVersion { get; set; } = "";

    public string VersionId { get; set; } = "";

    /// <summary>Version the game reported about itself, e.g. "V 3.1.0 (b14)".</summary>
    public string GameVersion { get; set; } = "";

    public TestStage Stage { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Untested;

    /// <summary>German status label, so a stored record is readable without the enum.</summary>
    public string StatusText => RunStatusText.De(Status);

    /// <summary>Why the wait ended: ready, fatal, exited, timeout.</summary>
    public string? StopReason { get; set; }

    public string? Note { get; set; }

    public string LogPath { get; set; } = "";

    public LogAnalysis Analysis { get; set; } = new();

    public List<DependencyResult> Dependencies { get; set; } = new();

    // ---- stage 2 -------------------------------------------------------
    /// <summary>
    /// Null means the mod has no evidence provable from the log; that is reported
    /// as such instead of booking an empty pattern list as passed.
    /// </summary>
    public bool? EvidenceOk { get; set; }

    public string? EvidenceLabel { get; set; }
    public List<string> MissingEvidence { get; set; } = new();

    public string? VisualQuestion { get; set; }
    public VisualState Visual { get; set; } = VisualState.NotApplicable;
    public string? VisualNote { get; set; }
    public DateTimeOffset? VisualAt { get; set; }

    /// <summary>
    /// Both stages passed AND a human confirmed. This is the only thing that may
    /// put a version on a compatibility list.
    /// </summary>
    public bool FullyVerified =>
        Status == RunStatus.Ok && Stage == TestStage.Gui && Visual == VisualState.Ok && EvidenceOk != false;
}
