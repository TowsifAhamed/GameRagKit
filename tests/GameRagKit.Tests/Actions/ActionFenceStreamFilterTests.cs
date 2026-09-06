using FluentAssertions;
using GameRagKit.Actions;

namespace GameRagKit.Tests.Actions;

public sealed class ActionFenceStreamFilterTests
{
    [Fact]
    public void Push_With_No_Block_Passes_Tokens_Through_After_Flush()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        output.AddRange(filter.Push("Hello "));
        output.AddRange(filter.Push("traveler, "));
        output.AddRange(filter.Push("welcome."));
        output.Add(filter.Flush());

        string.Concat(output).Should().Be("Hello traveler, welcome.");
    }

    [Fact]
    public void Push_Hides_Text_Inside_Block_Delivered_As_Single_Token()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        var wholeMessage = "Ah, you've proven yourself. [[gamerag:action]]\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}\n[[/gamerag]] Take this.";
        output.AddRange(filter.Push(wholeMessage));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Contain("Ah, you've proven yourself.");
        visible.Should().Contain("Take this.");
        visible.Should().NotContain("[[gamerag");
        visible.Should().NotContain("give_item");
    }

    [Fact]
    public void Push_Hides_Block_Split_Across_Many_Small_Tokens()
    {
        var filter = new ActionFenceStreamFilter();
        var fullMessage = "Ah, you've proven yourself. [[gamerag:action]]\n{\"name\":\"give_item\",\"args\":{\"item_id\":\"key\"}}\n[[/gamerag]] Take this token.";
        var output = new List<string>();

        // Simulate a real LLM token stream: push one character at a time.
        foreach (var ch in fullMessage)
        {
            output.AddRange(filter.Push(ch.ToString()));
        }

        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Contain("Ah, you've proven yourself.");
        visible.Should().Contain("Take this token.");
        visible.Should().NotContain("[[gamerag");
        visible.Should().NotContain("give_item");
    }

    [Fact]
    public void Push_Handles_Open_Marker_Split_Exactly_At_Token_Boundary()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        // Split "[[gamerag:action]]" itself across two tokens.
        output.AddRange(filter.Push("Before text [[gam"));
        output.AddRange(filter.Push("erag:action]]\n{\"name\":\"start_quest\",\"args\":{\"quest_id\":\"q1\"}}\n[[/gamerag]] After text"));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Contain("Before text");
        visible.Should().Contain("After text");
        visible.Should().NotContain("[[gamerag");
        visible.Should().NotContain("start_quest");
    }

    [Fact]
    public void Push_Handles_Close_Marker_Split_Across_Tokens()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        output.AddRange(filter.Push("[[gamerag:action]]\n{\"name\":\"start_quest\",\"args\":{}}\n[[/game"));
        output.AddRange(filter.Push("rag]] visible after"));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Be(" visible after");
    }

    [Fact]
    public void Push_With_Multiple_Blocks_Hides_Both()
    {
        var filter = new ActionFenceStreamFilter();
        var message = "A [[gamerag:action]]\n{\"name\":\"a\",\"args\":{}}\n[[/gamerag]] B [[gamerag:mood]]\n{\"value\":\"wary\"}\n[[/gamerag]] C";
        var output = new List<string>();

        output.AddRange(filter.Push(message));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Contain("A");
        visible.Should().Contain("B");
        visible.Should().Contain("C");
        visible.Should().NotContain("[[gamerag");
    }

    [Fact]
    public void Flush_After_Unclosed_Block_Drops_Remaining_Text()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        output.AddRange(filter.Push("Visible start [[gamerag:action]]\n{\"name\":\"give_item\""));
        output.AddRange(filter.Push(",\"args\":{\"item_id\":\"key\"}}"));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Be("Visible start ");
    }

    [Fact]
    public void Push_Does_Not_False_Positive_On_Unrelated_Triple_Backtick_Code_Fence()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        output.AddRange(filter.Push("Here is some ```code``` in my reply."));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Be("Here is some ```code``` in my reply.");
    }

    [Fact]
    public void Push_Does_Not_False_Positive_On_Double_Brackets_Without_Gamerag_Prefix()
    {
        var filter = new ActionFenceStreamFilter();
        var output = new List<string>();

        output.AddRange(filter.Push("See reference [[1]] and [[citation needed]] here."));
        output.Add(filter.Flush());

        var visible = string.Concat(output);
        visible.Should().Be("See reference [[1]] and [[citation needed]] here.");
    }

    [Fact]
    public void Flush_With_Nothing_Buffered_Returns_Empty()
    {
        var filter = new ActionFenceStreamFilter();

        filter.Flush().Should().BeEmpty();
    }

    [Fact]
    public void Flush_Twice_Is_Safe_And_Returns_Empty_Second_Time()
    {
        var filter = new ActionFenceStreamFilter();
        filter.Push("hi");

        var first = filter.Flush();
        var second = filter.Flush();

        first.Should().Be("hi");
        second.Should().BeEmpty();
    }
}
