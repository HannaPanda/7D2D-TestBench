using System.Diagnostics;
using Microsoft.Win32;
using Testbench.Core.Config;

namespace Testbench.Core.Prefs;

public sealed record PrefsCheck(string Name, int? Expected, int? Actual, bool Ok, string? Problem);

/// <summary>
/// Result of comparing the key after the restore against the backup taken before
/// the run. This is the check that needs no configuration: whatever the person
/// had set, it has to be there again afterwards.
/// </summary>
public sealed record PrefsRoundTrip(bool Ok, int Expected, int Actual, List<string> Differing, string? Problem)
{
    public static PrefsRoundTrip Unknown(string problem) => new(false, 0, 0, new List<string>(), problem);
}

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
            throw new IOException(I18n.Loc.T("prefs.backupFailed"));

        return file;
    }

    /// <summary>
    /// Imports the backup again and proves it worked, in two ways: the whole key
    /// is compared against the backup file, and any explicitly configured value is
    /// read back by name. The PowerShell scripts imported and trusted the result;
    /// losing tuned settings is the most expensive thing that can happen here and
    /// the hardest to notice afterwards.
    /// </summary>
    public (bool Restored, List<PrefsCheck> Checks, PrefsRoundTrip RoundTrip) Restore(string backupFile)
    {
        var restored = Reg("import", backupFile);
        if (!restored) _log(I18n.Loc.T("prefs.restore.failed", backupFile));
        return (restored, Verify(), RoundTrip(backupFile));
    }

    /// <summary>
    /// Exports the key again and compares it value by value against the backup.
    /// Works without any configuration and for anyone's settings, which is why it
    /// replaced the hard-coded list of four values the bench started with.
    /// </summary>
    public PrefsRoundTrip RoundTrip(string backupFile)
    {
        if (!File.Exists(backupFile)) return PrefsRoundTrip.Unknown(I18n.Loc.T("prefs.roundtrip.noBackup", backupFile));

        var temp = Path.Combine(Path.GetTempPath(), $"tb_prefs_after_{Guid.NewGuid():N}.reg");
        try
        {
            if (!Reg("export", _cfg.Key, temp, "/y") || !File.Exists(temp))
                return PrefsRoundTrip.Unknown(I18n.Loc.T("prefs.roundtrip.noExport"));

            var before = ValueLines(backupFile);
            var after = ValueLines(temp);

            // Names present before and now different or gone. A value that only
            // exists afterwards is the game having written something new, which is
            // not a loss and not worth alarming anyone about.
            var differing = before
                .Where(kv => !after.TryGetValue(kv.Key, out var now) || now != kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PrefsRoundTrip(differing.Count == 0, before.Count, after.Count, differing, null);
        }
        catch (Exception ex)
        {
            return PrefsRoundTrip.Unknown(ex.Message);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Value lines of a .reg export as name to raw text. reg.exe writes UTF-16,
    /// and a value can be split over continuation lines (hex values are), so those
    /// are folded back together before anything is compared.
    /// </summary>
    private static Dictionary<string, string> ValueLines(string regFile)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        var value = new System.Text.StringBuilder();

        foreach (var raw in File.ReadAllLines(regFile))
        {
            var line = raw.TrimEnd();
            if (line.EndsWith('\\')) { line = line[..^1]; }

            if (line.StartsWith('"'))
            {
                Flush();
                var close = line.IndexOf("\"=", StringComparison.Ordinal);
                if (close < 1) continue;
                name = line[1..close];
                value.Append(line[(close + 2)..].Trim());
            }
            else if (name is not null && line.Length > 0 && !line.StartsWith('['))
            {
                value.Append(line.Trim());
            }
            else if (line.StartsWith('['))
            {
                Flush();
            }
        }
        Flush();
        return result;

        void Flush()
        {
            if (name is not null) result[name] = value.ToString();
            name = null;
            value.Clear();
        }
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
                result.Add(new PrefsCheck(name, expected, null, false, I18n.Loc.T("prefs.keyUnreadable", _cfg.Key)));
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
                result.Add(new PrefsCheck(name, expected, null, false, I18n.Loc.T("prefs.valueMissing")));
                continue;
            }

            var actual = AsInt(key.GetValue(match));
            if (actual is null)
            {
                result.Add(new PrefsCheck(name, expected, null, false, I18n.Loc.T("prefs.valueNotANumber", match)));
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
            _log(I18n.Loc.T("prefs.regNotStarted", ex.Message));
            return false;
        }
    }

    public static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
}
