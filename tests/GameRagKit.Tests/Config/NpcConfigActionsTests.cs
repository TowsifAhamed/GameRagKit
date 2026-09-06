using FluentAssertions;
using GameRagKit.Config;

namespace GameRagKit.Tests.Config;

public sealed class NpcConfigActionsTests
{
    [Fact]
    public void LoadFromYaml_Parses_Actions_Section()
    {
        var yaml = """
        persona:
          id: guard-north-gate
          system_prompt: You are a guard.
          actions:
            - name: give_item
              description: Gives an item to the player
              args:
                - name: item_id
                  type: string
                  required: true
                - name: quantity
                  type: number
                  required: false
            - name: start_quest
              args:
                - name: quest_id
                  type: string
        """;

        var config = NpcConfig.LoadFromYaml(yaml);

        config.Persona.Actions.Should().HaveCount(2);
        var giveItem = config.Persona.Actions[0];
        giveItem.Name.Should().Be("give_item");
        giveItem.Description.Should().Be("Gives an item to the player");
        giveItem.Args.Should().HaveCount(2);
        giveItem.Args[0].Name.Should().Be("item_id");
        giveItem.Args[0].Type.Should().Be("string");
        giveItem.Args[0].Required.Should().BeTrue();
        giveItem.Args[1].Required.Should().BeFalse();

        var startQuest = config.Persona.Actions[1];
        startQuest.Name.Should().Be("start_quest");
        startQuest.Args.Should().ContainSingle(a => a.Name == "quest_id");
    }

    [Fact]
    public void LoadFromYaml_Without_Actions_Section_Defaults_To_Empty_List()
    {
        var yaml = """
        persona:
          id: guard-north-gate
          system_prompt: You are a guard.
        """;

        var config = NpcConfig.LoadFromYaml(yaml);

        config.Persona.Actions.Should().BeEmpty();
    }
}
