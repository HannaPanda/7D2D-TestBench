using System.Xml.Linq;

namespace Testbench.Core.Deploy;

public sealed record ModInfo(string Name, string DisplayName, string Version);

/// <summary>
/// Reads ModInfo.xml. The folder name and the name 7DTD reports are two different
/// things: the folder is "00000-Gears" and the mod logs itself as "Gears". Only
/// &lt;Name&gt; says what will appear in "[MODS] Loaded Mod: x", so anything that
/// checks the log has to read it from here, and from the INSTALLED copy.
/// </summary>
public static class ModInfoReader
{
    public static ModInfo Read(string modFolder)
    {
        var fallback = Path.GetFileName(Path.TrimEndingDirectorySeparator(modFolder));
        var file = Path.Combine(modFolder, "ModInfo.xml");
        if (!File.Exists(file)) return new ModInfo(fallback, fallback, "");

        try
        {
            var root = XDocument.Load(file).Root;
            if (root is null) return new ModInfo(fallback, fallback, "");

            var name = Value(root, "Name") ?? fallback;
            return new ModInfo(
                name,
                Value(root, "DisplayName") ?? name,
                Value(root, "Version") ?? "");
        }
        catch (Exception)
        {
            // A malformed ModInfo.xml is a finding, not a crash: the run should
            // report "mod not loaded" from the log, which is the honest answer.
            return new ModInfo(fallback, fallback, "");
        }
    }

    /// <summary>
    /// Both the v1 (&lt;ModInfo&gt;) and v2 (&lt;xml&gt;) roots are accepted, and the
    /// value sits in a "value" attribute in either case.
    /// </summary>
    private static string? Value(XElement root, string element)
    {
        var e = root.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, element, StringComparison.OrdinalIgnoreCase));
        var v = e?.Attribute("value")?.Value;
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        v = e?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
