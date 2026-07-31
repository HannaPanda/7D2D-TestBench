using Testbench.Core.Model;
using Testbench.Core.Run;

namespace Testbench.Core.Tests;

/// <summary>
/// One synthetic case per rule. The parity tests prove the analyzer agrees with the
/// old bench on real logs; these pin down WHY, so a future change breaks a named
/// rule instead of an opaque number.
/// </summary>
public class LogAnalyzerRuleTests
{
    private static LogAnalyzer.Input In(
        string[] lines,
        string modName = "MyMod",
        string[]? ignore = null,
        string xml = "XML loader:",
        string harmony = "Harmony patches applied") =>
        new(lines, modName, ignore ?? Array.Empty<string>(), xml, harmony, null);

    [Fact]
    public void Loaded_mod_line_is_parsed_without_the_version_suffix()
    {
        var a = LogAnalyzer.Analyze(In(new[]
        {
            "2026-07-31T21:52:15 1.795 INF [MODS]     Loaded Mod: MyMod (1.2.3.4)",
        }));

        Assert.Equal(new[] { "MyMod" }, a.LoadedMods);
        Assert.True(a.ModLoaded);
    }

    [Fact]
    public void Mod_name_with_spaces_survives()
    {
        var a = LogAnalyzer.Analyze(In(
            new[] { "INF [MODS]     Loaded Mod: 7 Dashes to Die (1.1.0.0)" },
            modName: "7 Dashes to Die"));

        Assert.True(a.ModLoaded);
    }

    /// <summary>
    /// PowerShell's -match is case-insensitive by default and every pattern in the
    /// old config was written under that assumption.
    /// </summary>
    [Fact]
    public void Matching_is_case_insensitive_like_powershell()
    {
        var a = LogAnalyzer.Analyze(In(
            new[] { "INF [MODS]     Loaded Mod: mymod (1.0)" },
            modName: "MyMod"));

        Assert.True(a.ModLoaded);
    }

    [Fact]
    public void Ignored_lines_are_counted_not_dropped()
    {
        var a = LogAnalyzer.Analyze(In(
            new[]
            {
                "INF [EOS] DeviceId access credentials already exist",
                "INF [Discord] whatever",
                "ERR something real",         // no surrounding spaces: not an ERR hit
                "2026 1.0 ERR  something",    // " ERR " does match here
            },
            ignore: new[] { @"\[EOS\]", @"\[Discord\]" }));

        Assert.Equal(4, a.TotalLines);
        Assert.Equal(2, a.Ignored);
        Assert.Equal(1, a.Errors);
    }

    /// <summary>
    /// Noise is removed before counting, so an ignored line that also looks like an
    /// error must not raise the error count. That is what makes "ignored" a number
    /// worth printing rather than a silent filter.
    /// </summary>
    [Fact]
    public void Noise_is_removed_before_counting_errors()
    {
        var a = LogAnalyzer.Analyze(In(
            new[] { "2026 1.0 ERR  [Discord] connection failed" },
            ignore: new[] { @"\[Discord\]" }));

        Assert.Equal(1, a.Ignored);
        Assert.Equal(0, a.Errors);
    }

    [Fact]
    public void Exceptions_count_both_spellings()
    {
        var a = LogAnalyzer.Analyze(In(new[]
        {
            "2026 1.0 EXC  NullReferenceException",
            "2026 1.0 INF  System.InvalidOperationException: nope",
        }));

        Assert.Equal(2, a.Exceptions);
    }

    [Fact]
    public void A_broken_pattern_does_not_take_the_run_down()
    {
        // An unbalanced group would throw if the pattern were compiled naively. The
        // analyzer runs after the game has already been started and stopped, so a
        // typo in a config file must not lose the result.
        var a = LogAnalyzer.Analyze(In(new[] { "irgendwas" }, xml: "XML loader: ([unclosed"));

        Assert.Equal(0, a.XmlProblems);
    }

    [Fact]
    public void Missing_evidence_is_null_when_no_pattern_is_configured()
    {
        // No patterns means "there is no log-provable evidence for this mod", which
        // must not be booked as passed.
        Assert.Null(LogAnalyzer.MissingEvidence(new[] { "x" }, Array.Empty<string>()));
    }

    [Fact]
    public void Missing_evidence_lists_only_what_is_absent()
    {
        var missing = LogAnalyzer.MissingEvidence(
            new[] { "INF dash key registered" },
            new[] { "dash key registered", "dash added to the controller bindings list" });

        Assert.NotNull(missing);
        Assert.Equal(new[] { "dash added to the controller bindings list" }, missing!);
    }

    // ---- the verdict order -------------------------------------------------

    private static LogAnalysis Clean => new()
    {
        ModLoaded = true, HarmonyApplied = true, TotalLines = 10,
    };

    [Fact]
    public void Fatal_outranks_everything()
    {
        var status = LogAnalyzer.Verdict(Clean, Array.Empty<DependencyResult>(), true, StopReason.Fatal);
        Assert.Equal(RunStatus.Fatal, status);
    }

    [Fact]
    public void A_mod_that_never_loaded_outranks_error_counts()
    {
        var a = Clean;
        a.ModLoaded = false;
        a.Errors = 5;

        Assert.Equal(RunStatus.ModNotLoaded,
            LogAnalyzer.Verdict(a, Array.Empty<DependencyResult>(), true, StopReason.Ready));
    }

    /// <summary>
    /// Provided is not loaded. A dependency that quietly failed to come up makes any
    /// test of the integration with it worthless, so it outranks the counters.
    /// </summary>
    [Fact]
    public void A_missing_dependency_outranks_error_counts()
    {
        var deps = new[] { new DependencyResult { Key = "gears", Folder = "00000-Gears", Problem = "nicht geladen" } };

        Assert.Equal(RunStatus.DependencyMissing,
            LogAnalyzer.Verdict(Clean, deps, true, StopReason.Ready));
    }

    [Fact]
    public void Harmony_is_only_required_when_the_mod_has_a_dll()
    {
        var a = Clean;
        a.HarmonyApplied = false;

        Assert.Equal(RunStatus.HarmonyMissing,
            LogAnalyzer.Verdict(a, Array.Empty<DependencyResult>(), requireHarmony: true, StopReason.Ready));
        Assert.Equal(RunStatus.Ok,
            LogAnalyzer.Verdict(a, Array.Empty<DependencyResult>(), requireHarmony: false, StopReason.Ready));
    }

    [Fact]
    public void Xml_warnings_rank_below_errors_and_above_ok()
    {
        var a = Clean;
        a.XmlProblems = 1;
        Assert.Equal(RunStatus.XmlWarnings, LogAnalyzer.Verdict(a, Array.Empty<DependencyResult>(), true, StopReason.Ready));

        a.Errors = 1;
        Assert.Equal(RunStatus.Errors, LogAnalyzer.Verdict(a, Array.Empty<DependencyResult>(), true, StopReason.Ready));
    }

    [Fact]
    public void A_timeout_is_not_ok_even_with_a_clean_log()
    {
        Assert.Equal(RunStatus.Timeout,
            LogAnalyzer.Verdict(Clean, Array.Empty<DependencyResult>(), true, StopReason.Timeout));
    }

    // ---- line splitting ----------------------------------------------------

    /// <summary>
    /// Get-Content does not produce an empty last line for a file that ends with a
    /// newline. Every counter of the old bench was measured that way.
    /// </summary>
    [Fact]
    public void Trailing_newline_does_not_add_a_line()
    {
        Assert.Equal(2, GameLauncher.SplitLines("a\r\nb\r\n").Length);
        Assert.Equal(2, GameLauncher.SplitLines("a\nb").Length);
        Assert.Empty(GameLauncher.SplitLines(""));
        Assert.Empty(GameLauncher.SplitLines(null));
    }
}
