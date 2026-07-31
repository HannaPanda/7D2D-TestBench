using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Testbench.Core.Run;

public sealed record LaunchOutcome(string StopReason, int? ExitCode);

/// <summary>
/// Starts one 7DTD installation and waits for it in the way the stage requires.
/// Knows nothing about mods or verdicts.
/// </summary>
public sealed class GameLauncher
{
    public const string ProcessNamePrefix = "7DaysToDie";

    private readonly Action<string> _log;

    public GameLauncher(Action<string>? log = null) => _log = log ?? (_ => { });

    /// <summary>
    /// Headless: dedicated server, -batchmode -nographics. The arguments are TFP's
    /// own from startdedicated.bat plus the isolation. Note there is no
    /// 7DaysToDieServer.exe in a client installation; the client exe with
    /// -dedicated is the documented fallback.
    ///
    /// Covers mod loading, Harmony, XML parsing and localization. Covers nothing
    /// graphical: TextureAtlasBlocks.LoadTextureAtlas does not run under
    /// -nographics, and no menu or key is ever touched.
    /// </summary>
    public LaunchOutcome RunHeadless(
        string gameDir,
        string exePath,
        string logPath,
        string userDataFolder,
        string readyPattern,
        string fatalPattern,
        int timeoutSeconds,
        CancellationToken cancel = default)
    {
        var args = new List<string>
        {
            "-logfile", logPath,
            "-quit", "-batchmode", "-nographics",
            "-configfile=serverconfig.xml",
            $"-UserDataFolder={userDataFolder}",
            "-noeac",
            "-dedicated",
        };

        using var proc = Start(exePath, gameDir, args);
        return WaitForMarker(proc, logPath, readyPattern, fatalPattern, timeoutSeconds, cancel);
    }

    /// <summary>
    /// GUI: the real client with a window, no EAC. Waits until the human closes
    /// it. This is the only stage that can test the texture atlas, menus, keys and
    /// how anything feels.
    /// </summary>
    public LaunchOutcome RunGui(
        string gameDir,
        string exePath,
        string logPath,
        string userDataFolder,
        CancellationToken cancel = default)
    {
        var args = new List<string>
        {
            $"-UserDataFolder={userDataFolder}",
            "-logfile", logPath,
            "-noeac",
        };

        using var proc = Start(exePath, gameDir, args);
        while (!proc.HasExited)
        {
            if (cancel.IsCancellationRequested)
            {
                KillTree(proc);
                return new LaunchOutcome(StopReason.Closed, null);
            }
            proc.WaitForExit(500);
        }

        WaitUntilGone();
        return new LaunchOutcome(StopReason.Closed, proc.ExitCode);
    }

    /// <summary>Is a 7DTD already running? Two instances share Steam, ports and the prefs key.</summary>
    public static Process[] RunningInstances() =>
        Process.GetProcesses().Where(p =>
        {
            try { return p.ProcessName.StartsWith(ProcessNamePrefix, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToArray();

    /// <summary>
    /// Starts the game with its console output swallowed.
    ///
    /// Unity prints its allocator configuration and a few startup lines to stdout
    /// even when -logfile is given. Inheriting our console would mix that into
    /// tb's own output, and in --json mode it would sit in front of the envelope
    /// and make the result unparsable. Everything worth reading is in the log file
    /// anyway.
    /// </summary>
    private Process Start(string exePath, string gameDir, List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        _log($"Start: {Path.GetFileName(exePath)} {string.Join(' ', args)}");
        var proc = Process.Start(psi) ?? throw new IOException($"'{exePath}' liess sich nicht starten.");

        // Redirected streams must be drained or the game blocks once the pipe
        // buffer fills, which for a 35 s startup is a real possibility.
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        return proc;
    }

    /// <summary>
    /// Polls the log for the ready marker or a fatal pattern.
    ///
    /// The ready marker is deliberately late in startup. Waiting for
    /// "Started Telnet" (which appears after ~2.7 s, long before the XMLs are
    /// parsed) would report every run green having tested nothing.
    /// </summary>
    private LaunchOutcome WaitForMarker(
        Process proc,
        string logPath,
        string readyPattern,
        string fatalPattern,
        int timeoutSeconds,
        CancellationToken cancel)
    {
        var ready = new Regex(readyPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var fatal = new Regex(fatalPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var stop = "";

        while (DateTime.UtcNow < deadline)
        {
            if (cancel.IsCancellationRequested) { stop = StopReason.Closed; break; }

            Thread.Sleep(1000);

            var text = ReadLogSafe(logPath);
            if (text is not null)
            {
                if (ready.IsMatch(text)) { stop = StopReason.Ready; break; }
                if (fatal.IsMatch(text)) { stop = StopReason.Fatal; break; }
            }

            if (proc.HasExited) { stop = StopReason.Exited; break; }
        }
        if (stop.Length == 0) stop = StopReason.Timeout;

        var exit = proc.HasExited ? proc.ExitCode : (int?)null;
        if (!proc.HasExited) KillTree(proc);
        WaitUntilGone();

        return new LaunchOutcome(stop, exit);
    }

    /// <summary>The game holds its log file open, so it has to be read with sharing allowed.</summary>
    public static string? ReadLogSafe(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static string[] ReadLogLines(string path) => SplitLines(ReadLogSafe(path));

    /// <summary>
    /// Splits log text into lines the same way Get-Content did, which is what the
    /// counters of the PowerShell bench were measured against: a trailing newline
    /// does NOT produce an extra empty line. Without that the line count and the
    /// "ignored" figure would be off by one against every old report.
    /// </summary>
    public static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.ToArray();
    }

    private void KillTree(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); }
        catch (Exception ex) { _log($"Prozess liess sich nicht beenden: {ex.Message}"); }
    }

    /// <summary>
    /// Wait until no 7DaysToDie process is left. Without this the next step can
    /// hit locked mod DLLs, and the prefs restore can race the game's own write on
    /// shutdown.
    /// </summary>
    public static void WaitUntilGone(int maxSeconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var running = RunningInstances();
            foreach (var p in running) p.Dispose();
            if (running.Length == 0) break;
            Thread.Sleep(1000);
        }
        // The scripts slept 2 s here for the same reason: the process is gone
        // before its file handles always are.
        Thread.Sleep(2000);
    }
}
