using Testbench.Core.Config;

namespace Testbench.Core.Tests;

/// <summary>
/// One rule for what "--mod seven" means, used by every command that takes the
/// option. It used to live inside RequireMod only, so `tb status --mod seven`
/// compared literally, found nothing, and reported "no run stored yet" for a store
/// that held three runs - the same sentence it uses for an empty store.
/// </summary>
public sealed class ModIdMatchingTests
{
    private static readonly string[] Registered = { "sevendashestodie", "adamantblock" };

    [Fact]
    public void Exact_id_matches()
    {
        Assert.Equal(new[] { "sevendashestodie" },
            ConfigStore.MatchModIds(Registered, "sevendashestodie"));
    }

    [Fact]
    public void Unambiguous_fragment_matches()
    {
        Assert.Equal(new[] { "sevendashestodie" }, ConfigStore.MatchModIds(Registered, "seven"));
        Assert.Equal(new[] { "adamantblock" }, ConfigStore.MatchModIds(Registered, "adamant"));
    }

    [Fact]
    public void Fragment_is_case_insensitive()
    {
        Assert.Equal(new[] { "sevendashestodie" }, ConfigStore.MatchModIds(Registered, "SEVEN"));
    }

    [Fact]
    public void Ambiguous_fragment_returns_every_candidate()
    {
        // The caller has to be able to name them, so this returns all of them
        // rather than picking one and being quietly wrong.
        var hits = ConfigStore.MatchModIds(new[] { "dashmod", "dashextra" }, "dash");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void An_exact_id_wins_over_a_fragment_of_another()
    {
        // "dash" is both an id of its own and a fragment of the other one. Without
        // the exact-first rule this would be ambiguous and unusable.
        Assert.Equal(new[] { "dash" }, ConfigStore.MatchModIds(new[] { "dash", "dashextra" }, "dash"));
    }

    [Fact]
    public void Unknown_fragment_matches_nothing()
    {
        Assert.Empty(ConfigStore.MatchModIds(Registered, "quartz"));
    }

    [Fact]
    public void Duplicates_across_sources_collapse()
    {
        // status resolves against the registered mods AND the ids in the run store,
        // so the same id arrives twice. That must not read as ambiguous.
        var hits = ConfigStore.MatchModIds(
            new[] { "sevendashestodie", "SevenDashesToDie" }, "seven");
        Assert.Single(hits);
    }
}
