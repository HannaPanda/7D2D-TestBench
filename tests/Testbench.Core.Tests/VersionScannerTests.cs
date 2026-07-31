using Testbench.Core.Config;
using Testbench.Core.I18n;

namespace Testbench.Core.Tests;

/// <summary>
/// The scanner decides which version a folder is. If it decides wrong, every
/// report afterwards names a version that was never tested, and no log would
/// ever say so. Hence the mismatch cases are tested, not only the happy ones.
/// </summary>
public sealed class VersionScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "tb-scan-" + Guid.NewGuid().ToString("N")[..8]);

    public VersionScannerTests()
    {
        Directory.CreateDirectory(_root);
        // Explain() is localized; these assertions read words, so the language has
        // to be pinned instead of following whoever runs the suite.
        Loc.Use(Loc.English);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---- the build string -------------------------------------------------

    [Theory]
    [InlineData("1.300.259.0", "3.0.0")]
    [InlineData("1.301.4.0", "3.0.1")]
    [InlineData("1.310.14.0", "3.1.0")]
    public void Identity_version_decodes_to_the_game_version(string build, string expected) =>
        Assert.Equal(expected, VersionScanner.IdFromBuild(build));

    [Theory]
    [InlineData("1.3010.4.0")]   // four digits: the scheme does not say what that means
    [InlineData("1.30.4.0")]     // two digits either
    [InlineData("")]
    [InlineData("irgendwas")]
    public void An_unknown_build_shape_gives_no_version_instead_of_a_guess(string build) =>
        Assert.Null(VersionScanner.IdFromBuild(build));

    // ---- the folder name --------------------------------------------------

    [Theory]
    [InlineData("7DTD-3.0.1", "3.0.1")]
    [InlineData("v3.1.0", "3.1.0")]
    [InlineData("7 Days To Die 3.0.0", "3.0.0")]
    [InlineData("7DTD-3.0.0-UserData", "3.0.0")]
    public void Folder_names_give_up_their_version(string folder, string expected) =>
        Assert.Equal(expected, VersionScanner.IdFromFolder(folder));

    [Theory]
    [InlineData("Smorgasbord")]
    [InlineData("7DTD")]
    [InlineData("")]
    public void A_folder_name_without_a_version_stays_unknown(string folder) =>
        Assert.Null(VersionScanner.IdFromFolder(folder));

    // ---- reading an installation -------------------------------------------

    [Fact]
    public void The_installation_is_asked_first_and_the_folder_name_only_as_a_fallback()
    {
        var dir = FakeInstall("7DTD-egal", "1.301.4.0");

        var c = VersionScanner.Inspect(dir);

        Assert.True(c.HasExe);
        Assert.Equal("1.301.4.0", c.Build);
        Assert.Equal("3.0.1", c.ProposedId);
        Assert.Equal(IdSource.GameConfig, c.Source);
        Assert.False(c.Mismatch);
    }

    [Fact]
    public void Without_a_game_config_the_folder_name_has_to_do()
    {
        var dir = FakeInstall("7DTD-3.1.0", build: null);

        var c = VersionScanner.Inspect(dir);

        Assert.Equal("3.1.0", c.ProposedId);
        Assert.Equal(IdSource.FolderName, c.Source);
        Assert.Contains("folder name", c.Explain());
    }

    /// <summary>
    /// The case that matters: Steam updated the folder in place, so its name is
    /// now a lie. Registering it under the folder name would put "3.0.1" on a
    /// compatibility list although 3.1.0 was tested.
    /// </summary>
    [Fact]
    public void A_folder_whose_name_contradicts_its_build_is_reported_as_a_contradiction()
    {
        var dir = FakeInstall("7DTD-3.0.1", "1.310.14.0");

        var c = VersionScanner.Inspect(dir);

        Assert.True(c.Mismatch);
        Assert.Equal("3.1.0", c.ProposedId);
        Assert.Contains("folder says 3.0.1", c.Explain());
        Assert.Contains("installation says 3.1.0", c.Explain());
    }

    [Fact]
    public void A_folder_without_the_exe_is_not_an_installation()
    {
        var dir = Path.Combine(_root, "7DTD-3.0.0-UserData");
        Directory.CreateDirectory(dir);

        Assert.False(VersionScanner.LooksLikeInstall(dir));
        Assert.False(VersionScanner.Inspect(dir).HasExe);
    }

    [Fact]
    public void Missing_harmony_is_visible_before_a_run_wastes_time_on_it()
    {
        var dir = FakeInstall("7DTD-3.0.1", "1.301.4.0", harmony: false);

        var c = VersionScanner.Inspect(dir);

        Assert.False(c.HasHarmony);
        Assert.Contains("no 0_TFP_Harmony", c.Explain());
    }

    // ---- walking a folder tree ---------------------------------------------

    [Fact]
    public void Scanning_finds_the_installations_and_skips_everything_else()
    {
        FakeInstall("7DTD-3.0.0", "1.300.259.0");
        FakeInstall("7DTD-3.1.0", "1.310.14.0");
        Directory.CreateDirectory(Path.Combine(_root, "7DTD-3.0.0-UserData"));

        var found = VersionScanner.Scan(_root, new MachineConfig());

        Assert.Equal(new[] { "3.0.0", "3.1.0" }, found.Select(c => c.ProposedId));
    }

    /// <summary>
    /// An installation has thousands of folders below it. Walking into them would
    /// take seconds per version and could report a nested copy as its own version.
    /// </summary>
    [Fact]
    public void The_walk_does_not_descend_into_an_installation()
    {
        var dir = FakeInstall("7DTD-3.0.1", "1.301.4.0");
        var nested = Path.Combine(dir, "Backup-3.1.0");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, VersionScanner.ExeName), "");

        var found = VersionScanner.Scan(_root, new MachineConfig(), maxDepth: 4);

        Assert.Single(found);
        Assert.Equal("3.0.1", found[0].ProposedId);
    }

    [Fact]
    public void An_already_registered_folder_says_so_instead_of_offering_itself_again()
    {
        var dir = FakeInstall("7DTD-3.0.1", "1.301.4.0");
        var machine = new MachineConfig { GameRoot = _root };
        machine.Versions.Add(new GameVersion { Id = "3.0.1" });

        var c = VersionScanner.Scan(_root, machine).Single();

        Assert.True(c.Registered);
        Assert.Equal("3.0.1", c.RegisteredAs);
        Assert.Equal(dir, c.Dir);
    }

    [Fact]
    public void A_version_pointing_somewhere_else_is_still_recognised_as_registered()
    {
        var dir = FakeInstall("beliebiger-ordner", "1.301.4.0");
        var machine = new MachineConfig { GameRoot = @"E:\ganz-woanders" };
        machine.Versions.Add(new GameVersion { Id = "3.0.1", Path = dir });

        Assert.Equal("3.0.1", VersionScanner.RegisteredAs(machine, dir));
    }

    // ---- helper ------------------------------------------------------------

    private string FakeInstall(string folder, string? build, bool harmony = true)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, VersionScanner.ExeName), "");

        if (harmony) Directory.CreateDirectory(Path.Combine(dir, "Mods", "0_TFP_Harmony"));

        if (build is not null)
        {
            File.WriteAllText(Path.Combine(dir, VersionScanner.GameConfigName),
                $"""
                 <?xml version="1.0" encoding="utf-8"?>
                 <Game configVersion="1">
                   <Identity Name="TheFunPimps.7DaystoDiePC" Publisher="CN=4072923E" Version="{build}" />
                 </Game>
                 """);
        }

        return dir;
    }
}
