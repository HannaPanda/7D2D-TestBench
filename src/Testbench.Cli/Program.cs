using Testbench.Cli.Commands;
using Testbench.Core.Config;
using Testbench.Core.I18n;

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

        // Language before anything is printed. --lang wins over the config, which
        // wins over the system language; an unknown value lands on English rather
        // than stopping a run.
        SelectLanguage(args);

        if (verb is null || verb is "help" or "-h" or "--help")
        {
            if (!json) Console.WriteLine(Help());
            return output.Finish("help", ExitCodes.Ok, new { usage = Help(), version = Version() });
        }

        if (verb is "version" or "-v" or "--version")
        {
            // Worth its own verb: the first question about any bug report is which
            // build produced it.
            output.Info(Loc.T("cli.version.line", Version(), Loc.Current));
            return output.Finish("version", ExitCodes.Ok, new
            {
                version = Version(),
                language = Loc.Current,
                langDir = Loc.LangDir,
                exeDir = AppContext.BaseDirectory,
            });
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
                "lang" or "language" => ConfigCommands.Lang(ctx),
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
            if (!json) Console.WriteLine(Help());
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

    /// <summary>
    /// Reads the language from --lang, else from the config file if there is one,
    /// else from the system. Deliberately tolerant: a broken config must still let
    /// "tb doctor" explain itself, so a failure here means English, not an abort.
    /// </summary>
    private static void SelectLanguage(Args args)
    {
        var explicitLang = args.Get("lang") ?? args.Get("language");
        if (!string.IsNullOrWhiteSpace(explicitLang)) { Loc.Use(explicitLang); return; }

        try
        {
            var path = ConfigStore.ResolveMachinePath(args.Get("config"));
            if (File.Exists(path)) { Loc.Use(ConfigStore.LoadMachine(path).Language); return; }
        }
        catch (Exception)
        {
            // Fall through to the system language.
        }
        Loc.Use("auto");
    }

    /// <summary>Informational version, so a bug report can name a build.</summary>
    public static string Version() =>
        typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static int Unknown(Output output, string verb)
    {
        output.Bad(Loc.T("cli.unknownCommand", verb));
        if (!output.IsJson) Console.WriteLine(Help());
        return output.Finish(verb, ExitCodes.SetupError, new { error = $"unknown command: {verb}" });
    }

    /// <summary>
    /// Usage text. Assembled from the catalog so it translates, but the verbs and
    /// options themselves never do: they are the contract, and a translated flag
    /// would be a different program.
    /// </summary>
    public static string Help()
    {
        var lines = new[]
        {
            Loc.T("cli.help.title"),
            "",
            Loc.T("cli.help.section.setup"),
            "  tb version                                       " + Loc.T("cli.help.version"),
            "  tb init [--game-root <path>] [--bench-root <path>] [--lang <language>]",
            "  tb import --psd1 <file> [--mod-out <file>]        " + Loc.T("cli.help.import.psd1"),
            "  tb import --gui-verified <file> --mod <modId>     " + Loc.T("cli.help.import.guiVerified"),
            "  tb doctor                                        " + Loc.T("cli.help.doctor"),
            "  tb lang [<language>] [--check]                   " + Loc.T("cli.help.lang"),
            "",
            Loc.T("cli.help.section.look"),
            "  tb versions                                      " + Loc.T("cli.help.versions"),
            "  tb versions scan [--root <p>] [--depth <n>] [--add]  " + Loc.T("cli.help.versionsScan"),
            "  tb versions add [<version>] [--path <p>] [--branch <b>] [--notes <t>]",
            "  tb versions remove <version>",
            "  tb mods                                          " + Loc.T("cli.help.mods"),
            "  tb mods add <path-to-testbench.mod.json>",
            "  tb mods remove <modId|path>",
            "  tb profiles --mod <modId>                        " + Loc.T("cli.help.profiles"),
            "",
            Loc.T("cli.help.section.test"),
            "  tb run --mod <modId> --profile <name>",
            "  tb run --mod <modId> --version 3.0.1 [--version 3.1.0] [--variant <name>]",
            "         [--stage headless|gui] [--visual ask|defer|ok] [--skip-deploy] [--note <t>]",
            "  tb status [--mod <modId>] [--limit <n>] [--pending]",
            "  tb verify --run <runId> --visual ok|fail [--note <t>]",
            "  tb report --mod <modId> [--variant <name>] [--write]",
            "  tb log --run <runId> [--lines <n>] [--highlights]",
            "",
            Loc.T("cli.help.section.global"),
            "  --json            " + Loc.T("cli.help.json"),
            "  --config <file>   " + Loc.T("cli.help.config"),
            "  --lang <language>  " + Loc.T("cli.help.langFlag"),
            "",
            Loc.T("cli.help.section.exitCodes"),
            "  0 " + Loc.T("cli.exit.ok") +
            "   1 " + Loc.T("cli.exit.testFailed") +
            "   2 " + Loc.T("cli.exit.setupError") +
            "   3 " + Loc.T("cli.exit.blocked"),
        };
        return string.Join(Environment.NewLine, lines);
    }
}
