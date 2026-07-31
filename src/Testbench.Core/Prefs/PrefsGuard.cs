using System.Diagnostics;
using Microsoft.Win32;
using Testbench.Core.Config;

namespace Testbench.Core.Prefs;

public sealed record PrefsCheck(string Name, int? Expected, int? Actual, bool Ok, string? Problem);

/// <summary>
/// Protects the GamePrefs around every run.
///
/// 7DTD stores its options as Unity PlayerPrefs under
/// HKCU\Software\The Fun Pimps\7 Days To Die: OUTSIDE the user data folder,
/// shared by every installation, and not redirectable by any launch parameter.
/// A freshly unpacked build writes its defaults there on first start and silently
/// overwrites the tuned live settings. Proof, if anyone doubts it: a start with a
/// brand new empty user data folder still logs "Last played version".
///
/// Anything that starts the game has to go through this class.
/// </summary>
public sealed class PrefsGuard
{
    private readonly PrefsConfig _cfg;
    private readonly Action<string> _log;

    public PrefsGuard(PrefsConfig cfg, Action<string>? log = null)
    {
        _cfg = cfg;
        _log = log ?? (_ => { });
    }

    /// <summary>Exports the key. Returns the backup path; throws if it did not appear.</summary>
    public string Backup(string label)
    {
        Directory.CreateDirectory(_cfg.BackupDir);
        var file = Path.Combine(_cfg.BackupDir, $"prefs_pre_{label}_{Stamp()}.reg");

        var ok = Reg("export", _cfg.Key, file, "/y");
        if (!ok || !File.Exists(file))
            throw new IOException("GamePrefs konnten nicht gesichert werden - Abbruch.");

        return file;
    }

    /// <summary>
    /// Imports the backup again and then checks the values that matter. The
    /// PowerShell scripts restored and trusted the result; these four settings are
    /// the ones that fixed the RAM thrashing, so a failed restore has to be loud.
    /// </summary>
    public (bool Restored, List<PrefsCheck> Checks) Restore(string backupFile)
    {
        var restored = Reg("import", backupFile);
        if (!restored) _log($"Restore fehlgeschlagen. Manuell: reg import \"{backupFile}\"");
        return (restored, Verify());
    }

    /// <summary>Compares the tuned values against what is in the registry now.</summary>
    public List<PrefsCheck> Verify()
    {
        var result = new List<PrefsCheck>();
        if (_cfg.GoldenValues.Count == 0) return result;

        using var key = OpenKey();
        if (key is null)
        {
            foreach (var (name, expected) in _cfg.GoldenValues)
                result.Add(new PrefsCheck(name, expected, null, false, $"Registry-Key '{_cfg.Key}' nicht lesbar."));
            return result;
        }

        var names = key.GetValueNames();
        foreach (var (name, expected) in _cfg.GoldenValues)
        {
            // Unity mangles PlayerPrefs names into "<name>_h<hash>", so the real
            // value name is only known by prefix.
            var match = names.FirstOrDefault(n =>
                n.StartsWith(name + "_h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                result.Add(new PrefsCheck(name, expected, null, false, "Wert nicht vorhanden."));
                continue;
            }

            var actual = AsInt(key.GetValue(match));
            if (actual is null)
            {
                result.Add(new PrefsCheck(name, expected, null, false, $"Wert '{match}' nicht als Zahl lesbar."));
                continue;
            }

            result.Add(new PrefsCheck(name, expected, actual, actual == expected, null));
        }
        return result;
    }

    private RegistryKey? OpenKey()
    {
        var (hive, sub) = SplitKey(_cfg.Key);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            return baseKey.OpenSubKey(sub, writable: false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static (RegistryHive Hive, string SubKey) SplitKey(string full)
    {
        var s = full.Replace('/', '\\').Trim();
        var i = s.IndexOf('\\');
        var head = i < 0 ? s : s[..i];
        var rest = i < 0 ? "" : s[(i + 1)..];

        var hive = head.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            _ => RegistryHive.CurrentUser,
        };
        return (hive, rest);
    }

    /// <summary>
    /// Unity writes int prefs as REG_DWORD, but older values can sit there as
    /// binary or string, so all three are accepted before giving up.
    /// </summary>
    private static int? AsInt(object? value) => value switch
    {
        null => null,
        int i => i,
        long l => (int)l,
        string s => int.TryParse(s, out var p) ? p : null,
        byte[] b when b.Length >= 4 => BitConverter.ToInt32(b, 0),
        _ => null,
    };

    /// <summary>
    /// reg.exe wrapped instead of called directly, because of the trap the
    /// PowerShell scripts hit: under PowerShell 5.1 every stderr line of a native
    /// exe becomes a NativeCommandError and tore the script down with
    /// ErrorActionPreference=Stop even at exit code 0. In C# the same rule
    /// applies in spirit: judge by the exit code, not by whether anything was
    /// written to stderr.
    /// </summary>
    private bool Reg(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 && err.Trim().Length > 0) _log($"reg.exe: {err.Trim()}");
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _log($"reg.exe liess sich nicht starten: {ex.Message}");
            return false;
        }
    }

    public static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
}
