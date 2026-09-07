using FluentAssertions;
using GameRagKit.Config;

namespace GameRagKit.Tests.Config;

public sealed class NpcConfigTiersTests
{
    [Fact]
    public void LoadFromYaml_Parses_A_Custom_Tiers_List()
    {
        var yaml = """
        persona:
          id: shop-clerk
          system_prompt: You sell wares in the market.
          tiers:
            - name: continent
              id: aros
            - name: kingdom
              id: eldoria
            - name: guild
              id: merchants-guild
        """;

        var config = NpcConfig.LoadFromYaml(yaml);

        config.Persona.Tiers.Should().HaveCount(3);
        config.Persona.TierPath.Select(t => (t.Name, t.Id)).Should().Equal(
            ("continent", "aros"),
            ("kingdom", "eldoria"),
            ("guild", "merchants-guild"));
    }

    [Fact]
    public void LoadFromYaml_Without_Tiers_Falls_Back_To_WorldRegionFaction_Sugar()
    {
        var yaml = """
        persona:
          id: guard-north-gate
          system_prompt: You are a guard.
          world_id: eldoria
          region_id: valeria
          faction_id: city-guard
        """;

        var config = NpcConfig.LoadFromYaml(yaml);

        config.Persona.Tiers.Should().BeNull();
        config.Persona.TierPath.Select(t => (t.Name, t.Id)).Should().Equal(
            ("world", "eldoria"),
            ("region", "valeria"),
            ("faction", "city-guard"));
    }

    [Fact]
    public void LoadFromYaml_With_Neither_Tiers_Nor_WorldRegionFaction_Produces_Empty_TierPath()
    {
        var yaml = """
        persona:
          id: solo-npc
          system_prompt: You wander alone.
        """;

        var config = NpcConfig.LoadFromYaml(yaml);

        config.Persona.TierPath.Should().BeEmpty();
    }
}
