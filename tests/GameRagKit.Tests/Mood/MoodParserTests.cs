using FluentAssertions;
using GameRagKit.Actions;
using GameRagKit.Mood;

namespace GameRagKit.Tests.Mood;

public sealed class MoodParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<GameRagBlock> Blocks(params (string Tag, string Json)[] blocks)
        => blocks.Select(b => new GameRagBlock(b.Tag, b.Json)).ToArray();

    [Fact]
    public void Validate_With_No_Blocks_Returns_Null_Mood()
    {
        var result = MoodParser.Validate(Array.Empty<GameRagBlock>(), Now);

        result.Mood.Should().BeNull();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Parses_Valid_Mood_Block()
    {
        var blocks = Blocks(("mood", "{\"value\":\"wary\",\"intensity\":0.6}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().NotBeNull();
        result.Mood!.Value.Should().Be("wary");
        result.Mood.Intensity.Should().Be(0.6);
        result.Mood.UpdatedAt.Should().Be(Now);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Defaults_Intensity_To_Half_When_Omitted()
    {
        var blocks = Blocks(("mood", "{\"value\":\"content\"}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood!.Intensity.Should().Be(0.5);
    }

    [Fact]
    public void Validate_Clamps_Intensity_Above_One()
    {
        var blocks = Blocks(("mood", "{\"value\":\"furious\",\"intensity\":5}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood!.Intensity.Should().Be(1.0);
    }

    [Fact]
    public void Validate_Clamps_Intensity_Below_Zero()
    {
        var blocks = Blocks(("mood", "{\"value\":\"calm\",\"intensity\":-3}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood!.Intensity.Should().Be(0.0);
    }

    [Fact]
    public void Validate_Rejects_Missing_Value_Field()
    {
        var blocks = Blocks(("mood", "{\"intensity\":0.5}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Contains("missing required \"value\""));
    }

    [Fact]
    public void Validate_Rejects_Empty_Value()
    {
        var blocks = Blocks(("mood", "{\"value\":\"  \"}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Contains("must not be empty"));
    }

    [Fact]
    public void Validate_Rejects_Non_Numeric_Intensity()
    {
        var blocks = Blocks(("mood", "{\"value\":\"wary\",\"intensity\":\"high\"}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Contains("must be a number"));
    }

    [Fact]
    public void Validate_Handles_Malformed_Json_Without_Throwing()
    {
        var blocks = Blocks(("mood", "{not valid json"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Contains("Malformed mood JSON"));
    }

    [Fact]
    public void Validate_Ignores_Blocks_With_Other_Tags()
    {
        var blocks = Blocks(("action", "{\"name\":\"give_item\"}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().BeNull();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_With_Multiple_Mood_Blocks_Last_Valid_One_Wins()
    {
        var blocks = Blocks(
            ("mood", "{\"value\":\"wary\",\"intensity\":0.3}"),
            ("mood", "{\"value\":\"hostile\",\"intensity\":0.9}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood!.Value.Should().Be("hostile");
        result.Mood.Intensity.Should().Be(0.9);
    }

    [Fact]
    public void Validate_Is_Case_Insensitive_On_Tag()
    {
        var blocks = Blocks(("MOOD", "{\"value\":\"wary\"}"));

        var result = MoodParser.Validate(blocks, Now);

        result.Mood.Should().NotBeNull();
    }
}
