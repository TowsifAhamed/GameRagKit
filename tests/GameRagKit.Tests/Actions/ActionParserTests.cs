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

    [Fact]
    public void Parse_With_No_Action_Block_Returns_Text_Unchanged()
    {
        var text = "Hello traveler, welcome to the keep.";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.CleanedText.Should().Be(text);
        result.Actions.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Extracts_Valid_Action_And_Strips_Block_From_Text()
    {
        var text = "Ah, you've proven yourself.\n```action\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"brass_token\",\"quantity\":1}}\n```\nTake this token and guard it well.";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().HaveCount(1);
        result.Actions[0].Name.Should().Be("give_item");
        result.Actions[0].Args.Should().Contain(new KeyValuePair<string, string>("item_id", "brass_token"));
        result.Actions[0].Args.Should().Contain(new KeyValuePair<string, string>("quantity", "1"));
        result.CleanedText.Should().NotContain("```");
        result.CleanedText.Should().Contain("Ah, you've proven yourself.");
        result.CleanedText.Should().Contain("Take this token and guard it well.");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Extracts_Multiple_Action_Blocks()
    {
        var text = "```action\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}\n```\n```action\n{\"name\":\"start_quest\",\"args\":{\"quest_id\":\"find_king\"}}\n```";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().HaveCount(2);
        result.Actions.Select(a => a.Name).Should().BeEquivalentTo(new[] { "give_item", "start_quest" });
    }

    [Fact]
    public void Parse_Rejects_Action_Not_In_Allowed_List()
    {
        var text = "```action\n{\"name\":\"delete_world\",\"args\":{}}\n```";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("delete_world") && e.Contains("not in the NPC's allowed action list"));
        result.CleanedText.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Rejects_Call_Missing_Required_Arg()
    {
        var text = "```action\n{\"name\":\"give_item\",\"args\":{}}\n```";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("missing required arg \"item_id\""));
    }

    [Fact]
    public void Parse_Allows_Missing_Optional_Arg()
    {
        var text = "```action\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}\n```";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Actions[0].Args.Should().ContainKey("item_id");
        result.Actions[0].Args.Should().NotContainKey("quantity");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Drops_Undeclared_Args()
    {
        var text = "```action\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\",\"malicious_field\":\"drop_table\"}}\n```";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Actions[0].Args.Should().NotContainKey("malicious_field");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Handles_Malformed_Json_Without_Throwing()
    {
        var text = "```action\n{not valid json\n```\nStill talking after the broken block.";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("Malformed action JSON"));
        result.CleanedText.Should().Contain("Still talking after the broken block.");
    }

    [Fact]
    public void Parse_With_No_Allowed_Actions_Returns_Text_Unchanged_Even_If_Block_Present()
    {
        var text = "```action\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}\n```";

        var result = ActionParser.Parse(text, Array.Empty<ActionDefinition>());

        result.CleanedText.Should().Be(text);
        result.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Is_Case_Insensitive_On_Action_Name()
    {
        var text = "```action\n{\"name\":\"GIVE_ITEM\",\"args\":{\"item_id\":\"key\"}}\n```";

        var result = ActionParser.Parse(text, GiveItemActions);

        result.Actions.Should().ContainSingle();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Empty_Text_Returns_Empty()
    {
        var result = ActionParser.Parse(string.Empty, GiveItemActions);

        result.CleanedText.Should().BeEmpty();
        result.Actions.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }
}
