using Testbench.Core.Config;
using Testbench.Core.Deploy;
using Testbench.Core.I18n;

namespace Testbench.Core.Tests;

/// <summary>
/// The trash folder is created inside somebody else's game installation, which is
/// why its name stopped being German. These tests hold the promise that rename has
/// to keep: a mod folder is never lost over it. Everything runs through the real
/// Deploy, because a migration that only works when called directly is worthless.
/// </summary>
public sealed class ModDeployerTrashTests : IDisposable
{
    private readonly string _tmp;

    public ModDeployerTrashTests()
    {
        Loc.Use(Loc.English);
        _tmp = Path.Combine(Path.GetTempPath(), "tb-trash-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Carries_an_old_trash_folder_over_to_the_new_name()
    {
        var game = Bench();
        Mod(Path.Combine(game, ModDeployer.LegacyTrashFolder, "SomeOldMod"));

        Deploy(game);

        Assert.True(File.Exists(Path.Combine(game, ModDeployer.TrashFolder, "SomeOldMod", "marker.txt")));
        Assert.False(Directory.Exists(Path.Combine(game, ModDeployer.LegacyTrashFolder)));
    }

    [Fact]
    public void Leaves_a_collision_where_it_is_instead_of_overwriting_it()
    {
        var game = Bench();
        Mod(Path.Combine(game, ModDeployer.LegacyTrashFolder, "Dup"), "old");
        Mod(Path.Combine(game, ModDeployer.TrashFolder, "Dup"), "new");

        Deploy(game);

        // The newer copy is the current state and stays; the older one is not
        // deleted to make the folder disappear, so the leftover is visible.
        Assert.Equal("new", File.ReadAllText(Path.Combine(game, ModDeployer.TrashFolder, "Dup", "marker.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(game, ModDeployer.LegacyTrashFolder, "Dup", "marker.txt")));
    }

    [Fact]
    public void A_foreign_mod_goes_into_the_folder_with_the_new_name()
    {
        var game = Bench();
        Mod(Path.Combine(game, "Mods", "SomebodyElsesMod"));

        var result = Deploy(game);

        Assert.Contains("SomebodyElsesMod", result.Disabled);
        Assert.True(Directory.Exists(Path.Combine(game, ModDeployer.TrashFolder, "SomebodyElsesMod")));
        Assert.False(Directory.Exists(Path.Combine(game, "Mods", "SomebodyElsesMod")));
    }

    [Fact]
    public void Without_an_old_folder_nothing_happens_and_nothing_is_created()
    {
        var game = Bench();

        Deploy(game);

        Assert.False(Directory.Exists(Path.Combine(game, ModDeployer.LegacyTrashFolder)));
    }

    /// <summary>A game installation with an empty Mods folder and a mod to deploy.</summary>
    private string Bench()
    {
        var game = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(Path.Combine(game, "Mods"));
        Mod(Path.Combine(_tmp, "repo", "MyMod"));
        return game;
    }

    private static void Mod(string dir, string marker = "x")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "marker.txt"), marker);
    }

    private DeployResult Deploy(string game)
    {
        var variant = new ModVariant { Name = "Default", Folder = "MyMod" };
        var mod = new ModConfig { ModId = "mymod", Repo = Path.Combine(_tmp, "repo") };
        mod.Variants.Add(variant);

        return new ModDeployer(new MachineConfig()).Deploy(game, mod, variant);
    }
}
