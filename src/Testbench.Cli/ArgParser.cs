namespace Testbench.Cli;

/// <summary>
/// Minimal argument parser: verbs, --flags, --key value, repeatable keys.
///
/// Hand-written on purpose. The only dependency this tool has is the .NET
/// runtime, and an argument library that changes its API between previews is a
/// bad trade for a tool whose job is to still work in a year.
/// </summary>
public sealed class Args
{
    private readonly List<string> _positional = new();
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var t = argv[i];
            if (!t.StartsWith("--", StringComparison.Ordinal))
            {
                a._positional.Add(t);
                continue;
            }

            var name = t[2..];
            string? value = null;

            // --key=value and --key value are both accepted; typing either should
            // not be a thing anyone has to remember.
            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                value = name[(eq + 1)..];
                name = name[..eq];
            }
            else if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = argv[++i];
            }

            a._seen.Add(name);
            if (!a._options.TryGetValue(name, out var list)) a._options[name] = list = new List<string>();
            if (value is not null) list.Add(value);
        }
        return a;
    }

    public string? Verb(int index) => index < _positional.Count ? _positional[index] : null;

    public IReadOnlyList<string> Positional => _positional;

    public bool Has(string name) => _seen.Contains(name);

    public bool Flag(string name) =>
        _seen.Contains(name) &&
        (!_options.TryGetValue(name, out var v) || v.Count == 0 ||
         v[0] is "true" or "1" or "yes" or "ja");

    public string? Get(string name) =>
        _options.TryGetValue(name, out var v) && v.Count > 0 ? v[^1] : null;

    public string Require(string name) =>
        Get(name) ?? throw new UsageException($"--{name} fehlt.");

    /// <summary>
    /// Values of a repeatable option, also splitting on commas so
    /// "--version 3.0.0,3.0.1" and "--version 3.0.0 --version 3.0.1" agree.
    /// </summary>
    public List<string> GetAll(string name)
    {
        if (!_options.TryGetValue(name, out var v)) return new List<string>();
        return v.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();
    }

    public int? GetInt(string name) =>
        int.TryParse(Get(name), out var i) ? i : null;

    /// <summary>Names the user passed that no command knows. A typo must not be silently ignored.</summary>
    public List<string> Unknown(params string[] known)
    {
        var set = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        return _seen.Where(s => !set.Contains(s)).ToList();
    }
}

public sealed class UsageException : Exception
{
    public UsageException(string message) : base(message) { }
}
