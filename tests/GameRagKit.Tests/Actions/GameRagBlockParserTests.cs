using FluentAssertions;
using GameRagKit.Actions;

namespace GameRagKit.Tests.Actions;

public sealed class GameRagBlockParserTests
{
    [Fact]
    public void Parse_With_No_Block_Returns_Text_Unchanged()
    {
        var text = "Hello traveler, welcome to the keep.";

        var result = GameRagBlockParser.Parse(text);

        result.CleanedText.Should().Be(text);
        result.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Extracts_Single_Block_And_Strips_It_From_Text()
    {
        var text = "Ah, you've proven yourself.\n[[gamerag:action]]\n{\"name\":\"give_item\"}\n[[/gamerag]]\nTake this token and guard it well.";

        var result = GameRagBlockParser.Parse(text);

        result.Blocks.Should().ContainSingle();
        result.Blocks[0].Tag.Should().Be("action");
        result.Blocks[0].RawJson.Should().Be("{\"name\":\"give_item\"}");
        result.CleanedText.Should().NotContain("[[gamerag");
        result.CleanedText.Should().Contain("Ah, you've proven yourself.");
        result.CleanedText.Should().Contain("Take this token and guard it well.");
    }

    [Fact]
    public void Parse_Extracts_Multiple_Blocks_With_Different_Tags()
    {
        var text = "[[gamerag:action]]\n{\"name\":\"a\"}\n[[/gamerag]]\n[[gamerag:mood]]\n{\"value\":\"wary\"}\n[[/gamerag]]";

        var result = GameRagBlockParser.Parse(text);

        result.Blocks.Should().HaveCount(2);
        result.Blocks[0].Tag.Should().Be("action");
        result.Blocks[1].Tag.Should().Be("mood");
    }

    [Fact]
    public void Parse_Does_Not_Touch_Ordinary_Markdown_Code_Fences()
    {
        var text = "Here is some ```code``` and a ```python\nprint(1)\n``` block.";

        var result = GameRagBlockParser.Parse(text);

        result.CleanedText.Should().Be(text);
        result.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Empty_Text_Returns_Empty()
    {
        var result = GameRagBlockParser.Parse(string.Empty);

        result.CleanedText.Should().BeEmpty();
        result.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Unclosed_Block_Is_Not_Extracted()
    {
        var text = "Visible start [[gamerag:action]]\n{\"name\":\"a\"}";

        var result = GameRagBlockParser.Parse(text);

        result.Blocks.Should().BeEmpty();
        result.CleanedText.Should().Be(text);
    }

    [Fact]
    public void Parse_Tag_Is_Case_Preserved_But_Callers_May_Compare_Case_Insensitively()
    {
        var text = "[[gamerag:ACTION]]\n{\"name\":\"a\"}\n[[/gamerag]]";

        var result = GameRagBlockParser.Parse(text);

        result.Blocks.Should().ContainSingle();
        result.Blocks[0].Tag.Should().Be("ACTION");
    }
}
