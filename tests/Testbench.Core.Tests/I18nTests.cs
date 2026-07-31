using System.Text.RegularExpressions;
using Testbench.Core.I18n;

namespace Testbench.Core.Tests;

/// <summary>
/// The translation system's safety net.
///
/// A missing text is the kind of defect nobody notices until a stranger opens the
/// window in their language and reads a key where a sentence should be. These
/// tests make that impossible: every key used in the code has to exist in
/// English, every shipped catalog has to have every English key, and no
/// translation may lose or invent a placeholder.
/// </summary>
public sealed class I18nTests
{
    private static readonly Regex Placeholder = new(@"\{(?<n>\d+)[^}]*\}", RegexOptions.Compiled);

    [Fact]
    public void Every_key_used_in_the_source_exists_in_english()
    {
        var english = Loc.Catalog(Loc.English);
        var used = KeysUsedInSource();

        Assert.NotEmpty(used);
        var unknown = used.Where(k => !english.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.Empty(unknown);
    }

    [Fact]
    public void English_and_german_are_complete()
    {
        Assert.Empty(Loc.MissingKeys(Loc.English));
        Assert.Empty(Loc.MissingKeys("german"));
    }

    /// <summary>
    /// Every catalog that ships has to be complete. A language nobody has
    /// translated yet simply has no file and does not appear in the menu, which is
    /// the honest state; a half-translated file that silently shows English lines
    /// in between is not.
    /// </summary>
    [Fact]
    public void Every_shipped_catalog_is_complete()
    {
        var incomplete = Loc.Available()
            .Select(code => (code, missing: Loc.MissingKeys(code)))
            .Where(x => x.missing.Count > 0)
            .Select(x => $"{x.code}: {x.missing.Count} missing, e.g. {x.missing[0]}")
            .ToList();

        Assert.Empty(incomplete);
    }

    [Fact]
    public void No_catalog_invents_keys_english_does_not_have()
    {
        var english = Loc.Catalog(Loc.English);
        var strays = new List<string>();

        foreach (var code in Loc.Available())
            strays.AddRange(Loc.Catalog(code).Keys
                .Where(k => !english.ContainsKey(k))
                .Select(k => $"{code}: {k}"));

        Assert.Empty(strays);
    }

    /// <summary>
    /// A translation that drops a {0} loses the file name, the version or the PID
    /// the message was about. One that adds a {3} throws a FormatException at the
    /// worst possible moment.
    /// </summary>
    [Fact]
    public void Placeholders_match_english()
    {
        var english = Loc.Catalog(Loc.English);
        var wrong = new List<string>();

        foreach (var code in Loc.Available().Where(c => c != Loc.English))
        {
            var catalog = Loc.Catalog(code);
            foreach (var (key, englishText) in english)
            {
                if (!catalog.TryGetValue(key, out var text)) continue;
                var expected = Indices(englishText);
                var actual = Indices(text);
                if (!expected.SetEquals(actual))
                    wrong.Add($"{code}/{key}: expected {string.Join(",", expected.Order())}, " +
                              $"got {string.Join(",", actual.Order())}");
            }
        }

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("de-DE", "german")]
    [InlineData("de-AT", "german")]
    [InlineData("en-GB", "english")]
    [InlineData("pt-BR", "brazilian")]
    [InlineData("pt-PT", "brazilian")]
    [InlineData("zh-Hans", "schinese")]
    [InlineData("zh-CN", "schinese")]
    [InlineData("zh-TW", "tchinese")]
    [InlineData("ko-KR", "koreana")]
    public void Cultures_map_to_the_games_own_language_names(string culture, string expected) =>
        Assert.Equal(expected, Loc.FromCulture(culture));

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("hu")]
    public void A_language_7dtd_does_not_have_maps_to_nothing(string culture) =>
        Assert.Null(Loc.FromCulture(culture));

    [Theory]
    [InlineData("klingon")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unusable_request_ends_up_at_english(string? requested)
    {
        // Never an exception: a wrong entry in a config file must not stop a test
        // run over a matter of wording.
        var code = Loc.Resolve(requested);
        Assert.Contains(code, Loc.Available());
    }

    [Fact]
    public void An_unknown_key_shows_the_key_instead_of_nothing()
    {
        // Ugly on purpose: a blank label is invisible, "gui.nope" is a bug report.
        Assert.Equal("gui.nope", Loc.T("gui.nope"));
    }

    [Fact]
    public void A_broken_placeholder_does_not_take_a_run_down()
    {
        var text = Loc.T("status.ok", "unused argument");
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void The_german_status_labels_are_the_ones_the_old_scripts_printed()
    {
        // Parity: old and new reports have to be comparable without translating.
        var german = Loc.Catalog("german");
        Assert.Equal("MOD NICHT GELADEN", german["status.modNotLoaded"]);
        Assert.Equal("ABHAENGIGKEIT FEHLT", german["status.dependencyMissing"]);
        Assert.Equal("XML-WARNUNGEN", german["status.xmlWarnings"]);
        Assert.Equal("UNGETESTET", german["status.untested"]);
    }

    // ---- helpers -----------------------------------------------------------

    private static HashSet<int> Indices(string text) =>
        Placeholder.Matches(text).Select(m => int.Parse(m.Groups["n"].Value)).ToHashSet();

    /// <summary>
    /// Every key the code asks for: Loc.T("..."), the "status.x" keys of
    /// RunStatusText, and the {local:Tr ...} of the XAML.
    /// </summary>
    private static List<string> KeysUsedInSource()
    {
        var src = FindSourceRoot();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        var callPattern = new Regex(@"Loc\.T\(\s*""(?<k>[^""]+)""", RegexOptions.Compiled);
        var literalPattern = new Regex(
            @"""(?<k>(cli|gui|col|doctor|error|report|run|scan|prefs|deploy|dep|import|launcher|store|status|visual)\.[a-zA-Z0-9.]+)""",
            RegexOptions.Compiled);
        var xamlPattern = new Regex(@"\{local:Tr\s+(?<k>[^}\s]+)\s*\}", RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);

            foreach (Match m in callPattern.Matches(text)) keys.Add(m.Groups["k"].Value);

            // Keys reached through a conditional, e.g. Loc.T(ok ? "a" : "b").
            // They are only counted when they look like a key AND the catalog is
            // the only place such a string would come from.
            foreach (Match m in literalPattern.Matches(text))
            {
                var candidate = m.Groups["k"].Value;
                if (Loc.Catalog(Loc.English).ContainsKey(candidate) || candidate.StartsWith("status.")) keys.Add(candidate);
            }
        }

        foreach (var file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in xamlPattern.Matches(File.ReadAllText(file)))
                keys.Add(m.Groups["k"].Value);

        return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Walks up from the test assembly to the repository's src folder. The test
    /// reads the real sources on purpose: a key that exists only in code is
    /// exactly the defect being looked for, and no build step could see it.
    /// </summary>
    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(src) && File.Exists(Path.Combine(dir.FullName, "Testbench.slnx"))) return src;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Repository not found above " + AppContext.BaseDirectory +
            " - this test reads the real source files.");
    }
}
