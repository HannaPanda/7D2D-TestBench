using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Testbench.Core.Diagnostics;

/// <summary>
/// Finds the player's own, live 7DTD installation.
///
/// Not so it can be tested - the opposite. A test run sweeps every mod it did
/// not put there into _Mods-deaktiviert and hands the GamePrefs around. Pointed
/// at the Steam copy somebody actually plays, it would take their modlist apart
/// on the first run. So the live installation is located in order to be refused:
/// <see cref="Doctor"/> turns a version that lives there into a failure.
/// </summary>
public static class SteamLocator
{
    public const string GameFolderName = "7 Days To Die";

    /// <summary>Steam's own installation folder, or null when Steam is not installed.</summary>
    public static string? SteamPath()
    {
        foreach (var (hive, key) in new[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var sub = baseKey.OpenSubKey(key);
                var path = sub?.GetValue("SteamPath") as string ?? sub?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var full = path.Replace('/', '\\');
                    if (Directory.Exists(full)) return Path.GetFullPath(full);
                }
            }
            catch (Exception)
            {
                // No Steam, no permission, no problem: this is a convenience.
            }
        }
        return null;
    }

    /// <summary>
    /// Every Steam library folder, from libraryfolders.vdf. Parsed with a regex
    /// rather than a VDF library: one quoted "path" per library entry is all this
    /// needs, and a malformed file must degrade to "found nothing".
    /// </summary>
    public static List<string> LibraryFolders()
    {
        var found = new List<string>();
        var steam = SteamPath();
        if (steam is null) return found;

        found.Add(steam);

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return found;

        try
        {
            foreach (var m in Regex.Matches(File.ReadAllText(vdf), @"""path""\s*""(?<p>[^""]+)""").Cast<Match>())
            {
                var path = m.Groups["p"].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path)) found.Add(Path.GetFullPath(path));
            }
        }
        catch (Exception)
        {
            // Fall through with whatever was collected.
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;
    }

    private static string? _live;
    private static bool _lookedForLive;

    /// <summary>
    /// The live installation folder, or null when there is none. Cached: this gets
    /// asked once per scanned folder and the answer cannot change while the tool
    /// runs. <see cref="Forget"/> clears it.
    /// </summary>
    public static string? LiveInstall()
    {
        if (_lookedForLive) return _live;

        foreach (var lib in LibraryFolders())
        {
            var dir = Path.Combine(lib, "steamapps", "common", GameFolderName);
            if (File.Exists(Path.Combine(dir, "7DaysToDie.exe")))
            {
                _live = Path.GetFullPath(dir);
                break;
            }
        }

        _lookedForLive = true;
        return _live;
    }

    /// <summary>Drops the cached answer, for tests and for after a Steam install.</summary>
    public static void Forget()
    {
        _live = null;
        _lookedForLive = false;
    }

    /// <summary>
    /// True when <paramref name="dir"/> is the live installation or sits inside
    /// it. Used to refuse it as a test version.
    /// </summary>
    public static bool IsLiveInstall(string dir)
    {
        var live = LiveInstall();
        return live is not null && IsInside(dir, live);
    }

    /// <summary>Path containment without being fooled by trailing separators or case.</summary>
    public static bool IsInside(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(parent)) return false;
        try
        {
            var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
            var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            return c.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                   c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
