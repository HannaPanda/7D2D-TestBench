using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Testbench.Core.I18n;

/// <summary>A language the tool can speak, named the way 7DTD names it.</summary>
/// <param name="Code">Catalog name, e.g. "german". Same spelling as the columns of the game's Localization.csv.</param>
/// <param name="NativeName">What speakers of it call it, for a language menu.</param>
/// <param name="Cultures">.NET culture names that should select this catalog.</param>
public sealed record LanguageInfo(string Code, string NativeName, string[] Cultures);

/// <summary>
/// Every text the tool shows anybody. Keys live in the code, texts live in JSON
/// catalogs, one per language.
///
/// Two decisions worth knowing:
///
/// The catalogs carry the game's own language names ("koreana", "schinese"), not
/// culture codes, so a modder who has ever edited a 7DTD Localization.csv knows
/// which file to open.
///
/// Catalogs are embedded in the assembly AND read from a "lang" folder next to
/// the executable, the folder winning. That is how a translation gets fixed
/// without a build: drop german.json in there, restart, done. A file may hold
/// only the keys it wants to change; the rest falls back to the embedded copy,
/// then to English, then to the key itself, which is ugly on purpose so a gap is
/// visible instead of blank.
/// </summary>
public static class Loc
{
    public const string English = "english";

    /// <summary>The thirteen languages 7 Days to Die itself ships.</summary>
    public static readonly IReadOnlyList<LanguageInfo> Known = new[]
    {
        new LanguageInfo("english", "English", new[] { "en" }),
        new LanguageInfo("german", "Deutsch", new[] { "de" }),
        new LanguageInfo("spanish", "Espanol", new[] { "es" }),
        new LanguageInfo("french", "Francais", new[] { "fr" }),
        new LanguageInfo("italian", "Italiano", new[] { "it" }),
        new LanguageInfo("japanese", "Nihongo", new[] { "ja" }),
        new LanguageInfo("koreana", "Hangugeo", new[] { "ko" }),
        new LanguageInfo("polish", "Polski", new[] { "pl" }),
        new LanguageInfo("brazilian", "Portugues do Brasil", new[] { "pt-BR", "pt" }),
        new LanguageInfo("russian", "Russkiy", new[] { "ru" }),
        new LanguageInfo("turkish", "Turkce", new[] { "tr" }),
        new LanguageInfo("schinese", "Jianti Zhongwen", new[] { "zh-Hans", "zh-CN", "zh-SG", "zh" }),
        new LanguageInfo("tchinese", "Fanti Zhongwen", new[] { "zh-Hant", "zh-TW", "zh-HK", "zh-MO" }),
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    private static Dictionary<string, string> _current = Catalog(English);
    private static Dictionary<string, string> _fallback = Catalog(English);

    /// <summary>Catalog in use right now.</summary>
    public static string Current { get; private set; } = English;

    /// <summary>Raised after <see cref="Use"/> changed the language, for the GUI to refresh.</summary>
    public static event Action? Changed;

    /// <summary>Folder for catalogs that override the embedded ones.</summary>
    public static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>
    /// Selects a language. Accepts a catalog name ("german"), a culture name
    /// ("de-DE"), null, empty or "auto" for the system language. Anything unknown
    /// ends up at English rather than throwing: a wrong entry in a config file
    /// must not stop a test run.
    /// </summary>
    public static string Use(string? requested)
    {
        var code = Resolve(requested);
        lock (Gate)
        {
            Current = code;
            _current = Catalog(code);
            _fallback = Catalog(English);
        }
        Changed?.Invoke();
        return code;
    }

    /// <summary>Turns a request into an existing catalog name.</summary>
    public static string Resolve(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return FromSystem();

        var wanted = requested.Trim();
        var available = Available();

        var direct = available.FirstOrDefault(a => a.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (direct is not null) return direct;

        var byCulture = FromCulture(wanted);
        return byCulture is not null && available.Contains(byCulture, StringComparer.OrdinalIgnoreCase)
            ? byCulture
            : English;
    }

    /// <summary>
    /// The system language, or English when it is not one of the thirteen or has
    /// no catalog yet.
    /// </summary>
    public static string FromSystem()
    {
        try
        {
            var code = FromCulture(CultureInfo.CurrentUICulture.Name);
            return code is not null && Available().Contains(code, StringComparer.OrdinalIgnoreCase)
                ? code
                : English;
        }
        catch (Exception)
        {
            return English;
        }
    }

    /// <summary>
    /// Culture name to catalog. Walks from the specific to the general, so
    /// "de-AT" finds German and "pt-PT" finds the Brazilian catalog rather than
    /// nothing.
    /// </summary>
    public static string? FromCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return null;

        var candidates = new List<string> { cultureName };
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            while (culture is not null && culture.Name.Length > 0)
            {
                if (!candidates.Contains(culture.Name, StringComparer.OrdinalIgnoreCase)) candidates.Add(culture.Name);
                // Simplified Chinese arrives as zh-Hans-CN and similar, so the
                // script part has to be considered as well as the language part.
                var parts = culture.Name.Split('-');
                if (parts.Length >= 2) candidates.Add($"{parts[0]}-{parts[1]}");
                culture = culture.Parent.Name.Length == 0 ? null : culture.Parent;
            }
        }
        catch (CultureNotFoundException)
        {
            var dash = cultureName.IndexOf('-');
            if (dash > 0) candidates.Add(cultureName[..dash]);
        }

        foreach (var candidate in candidates)
            foreach (var lang in Known)
                if (lang.Cultures.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    return lang.Code;

        return null;
    }

    /// <summary>Text for a key, formatted with <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        string text;
        lock (Gate)
        {
            text = _current.TryGetValue(key, out var hit) && hit.Length > 0 ? hit
                : _fallback.TryGetValue(key, out var en) && en.Length > 0 ? en
                : key;
        }

        if (args.Length == 0) return text;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            // A translation with a broken placeholder must not take a run down.
            return text;
        }
    }

    /// <summary>
    /// Catalog names that actually exist, in the order of <see cref="Known"/>.
    ///
    /// Deliberately not "the thirteen 7DTD languages": a language nobody has
    /// translated yet must not show up in a menu, because picking it would just
    /// show English and look broken. A translation exists when its file does.
    /// </summary>
    public static List<string> Available()
    {
        var embedded = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("Testbench.Core.I18n.lang.", StringComparison.Ordinal) &&
                        n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n["Testbench.Core.I18n.lang.".Length..^".json".Length])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var codes = Known.Select(k => k.Code).Where(embedded.Contains).ToList();
        foreach (var extra in embedded.Where(e => !codes.Contains(e, StringComparer.OrdinalIgnoreCase)))
            codes.Add(extra);

        try
        {
            if (Directory.Exists(LangDir))
                foreach (var file in Directory.GetFiles(LangDir, "*.json"))
                {
                    var code = Path.GetFileNameWithoutExtension(file);
                    if (!codes.Contains(code, StringComparer.OrdinalIgnoreCase)) codes.Add(code);
                }
        }
        catch (Exception)
        {
            // A missing or unreadable folder just means no extra languages.
        }
        return codes;
    }

    /// <summary>Native name for a catalog, falling back to the catalog name itself.</summary>
    public static string NativeName(string code) =>
        Known.FirstOrDefault(k => k.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.NativeName ?? code;

    /// <summary>Keys English has and <paramref name="code"/> does not, for "tb lang --check".</summary>
    public static List<string> MissingKeys(string code)
    {
        var english = Catalog(English);
        var other = Catalog(code);
        return english.Keys
            .Where(k => !other.TryGetValue(k, out var v) || v.Length == 0)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Loads a catalog: embedded first, then overridden by the lang folder.</summary>
    public static Dictionary<string, string> Catalog(string code)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(code, out var cached)) return cached;

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            Merge(texts, Embedded(code));
            Merge(texts, FromDisk(code));
            Cache[code] = texts;
            return texts;
        }
    }

    /// <summary>Forgets loaded catalogs, so an edited file takes effect without a restart.</summary>
    public static void Reload()
    {
        lock (Gate)
        {
            Cache.Clear();
            _current = Catalog(Current);
            _fallback = Catalog(English);
        }
        Changed?.Invoke();
    }

    private static void Merge(Dictionary<string, string> into, Dictionary<string, string>? from)
    {
        if (from is null) return;
        foreach (var (k, v) in from) into[k] = v;
    }

    private static Dictionary<string, string>? Embedded(string code)
    {
        var name = $"Testbench.Core.I18n.lang.{code}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string>? FromDisk(string code)
    {
        try
        {
            var path = Path.Combine(LangDir, code + ".json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Options);
        }
        catch (Exception)
        {
            // A broken hand-written catalog falls back instead of crashing.
            return null;
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
