using FluentAssertions;
using GameRagKit.Config;
using GameRagKit.Storage;

namespace GameRagKit.Tests.Storage;

public sealed class IndexScopeKeyTests
{
    [Fact]
    public void Two_Keys_For_The_Same_Scope_Are_Equal_Even_With_Different_TierPath_List_Instances()
    {
        // PersonaConfig.TierPath (when built from world_id/region_id/faction_id sugar rather
        // than an explicit Tiers list) allocates a fresh List on every access, so two calls
        // for the logically identical scope carry different TierPath list instances. Equality
        // must be by Scope, not by default record-struct member-wise comparison (which would
        // compare those lists by reference and wrongly treat identical scopes as different
        // dictionary keys -- this broke indexCache/_locks lookups before Scope-based equality
        // was added).
        var persona = new PersonaConfig { Id = "guard", WorldId = "eldoria", RegionId = "valeria", FactionId = "city-guard" };

        var key1 = IndexScopeKey.ForTier(persona, "faction");
        var key2 = IndexScopeKey.ForTier(persona, "faction");

        key1.Should().Be(key2);
        key1.GetHashCode().Should().Be(key2.GetHashCode());

        var dict = new Dictionary<IndexScopeKey, string> { [key1] = "value" };
        dict.TryGetValue(key2, out var found).Should().BeTrue();
        found.Should().Be("value");
    }

    [Fact]
    public void ForTier_Works_With_Arbitrary_Dev_Declared_Tier_Names_Not_Just_World_Region_Faction()
    {
        var persona = new PersonaConfig
        {
            Id = "shop-clerk",
            Tiers = new List<PersonaTier>
            {
                new("continent", "aros"),
                new("kingdom", "eldoria"),
                new("guild", "merchants-guild")
            }
        };

        IndexScopeKey.ForTier(persona, "continent").Scope.Should().Be("continent:aros");
        IndexScopeKey.ForTier(persona, "kingdom").Scope.Should().Be("kingdom:eldoria");
        IndexScopeKey.ForTier(persona, "guild").Scope.Should().Be("guild:merchants-guild");
    }

    [Fact]
    public void TierPath_From_WorldRegionFaction_Sugar_Matches_Explicit_Tiers_Equivalent()
    {
        var sugarPersona = new PersonaConfig { Id = "guard", WorldId = "eldoria", RegionId = "valeria", FactionId = "city-guard" };
        var explicitPersona = new PersonaConfig
        {
            Id = "guard",
            Tiers = new List<PersonaTier> { new("world", "eldoria"), new("region", "valeria"), new("faction", "city-guard") }
        };

        sugarPersona.TierPath.Should().BeEquivalentTo(explicitPersona.TierPath, options => options.WithStrictOrdering());
    }

    [Fact]
    public void FromSource_Routes_To_A_Custom_Tier_By_Name()
    {
        var persona = new PersonaConfig
        {
            Id = "shop-clerk",
            Tiers = new List<PersonaTier> { new("guild", "merchants-guild") }
        };
        var source = new SourceConfig { File = "guild-charter.md", Tier = "guild" };

        var scope = IndexScopeKey.FromSource(persona, source);

        scope.Scope.Should().Be("guild:merchants-guild");
    }

    [Fact]
    public void FromSource_Falls_Back_To_Persona_Scope_When_Tier_Name_Does_Not_Match_Any_Declared_Tier()
    {
        var persona = new PersonaConfig
        {
            Id = "shop-clerk",
            Tiers = new List<PersonaTier> { new("guild", "merchants-guild") }
        };
        var source = new SourceConfig { File = "notes.md", Tier = "common" };

        var scope = IndexScopeKey.FromSource(persona, source);

        scope.Scope.Should().Be("npc:shop-clerk");
    }
}
