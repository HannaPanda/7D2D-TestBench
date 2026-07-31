using System.Text.Json;
using Testbench.Core.Config;

namespace Testbench.Cli;

/// <summary>
/// Exit codes. An agent decides what to do next by these, so they are part of the
/// contract and must not shift meaning.
/// </summary>
public static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>The tool worked, the test did not pass.</summary>
    public const int TestFailed = 1;

    /// <summary>Configuration or environment problem: nothing was tested.</summary>
    public const int SetupError = 2;

    /// <summary>Refused on purpose: another run holds the lock, or a game is running.</summary>
    public const int Blocked = 3;
}

/// <summary>
/// Everything the CLI prints. Human mode is coloured plain text, --json is one
/// envelope on stdout and nothing else, so a caller can parse it without
/// stripping progress lines.
/// </summary>
public sealed class Output
{
    private readonly bool _json;
    private readonly List<string> _progress = new();

    public Output(bool json) => _json = json;

    public bool IsJson => _json;

    /// <summary>Progress and warnings. In JSON mode collected into the envelope.</summary>
    public void Info(string message)
    {
        if (_json) { _progress.Add(message); return; }
        Console.WriteLine(message);
    }

    public void Detail(string message)
    {
        if (_json) { _progress.Add(message); return; }
        WriteColored(message, ConsoleColor.DarkGray);
    }

    public void Good(string message)
    {
        if (_json) { _progress.Add(message); return; }
        WriteColored(message, ConsoleColor.Green);
    }

    public void Warn(string message)
    {
        if (_json) { _progress.Add("WARN: " + message); return; }
        WriteColored(message, ConsoleColor.Yellow);
    }

    public void Bad(string message)
    {
        if (_json) { _progress.Add("FEHLER: " + message); return; }
        WriteColored(message, ConsoleColor.Red);
    }

    public void Table(IEnumerable<string[]> rows, params string[] headers)
    {
        if (_json) return;

        var all = new List<string[]> { headers };
        all.AddRange(rows);
        if (all.Count == 1) return;

        var widths = new int[headers.Length];
        foreach (var row in all)
        {
            for (var i = 0; i < headers.Length && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], (row[i] ?? "").Length);
        }

        for (var r = 0; r < all.Count; r++)
        {
            var row = all[r];
            var line = string.Join("  ", headers.Select((_, i) =>
                (i < row.Length ? row[i] ?? "" : "").PadRight(widths[i]))).TrimEnd();

            if (r == 0)
            {
                WriteColored(line, ConsoleColor.Cyan);
                WriteColored(string.Join("  ", widths.Select(w => new string('-', w))), ConsoleColor.DarkGray);
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// Final result. In JSON mode this is the only thing on stdout, so a caller can
    /// pipe it straight into a parser.
    /// </summary>
    public int Finish(string command, int exitCode, object? data = null)
    {
        if (!_json) return exitCode;

        var envelope = new
        {
            ok = exitCode == ExitCodes.Ok,
            command,
            exitCode,
            messages = _progress,
            data,
        };
        Console.WriteLine(JsonSerializer.Serialize(envelope, ConfigStore.Json));
        return exitCode;
    }

    private static void WriteColored(string message, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = prev;
        }
    }
}
