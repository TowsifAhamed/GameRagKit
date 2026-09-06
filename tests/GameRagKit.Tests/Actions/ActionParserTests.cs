using FluentAssertions;
using GameRagKit.Actions;
using GameRagKit.Config;

namespace GameRagKit.Tests.Actions;

public sealed class ActionParserTests
{
    private static readonly List<ActionDefinition> GiveItemActions = new()
    {
        new ActionDefinition
        {
            Name = "give_item",
            Description = "Gives an item to the player",
            Args = new List<ActionArgDefinition>
            {
                new() { Name = "item_id", Type = "string", Required = true },
                new() { Name = "quantity", Type = "number", Required = false }
            }
        },
        new ActionDefinition
        {
            Name = "start_quest",
            Args = new List<ActionArgDefinition>
            {
                new() { Name = "quest_id", Type = "string", Required = true }
            }
        }
    };

    private static IReadOnlyList<GameRagBlock> Blocks(params (string Tag, string Json)[] blocks)
        => blocks.Select(b => new GameRagBlock(b.Tag, b.Json)).ToArray();

    [Fact]
    public void Validate_With_No_Blocks_Returns_Empty()
    {
        var result = ActionParser.Validate(Array.Empty<GameRagBlock>(), GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Extracts_Valid_Action()
    {
        var blocks = Blocks(("action", "{\"name\":\"give_item\",\"args\":{\"item_id\":\"brass_token\",\"quantity\":1}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().HaveCount(1);
        result.Actions[0].Name.Should().Be("give_item");
        result.Actions[0].Args.Should().Contain(new KeyValuePair<string, string>("item_id", "brass_token"));
        result.Actions[0].Args.Should().Contain(new KeyValuePair<string, string>("quantity", "1"));
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Extracts_Multiple_Action_Blocks()
    {
        var blocks = Blocks(
            ("action", "{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}"),
            ("action", "{\"name\":\"start_quest\",\"args\":{\"quest_id\":\"find_king\"}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().HaveCount(2);
        result.Actions.Select(a => a.Name).Should().BeEquivalentTo(new[] { "give_item", "start_quest" });
    }

    [Fact]
    public void Validate_Ignores_Blocks_With_Other_Tags()
    {
        var blocks = Blocks(
            ("action", "{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}"),
            ("mood", "{\"value\":\"wary\"}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Actions[0].Name.Should().Be("give_item");
    }

    [Fact]
    public void Validate_Rejects_Action_Not_In_Allowed_List()
    {
        var blocks = Blocks(("action", "{\"name\":\"delete_world\",\"args\":{}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("delete_world") && e.Contains("not in the NPC's allowed action list"));
    }

    [Fact]
    public void Validate_Rejects_Call_Missing_Required_Arg()
    {
        var blocks = Blocks(("action", "{\"name\":\"give_item\",\"args\":{}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("missing required arg \"item_id\""));
    }

    [Fact]
    public void Validate_Allows_Missing_Optional_Arg()
    {
        var blocks = Blocks(("action", "{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Actions[0].Args.Should().ContainKey("item_id");
        result.Actions[0].Args.Should().NotContainKey("quantity");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Drops_Undeclared_Args()
    {
        var blocks = Blocks(("action", "{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\",\"malicious_field\":\"drop_table\"}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Actions[0].Args.Should().NotContainKey("malicious_field");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Handles_Malformed_Json_Without_Throwing()
    {
        var blocks = Blocks(("action", "{not valid json"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("Malformed action JSON"));
    }

    [Fact]
    public void Validate_With_No_Allowed_Actions_Returns_Empty_Even_If_Block_Present()
    {
        var blocks = Blocks(("action", "{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}"));

        var result = ActionParser.Validate(blocks, Array.Empty<ActionDefinition>());

        result.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Is_Case_Insensitive_On_Action_Name()
    {
        var blocks = Blocks(("action", "{\"name\":\"GIVE_ITEM\",\"args\":{\"item_id\":\"key\"}}"));

        var result = ActionParser.Validate(blocks, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Errors.Should().BeEmpty();
    }
}
