namespace Testbench.Core.Config;

/// <summary>
/// Everything that is specific to ONE mod. Lives in the mod's own repository
/// (&lt;repo&gt;\test\testbench.mod.json) so it travels with the code it describes.
/// </summary>
public sealed class ModConfig
{
    /// <summary>Short id used on the command line: "sevendashes".</summary>
    public string ModId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Repository root; variant folders are resolved relative to it.</summary>
    public string Repo { get; set; } = "";

    /// <summary>
    /// The deployable folders of this mod. A list, not a fixed
    /// Survival/Creative pair: the PowerShell config forced both keys to exist,
    /// so a mod without editions had to point both at the same folder.
    /// </summary>
    public List<ModVariant> Variants { get; set; } = new();

    /// <summary>Keys into MachineConfig.DependencyLibrary.</summary>
    public List<string> Dependencies { get; set; } = new();

    public Stage1Config Stage1 { get; set; } = new();

    public Stage2Config? Stage2 { get; set; }

    /// <summary>Named combinations so nobody has to assemble one by hand.</summary>
    public List<TestProfile> Profiles { get; set; } = new();

    public ModVariant? FindVariant(string? name)
    {
        if (Variants.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(name)) return Variants[0];
        return Variants.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public TestProfile? FindProfile(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Source folder that gets mirrored into Mods\.</summary>
    public string VariantSource(ModVariant variant) =>
        System.IO.Path.IsPathRooted(variant.Folder)
            ? variant.Folder
            : System.IO.Path.Combine(Repo, variant.Folder);
}

public sealed class ModVariant
{
    /// <summary>Label shown in the GUI and accepted by --variant.</summary>
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Folder name relative to Repo. This is also the folder name inside Mods\,
    /// which is NOT necessarily the name 7DTD reports: that comes from
    /// ModInfo.xml's &lt;Name&gt; and is read from the installed copy.
    /// </summary>
    public string Folder { get; set; } = "";

    public string? Notes { get; set; }
}

/// <summary>
/// Stage 1 is the headless run. Only two things about it are mod-specific.
/// </summary>
public sealed class Stage1Config
{
    /// <summary>
    /// Line that proves the Harmony patches went in.
    ///
    /// This looked like a vanilla marker in the PowerShell bench and is not: the
    /// real line is the MOD's own, e.g.
    /// "INF [7 Dashes to Die] loaded from ...\Mods\SevenDashesToDie, Harmony patches applied".
    /// The default works for both existing mods because both happen to end their
    /// startup message that way. A mod that words it differently has to say so
    /// here, or every run of it would be reported as HARMONY FEHLT.
    /// </summary>
    public string HarmonyPattern { get; set; } = "Harmony patches applied";

    /// <summary>
    /// False for an XML-only mod. The PowerShell bench had no such switch, so a
    /// mod without a DLL could never reach OK.
    /// </summary>
    public bool RequireHarmony { get; set; } = true;

    /// <summary>Additional patterns that abort the run as FATAL.</summary>
    public List<string> ExtraFatalPatterns { get; set; } = new();
}

/// <summary>
/// Stage 2 is the GUI run. Everything here is mod-specific and used to be
/// hardcoded in Start-Gui.ps1, which is why a GUI run of any other mod asked
/// whether a purple crystal block looked right.
/// </summary>
public sealed class Stage2Config
{
    /// <summary>Which lines are worth showing after the run.</summary>
    public string? LogFilter { get; set; }

    /// <summary>
    /// ALL of these must appear in the log or the evidence counts as not
    /// provided. An empty list means "this mod has no evidence provable from the
    /// log", which is reported as such instead of booking an empty pattern as
    /// passed.
    /// </summary>
    public List<string> EvidencePatterns { get; set; } = new();

    public string EvidenceLabel { get; set; } = "Im Log belegt";

    /// <summary>
    /// The question a human has to answer. No log pattern can replace it,
    /// because -nographics executes nothing graphical and no script can judge
    /// whether something feels right.
    /// </summary>
    public string VisualQuestion { get; set; } = "Sah/verhielt sich alles wie erwartet?";
}

public sealed class TestProfile
{
    public string Name { get; set; } = "";

    public string? Variant { get; set; }

    /// <summary>Empty means every version the machine config knows.</summary>
    public List<string> Versions { get; set; } = new();

    /// <summary>Which stages this profile runs.</summary>
    public List<TestStage> Stages { get; set; } = new() { TestStage.Headless };

    public string? Notes { get; set; }
}

public enum TestStage
{
    /// <summary>
    /// Stage 1: dedicated server, -batchmode -nographics. Covers mod loading,
    /// Harmony patches, XML parsing, localization and every startup ERR/EXC.
    /// Covers nothing graphical and no input or menu behaviour.
    /// </summary>
    Headless,

    /// <summary>
    /// Stage 2: the real client with a window. The only way to test texture
    /// atlas injection, menus, keys and feel.
    /// </summary>
    Gui,
}
