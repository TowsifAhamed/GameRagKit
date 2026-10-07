using FluentAssertions;
using GameRagKit.Scenes;

namespace GameRagKit.Tests.Scenes;

public sealed class SceneDirectorTests
{
    // The helpers under test only read names/ids, never the agent.
    private static readonly SceneParticipant Mira = new("tavern-keeper-mira", "Mira", null!);
    private static readonly SceneParticipant Bram = new("blacksmith-bram", "Bram", null!);
    private static readonly SceneParticipant Oswin = new("town-crier-oswin", "Oswin, Town Crier", null!);
    private static readonly SceneParticipant[] Everyone = { Mira, Bram, Oswin };

    [Theory]
    [InlineData("Mira, does Bram owe you money?", "tavern-keeper-mira")]
    [InlineData("Is the forge busy today, Bram?", "blacksmith-bram")]
    [InlineData("Hey Oswin what's the news", "town-crier-oswin")]
    [InlineData("oswin: any news from the capital?", "town-crier-oswin")]
    [InlineData("Good evening, Mira.", "tavern-keeper-mira")]
    public void FindVocatives_Picks_The_Person_Being_Spoken_To(string line, string expected)
    {
        SceneDirector.FindVocatives(line, Everyone).Select(p => p.NpcId).Should().Equal(expected);
    }

    [Fact]
    public void FindVocatives_Handles_Several_Addressees()
    {
        SceneDirector.FindVocatives("Bram and Mira, who opened first today?", Everyone)
            .Select(p => p.NpcId)
            .Should().BeEquivalentTo(new[] { "blacksmith-bram", "tavern-keeper-mira" });
    }

    [Fact]
    public void FindVocatives_Catches_Mid_Sentence_Address_Without_Leading_Comma()
    {
        // Real whisper.cpp transcript of "Hey Bram, is the forge busy today? And Mira, did he pay his tab?"
        SceneDirector.FindVocatives("Hey Bram is the forge busy today and Mira, did he pay his tab?", Everyone)
            .Select(p => p.NpcId)
            .Should().BeEquivalentTo(new[] { "blacksmith-bram", "tavern-keeper-mira" });
    }

    [Theory]
    [InlineData("What do you all think of Bram?")]
    [InlineData("Does Mira water down the ale")]
    [InlineData("Where can I buy a sword?")]
    public void FindVocatives_Ignores_Names_That_Are_Only_Mentioned(string line)
    {
        SceneDirector.FindVocatives(line, Everyone).Should().BeEmpty();
    }

    [Fact]
    public void FindAddressed_Matches_Short_Name_From_Display_Name()
    {
        SceneDirector.FindAddressed("Ask Oswin, he hears everything.", Everyone, exclude: "tavern-keeper-mira")
            .Select(p => p.NpcId).Should().Equal("town-crier-oswin");
    }

    [Fact]
    public void FindAddressed_Excludes_The_Speaker()
    {
        SceneDirector.FindAddressed("I'm Mira, and Bram here owes me.", Everyone, exclude: "tavern-keeper-mira")
            .Select(p => p.NpcId).Should().Equal("blacksmith-bram");
    }

    [Theory]
    [InlineData("Mira: Welcome in, traveler!", "Welcome in, traveler!")]
    [InlineData("(laughs) Bram's good for a round.", "Bram's good for a round.")]
    [InlineData("*wipes the bar* Ale's fresh today.", "Ale's fresh today.")]
    [InlineData("\"Quiet night so far.\"", "Quiet night so far.")]
    public void CleanSpokenText_Strips_Prefixes_And_Stage_Directions(string raw, string expected)
    {
        SceneDirector.CleanSpokenText(raw, Mira, Everyone).Should().Be(expected);
    }

    [Fact]
    public void CleanSpokenText_Caps_Monologues_At_Three_Sentences()
    {
        var raw = "Hear ye! The messenger rode north. He carried a token. Nobody knows why. Stay tuned!";

        SceneDirector.CleanSpokenText(raw, Oswin, Everyone).Should().Be("Hear ye! The messenger rode north. He carried a token.");
    }

    [Fact]
    public void CleanSpokenText_Drops_Lines_Written_For_Other_Characters()
    {
        var raw = "Ask Bram about the gate.\nBram: Aye, I forged its hinges myself.";

        SceneDirector.CleanSpokenText(raw, Mira, Everyone).Should().Be("Ask Bram about the gate.");
    }

    [Fact]
    public void BuildSceneInstructions_Marks_Cut_Off_Lines_And_Reminds_The_Speaker_Of_Unsaid_Words()
    {
        var transcript = new[]
        {
            new SceneLine(SceneLine.PlayerSpeaker, "What happened at the gate?"),
            new SceneLine("blacksmith-bram", "Well, the guard told me the messenger", Interrupted: true),
            new SceneLine(SceneLine.PlayerSpeaker, "Mira, is that true?")
        };
        var unsaid = new Dictionary<string, string> { ["blacksmith-bram"] = "left a sealed letter with Mira." };

        var forBram = SceneDirector.BuildSceneInstructions(Bram, Everyone, transcript, unsaid);
        var forMira = SceneDirector.BuildSceneInstructions(Mira, Everyone, transcript, unsaid);

        forBram.Should().Contain("Bram: Well, the guard told me the messenger — (cut off mid-sentence)");
        forBram.Should().Contain("cut off before you could say: \"left a sealed letter with Mira.\"");
        // Only the NPC who was cut off is told what it didn't get to say.
        forMira.Should().Contain("(cut off mid-sentence)");
        forMira.Should().NotContain("sealed letter");
    }

    [Fact]
    public void BuildSceneInstructions_Tells_A_Resumed_Speaker_To_Finish_Its_Point()
    {
        var unsaid = new Dictionary<string, string> { ["blacksmith-bram"] = "left a sealed letter with Mira." };

        var resumed = SceneDirector.BuildSceneInstructions(Bram, Everyone, Array.Empty<SceneLine>(), unsaid, continuing: true);
        var ordinary = SceneDirector.BuildSceneInstructions(Bram, Everyone, Array.Empty<SceneLine>(), unsaid);

        resumed.Should().Contain("Pick up exactly where you were cut off");
        ordinary.Should().Contain("Respond to what was just said first");
    }

    [Theory]
    [InlineData("Sorry, go on.")]
    [InlineData("You were saying?")]
    [InlineData("Carry on, I'm listening")]
    public async Task PickResponders_Hands_The_Floor_Back_To_Whoever_Was_Cut_Off(string line)
    {
        var history = new[]
        {
            new SceneLine("tavern-keeper-mira", "Ale's fresh tonight."),
            new SceneLine("blacksmith-bram", "Funny you mention the gate, because", Interrupted: true),
            new SceneLine(SceneLine.PlayerSpeaker, "Wait, wait.")
        };

        var (responders, method) = await new SceneDirector().PickRespondersAsync(
            Everyone, line, history, new SceneOptions { UseLlmRouter = false }, CancellationToken.None);

        method.Should().Be("continue-interrupted");
        responders.Select(p => p.NpcId).Should().Equal("blacksmith-bram");
    }

    [Fact]
    public async Task PickResponders_Uses_Unsaid_When_No_Line_Was_Cut_Mid_Sentence()
    {
        // Mira's whole line was queued but never played, so it's only in Unsaid.
        var options = new SceneOptions
        {
            UseLlmRouter = false,
            Unsaid = new Dictionary<string, string> { ["tavern-keeper-mira"] = "I saw the messenger leave at dawn." }
        };

        var (responders, method) = await new SceneDirector().PickRespondersAsync(
            Everyone, "Go ahead.", Array.Empty<SceneLine>(), options, CancellationToken.None);

        method.Should().Be("continue-interrupted");
        responders.Select(p => p.NpcId).Should().Equal("tavern-keeper-mira");
    }
}
