using Testbench.Core.Config;
using Testbench.Core.Run;

namespace Testbench.Core.Tests;

/// <summary>
/// The parity proof: real logs from real runs, with the counters the PowerShell
/// bench produced for exactly those files.
///
/// The expected values were not written by hand. They come from a script that
/// reproduces the counting lines of Invoke-SmokeTest.ps1 verbatim
/// (Get-Content, -match, IgnorePatterns joined with |, ' ERR ',
/// ' EXC |Exception:', XmlProblemPattern) run against these four files. If a
/// change to the analyzer moves any number here, it changes what gets reported as
/// tested, and that has to be a deliberate decision.
/// </summary>
public class LogAnalyzerParityTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>The machine defaults the old .psd1 used, so the patterns match too.</summary>
    private static MachineConfig Machine => new();

    private static LogAnalyzer.Input InputFor(string file, string modName) => new(
        GameLauncher.SplitLines(File.ReadAllText(Path.Combine(FixtureDir, file))),
        modName,
        Machine.IgnorePatterns,
        Machine.XmlProblemPattern,
        "Harmony patches applied",
        null);

    [Theory]
    // file, modName, totalLines, modLoaded, harmony, ignored, err, exc, xml, gameVersionShort
    [InlineData("smoke_3.1.0_Survival_2026-07-31_21-52-13.log", "SevenDashesToDie", 1339, true, true, 12, 0, 0, 0, "V 3.1.0")]
    [InlineData("smoke_3.0.0_Creative_2026-07-29_18-33-00.log", "AdamantBlock", 1297, true, true, 12, 0, 0, 1, "V 3.0.0")]
    [InlineData("smoke_3.0.0_Creative_2026-07-29_11-24-21.log", "AdamantBlock", 508, true, true, 4, 0, 0, 0, "V 3.0.0")]
    [InlineData("smoke_3.0.0_Survival_2026-07-31_21-40-35.log", "SevenDashesToDie", 1335, true, true, 11, 0, 0, 0, "V 3.0.0")]
    public void Counters_match_the_powershell_bench(
        string file, string modName, int totalLines, bool modLoaded, bool harmony,
        int ignored, int errors, int exceptions, int xml, string versionShort)
    {
        var a = LogAnalyzer.Analyze(InputFor(file, modName));

        Assert.Equal(totalLines, a.TotalLines);
        Assert.Equal(modLoaded, a.ModLoaded);
        Assert.Equal(harmony, a.HarmonyApplied);
        Assert.Equal(ignored, a.Ignored);
        Assert.Equal(errors, a.Errors);
        Assert.Equal(exceptions, a.Exceptions);
        Assert.Equal(xml, a.XmlProblems);
        Assert.Equal(versionShort, a.GameVersionShort);
    }

    /// <summary>
    /// Invoke-SmokeTest.ps1 cut the version after the number; Start-Gui.ps1 kept
    /// everything up to the first comma. The port keeps the longer form because the
    /// build number distinguishes two publishes of the same version, and exposes
    /// the short form separately.
    /// </summary>
    [Fact]
    public void Full_game_version_keeps_the_build_number()
    {
        var a = LogAnalyzer.Analyze(InputFor("smoke_3.1.0_Survival_2026-07-31_21-52-13.log", "SevenDashesToDie"));

        Assert.Equal("V 3.1.0 (b14) Compatibility Version: V 3.1.0", a.GameVersion);
        Assert.Equal("V 3.1.0", a.GameVersionShort);
    }

    /// <summary>
    /// This log is from the run where the cleanup step had left AdamantBlock in the
    /// Mods folder while SevenDashesToDie was being tested. Both show up, which is
    /// exactly the situation the old bench could not see: it only ever asked
    /// whether ITS mod was loaded.
    /// </summary>
    [Fact]
    public void Every_loaded_mod_is_listed_not_only_the_one_under_test()
    {
        var a = LogAnalyzer.Analyze(InputFor("smoke_3.0.0_Survival_2026-07-31_21-40-35.log", "SevenDashesToDie"));

        Assert.Contains("SevenDashesToDie", a.LoadedMods);
        Assert.Contains("AdamantBlock", a.LoadedMods);
        Assert.Contains("Gears", a.LoadedMods);
        Assert.Contains("Quartz", a.LoadedMods);
        Assert.Contains("TFP_Harmony", a.LoadedMods);
    }

    /// <summary>
    /// The mod name from ModInfo.xml decides, not the folder name. The Creative
    /// variant lives in "AdamantBlock-Creative" and reports itself as
    /// "AdamantBlock"; checking the folder name would report MOD NICHT GELADEN.
    /// </summary>
    [Fact]
    public void Folder_name_is_not_the_reported_name()
    {
        var byFolder = LogAnalyzer.Analyze(InputFor("smoke_3.0.0_Creative_2026-07-29_18-33-00.log", "AdamantBlock-Creative"));
        var byName = LogAnalyzer.Analyze(InputFor("smoke_3.0.0_Creative_2026-07-29_18-33-00.log", "AdamantBlock"));

        Assert.False(byFolder.ModLoaded);
        Assert.True(byName.ModLoaded);
    }

    /// <summary>
    /// The one XML hit in this fixture is the negative control from the config
    /// comments: a deliberately broken xpath. It has to be found, and it is a WRN
    /// line, not an ERR line, so it must not also raise the error count.
    /// </summary>
    [Fact]
    public void Broken_xpath_is_found_as_an_xml_problem_only()
    {
        var a = LogAnalyzer.Analyze(InputFor("smoke_3.0.0_Creative_2026-07-29_18-33-00.log", "AdamantBlock"));

        Assert.Equal(1, a.XmlProblems);
        Assert.Equal(0, a.Errors);
        Assert.Equal(0, a.Exceptions);
    }
}
