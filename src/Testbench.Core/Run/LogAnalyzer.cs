using System.Text.RegularExpressions;
using Testbench.Core.Config;
using Testbench.Core.Model;

namespace Testbench.Core.Run;

/// <summary>
/// Turns a 7DTD log into a verdict. Deliberately pure: no file system, no
/// process, no config writing. Every counter a run reports can therefore be
/// reproduced from a stored log, which is what makes the port checkable against
/// the PowerShell scripts at all.
/// </summary>
public static class LogAnalyzer
{
    /// <summary>
    /// PowerShell's -match is case-insensitive by default and every pattern in
    /// the old config was written under that assumption. Matching case-sensitively
    /// here would silently change which lines count.
    /// </summary>
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex LoadedModRx =
        new(@"\[MODS\]\s+Loaded Mod:\s*(?<name>.+?)(?:\s*\([^)]*\))?\s*$", Opts);

    private static readonly Regex VersionRx =
        new(@"INF Version:\s*(?<v>V [\d.]+[^,]*)", Opts);

    public sealed record Input(
        IReadOnlyList<string> Lines,
        string ModName,
        IReadOnlyList<string> IgnorePatterns,
        string XmlProblemPattern,
        string HarmonyPattern,
        string? HighlightFilter);

    public static LogAnalysis Analyze(Input input)
    {
        var a = new LogAnalysis { TotalLines = input.Lines.Count };

        // ---- what loaded -------------------------------------------------
        foreach (var line in input.Lines)
        {
            var m = LoadedModRx.Match(line);
            if (!m.Success) continue;
            var name = m.Groups["name"].Value.Trim();
            if (name.Length > 0 && !a.LoadedMods.Contains(name, StringComparer.OrdinalIgnoreCase))
                a.LoadedMods.Add(name);
        }

        // Matched against the name from ModInfo.xml, not the folder name. The
        // folder is "SevenDashesToDie" here too, but that is a coincidence, not a
        // rule: the PowerShell bench had the folder hardcoded and reported every
        // other mod as "MOD NICHT GELADEN".
        a.ModLoaded = !string.IsNullOrWhiteSpace(input.ModName) &&
                      a.LoadedMods.Contains(input.ModName, StringComparer.OrdinalIgnoreCase);

        a.HarmonyApplied = !string.IsNullOrWhiteSpace(input.HarmonyPattern) &&
                           AnyMatch(input.Lines, input.HarmonyPattern);

        foreach (var line in input.Lines)
        {
            var m = VersionRx.Match(line);
            if (!m.Success) continue;
            a.GameVersion = m.Groups["v"].Value.Trim();
            break;
        }

        // ---- counting, with the known noise taken out but kept visible ----
        var noise = BuildNoiseRegex(input.IgnorePatterns);
        var relevant = new List<string>(input.Lines.Count);
        foreach (var line in input.Lines)
        {
            if (noise is not null && noise.IsMatch(line)) continue;
            relevant.Add(line);
        }
        a.Ignored = input.Lines.Count - relevant.Count;

        a.Errors = Count(relevant, " ERR ");
        a.Exceptions = Count(relevant, " EXC |Exception:");
        a.XmlProblems = string.IsNullOrWhiteSpace(input.XmlProblemPattern)
            ? 0
            : Count(relevant, input.XmlProblemPattern);

        // ---- a few lines to look at ---------------------------------------
        var filter = string.IsNullOrWhiteSpace(input.HighlightFilter)
            ? (string.IsNullOrWhiteSpace(input.ModName)
                ? @"HarmonyException| EXC | ERR "
                : Regex.Escape(input.ModName) + @"|HarmonyException| EXC | ERR ")
            : input.HighlightFilter!;

        var hi = SafeRegex(filter);
        if (hi is not null)
        {
            foreach (var line in input.Lines)
            {
                if (!hi.IsMatch(line)) continue;
                a.Highlights.Add(line);
                if (a.Highlights.Count >= 20) break;
            }
        }

        return a;
    }

    /// <summary>
    /// Which of the expected evidence patterns are missing. Returns null when the
    /// mod defines no patterns at all, which means "no evidence provable from the
    /// log" and must not be booked as passed.
    /// </summary>
    public static List<string>? MissingEvidence(IReadOnlyList<string> lines, IReadOnlyList<string> patterns)
    {
        var wanted = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (wanted.Count == 0) return null;

        var missing = new List<string>();
        foreach (var p in wanted)
        {
            if (!AnyMatch(lines, p)) missing.Add(p);
        }
        return missing;
    }

    /// <summary>
    /// Decides the verdict. The order matters: a run whose mod never loaded says
    /// nothing about error counts, and a missing dependency invalidates any test
    /// of the integration with it, so both outrank the counters.
    /// </summary>
    public static RunStatus Verdict(
        LogAnalysis a,
        IReadOnlyList<DependencyResult> deps,
        bool requireHarmony,
        string stopReason)
    {
        if (stopReason == StopReason.Fatal) return RunStatus.Fatal;
        if (!a.ModLoaded) return RunStatus.ModNotLoaded;
        if (deps.Any(d => d.Problem is not null)) return RunStatus.DependencyMissing;
        if (requireHarmony && !a.HarmonyApplied) return RunStatus.HarmonyMissing;
        if (a.Exceptions > 0) return RunStatus.Exceptions;
        if (a.Errors > 0) return RunStatus.Errors;
        if (a.XmlProblems > 0) return RunStatus.XmlWarnings;
        if (stopReason == StopReason.Timeout) return RunStatus.Timeout;
        return RunStatus.Ok;
    }

    public static Input InputFor(
        IReadOnlyList<string> lines,
        MachineConfig machine,
        ModConfig mod,
        string modName,
        bool useStage2Filter)
        => new(
            lines,
            modName,
            machine.IgnorePatterns,
            machine.XmlProblemPattern,
            mod.Stage1.HarmonyPattern,
            useStage2Filter ? mod.Stage2?.LogFilter : null);

    private static Regex? BuildNoiseRegex(IReadOnlyList<string> patterns)
    {
        var parts = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : SafeRegex(string.Join("|", parts));
    }

    private static int Count(IReadOnlyList<string> lines, string pattern)
    {
        var rx = SafeRegex(pattern);
        if (rx is null) return 0;
        var n = 0;
        foreach (var line in lines)
        {
            if (rx.IsMatch(line)) n++;
        }
        return n;
    }

    private static bool AnyMatch(IReadOnlyList<string> lines, string pattern)
    {
        var rx = SafeRegex(pattern);
        if (rx is null) return false;
        foreach (var line in lines)
        {
            if (rx.IsMatch(line)) return true;
        }
        return false;
    }

    /// <summary>
    /// A broken pattern in a config file must not take a whole run down after the
    /// game has already been started and stopped. It becomes "no match" and the
    /// caller reports it.
    /// </summary>
    private static Regex? SafeRegex(string pattern)
    {
        try { return new Regex(pattern, Opts); }
        catch (ArgumentException) { return null; }
    }

    public static bool IsValidRegex(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { _ = new Regex(pattern); return true; }
        catch (ArgumentException) { return false; }
    }
}

public static class StopReason
{
    public const string Ready = "ready";
    public const string Fatal = "fatal";
    public const string Exited = "exited";
    public const string Timeout = "timeout";
    public const string Closed = "closed";
}
