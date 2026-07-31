using Testbench.Cli.Commands;
using Testbench.Core.Config;

namespace Testbench.Cli;

/// <summary>
/// tb - the one entry point to the 7DTD multiversion testbench.
///
/// Same core as the GUI. Everything here exists so that neither a human nor an
/// agent has to assemble a launch command, remember a config path, or read a
/// script to find out what a run actually did.
/// </summary>
public static class Program
{
    public static int Main(string[] argv)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var args = Args.Parse(argv);
        var json = args.Flag("json");
        var output = new Output(json);
        var verb = args.Verb(0)?.ToLowerInvariant();

        if (verb is null || verb is "help" or "-h" or "--help")
        {
            if (!json) Console.WriteLine(HelpText);
            return output.Finish("help", ExitCodes.Ok, new { usage = HelpText });
        }

        try
        {
            var ctx = new CommandContext(args, output);
            return verb switch
            {
                "init" => ConfigCommands.Init(ctx),
                "import" => ConfigCommands.Import(ctx),
                "doctor" => ConfigCommands.Doctor(ctx),
                "versions" or "version" => ConfigCommands.Versions(ctx),
                "mods" or "mod" => ConfigCommands.Mods(ctx),
                "profiles" => ConfigCommands.Profiles(ctx),
                "run" => RunCommands.Run(ctx),
                "status" => RunCommands.Status(ctx),
                "verify" => RunCommands.Verify(ctx),
                "report" => RunCommands.Report(ctx),
                "log" => RunCommands.Log(ctx),
                _ => Unknown(output, verb),
            };
        }
        catch (UsageException ex)
        {
            output.Bad(ex.Message);
            if (!json) Console.WriteLine();
            if (!json) Console.WriteLine(HelpText);
            return output.Finish(verb, ExitCodes.SetupError, new { error = ex.Message });
        }
        catch (ConfigException ex)
        {
            output.Bad(ex.Message);
            return output.Finish(verb, ExitCodes.SetupError, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // An unexpected failure is a setup error, never a passed test.
            output.Bad($"{ex.GetType().Name}: {ex.Message}");
            return output.Finish(verb, ExitCodes.SetupError, new { error = ex.Message, type = ex.GetType().Name });
        }
    }

    private static int Unknown(Output output, string verb)
    {
        output.Bad($"Unbekannter Befehl '{verb}'.");
        if (!output.IsJson) Console.WriteLine(HelpText);
        return output.Finish(verb, ExitCodes.SetupError, new { error = $"unknown command: {verb}" });
    }

    public const string HelpText = """
        tb - 7DTD Multiversion-Testbench

        Einrichten
          tb init [--game-root <pfad>] [--bench-root <pfad>]
          tb import --psd1 <datei> [--mod-out <datei>]      .psd1 des alten Benchs uebernehmen
          tb import --gui-verified <datei> --mod <modId>    alte Sichtpruefungen uebernehmen
          tb doctor                                        warum es nicht laeuft, vor dem Lauf

        Nachschauen
          tb versions                                      bekannte Spielversionen
          tb versions scan [--root <p>] [--depth <n>] [--add]   Installationen suchen
          tb versions add [<version>] [--path <p>] [--branch <b>] [--notes <t>]
          tb versions remove <version>
          tb mods                                          registrierte Mods
          tb mods add <pfad-zur-testbench.mod.json>
          tb mods remove <modId|pfad>
          tb profiles --mod <modId>                        fertige Testkombinationen

        Testen
          tb run --mod <modId> --profile <name>
          tb run --mod <modId> --version 3.0.1 [--version 3.1.0] [--variant <name>]
                 [--stage headless|gui] [--visual ask|defer|ok] [--skip-deploy] [--note <t>]
          tb status [--mod <modId>] [--limit <n>] [--pending]
          tb verify --run <runId> --visual ok|fail [--note <t>]
          tb report --mod <modId> [--variant <name>] [--write]
          tb log --run <runId> [--lines <n>] [--highlights]

        Global
          --json            genau ein JSON-Objekt auf stdout, sonst nichts
          --config <datei>  andere testbench.json verwenden

        Exit-Codes
          0 in Ordnung   1 Test durchgefallen   2 Konfiguration/Umgebung   3 blockiert
        """;
}
