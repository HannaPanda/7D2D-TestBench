using Testbench.Core.Config;
using Testbench.Core.Store;

namespace Testbench.Cli.Commands;

/// <summary>
/// What every command needs: the parsed arguments, the output channel and lazy
/// access to the configuration. Lazy because "tb init" must work before a config
/// exists, and "tb help" must not touch the disk at all.
/// </summary>
public sealed class CommandContext
{
    private MachineConfig? _machine;

    public CommandContext(Args args, Output output)
    {
        Args = args;
        Out = output;
        MachinePath = ConfigStore.ResolveMachinePath(args.Get("config"));
    }

    public Args Args { get; }
    public Output Out { get; }
    public string MachinePath { get; }

    public MachineConfig Machine => _machine ??= ConfigStore.LoadMachine(MachinePath);

    public void SaveMachine() => ConfigStore.SaveMachine(Machine, MachinePath);

    public RunStore Store => new(Machine);

    public (ModConfig Config, string Path) RequireMod() =>
        ConfigStore.RequireMod(Machine, Args.Get("mod") ?? throw new UsageException(Core.I18n.Loc.T("cli.optionMissing", "mod")));
}
