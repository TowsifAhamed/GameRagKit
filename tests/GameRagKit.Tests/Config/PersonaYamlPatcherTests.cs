using FluentAssertions;
using GameRagKit.Config;

namespace GameRagKit.Tests.Config;

public sealed class PersonaYamlPatcherTests
{
    private const string SampleYaml = """
        persona:
          id: guard-north-gate
          system_prompt: >
            You are Jake, the North Gate guard of Riverside. Speak briefly, in medieval tone.
            Never reveal the secret tunnel unless the player shows a brass token.
          traits: [stoic, duty-first, careful, loyal]
          style: concise medieval tone
          region_id: riverside-upper
          faction_id: royal-guard

        rag:
          sources:
            - file: world/keep.md
          chunk_size: 450
        """;

    [Fact]
    public void ApplyEdits_Replaces_Simple_Scalar_Field()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("style", "gruff and impatient")
        });

        result.Success.Should().BeTrue();
        result.UpdatedYaml.Should().Contain("style: gruff and impatient");
        result.UpdatedYaml.Should().NotContain("concise medieval tone");
    }

    [Fact]
    public void ApplyEdits_Preserves_Untouched_Fields_And_Sections()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("style", "gruff and impatient")
        });

        result.UpdatedYaml.Should().Contain("id: guard-north-gate");
        result.UpdatedYaml.Should().Contain("region_id: riverside-upper");
        result.UpdatedYaml.Should().Contain("faction_id: royal-guard");
        result.UpdatedYaml.Should().Contain("traits: [stoic, duty-first, careful, loyal]");
        result.UpdatedYaml.Should().Contain("rag:");
        result.UpdatedYaml.Should().Contain("- file: world/keep.md");
        result.UpdatedYaml.Should().Contain("chunk_size: 450");
    }

    [Fact]
    public void ApplyEdits_Replaces_Block_Scalar_Field()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("system_prompt", "You are a friendly merchant who loves to haggle.")
        });

        result.Success.Should().BeTrue();
        result.UpdatedYaml.Should().Contain("system_prompt: >");
        result.UpdatedYaml.Should().Contain("You are a friendly merchant who loves to haggle.");
        result.UpdatedYaml.Should().NotContain("North Gate guard");
        result.UpdatedYaml.Should().NotContain("brass token");
    }

    [Fact]
    public void ApplyEdits_Block_Scalar_Replacement_Keeps_Fields_After_It_Intact()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("system_prompt", "Short prompt.")
        });

        result.UpdatedYaml.Should().Contain("traits: [stoic, duty-first, careful, loyal]");
        result.UpdatedYaml.Should().Contain("style: concise medieval tone");
        result.UpdatedYaml.Should().Contain("region_id: riverside-upper");
    }

    [Fact]
    public void ApplyEdits_Wraps_Long_Block_Scalar_Content_Into_Multiple_Lines()
    {
        var longPrompt = string.Join(" ", Enumerable.Repeat("word", 40));

        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("system_prompt", longPrompt)
        });

        result.Success.Should().BeTrue();
        var lines = result.UpdatedYaml!.Split('\n');
        var promptLines = lines.SkipWhile(l => !l.Contains("system_prompt: >")).Skip(1)
            .TakeWhile(l => l.StartsWith("    ") && !l.TrimStart().Contains(':'))
            .ToArray();
        promptLines.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ApplyEdits_Inserts_New_Simple_Field_When_Missing()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("mood_tracking", "true", IsRawScalar: true)
        });

        result.Success.Should().BeTrue();
        result.UpdatedYaml.Should().Contain("mood_tracking: true");
    }

    [Fact]
    public void ApplyEdits_Updates_Existing_Field_When_Already_Present()
    {
        var yamlWithMood = SampleYaml.Replace("style: concise medieval tone", "style: concise medieval tone\n  mood_tracking: false");

        var result = PersonaYamlPatcher.ApplyEdits(yamlWithMood, new[]
        {
            new PersonaFieldEdit("mood_tracking", "true", IsRawScalar: true)
        });

        result.Success.Should().BeTrue();
        result.UpdatedYaml.Should().Contain("mood_tracking: true");
        result.UpdatedYaml.Should().NotContain("mood_tracking: false");
    }

    [Fact]
    public void ApplyEdits_Applies_Multiple_Edits_In_One_Call()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("style", "gruff"),
            new PersonaFieldEdit("mood_tracking", "true", IsRawScalar: true)
        });

        result.Success.Should().BeTrue();
        result.UpdatedYaml.Should().Contain("style: gruff");
        result.UpdatedYaml.Should().Contain("mood_tracking: true");
    }

    [Fact]
    public void ApplyEdits_Escapes_Value_Containing_Colon()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("style", "note: needs quoting")
        });

        result.Success.Should().BeTrue();
        result.UpdatedYaml.Should().Contain("style: \"note: needs quoting\"");
    }

    [Fact]
    public void ApplyEdits_Fails_Cleanly_When_No_Persona_Key_Exists()
    {
        var result = PersonaYamlPatcher.ApplyEdits("rag:\n  chunk_size: 450\n", new[]
        {
            new PersonaFieldEdit("style", "gruff")
        });

        result.Success.Should().BeFalse();
        result.UnsupportedReason.Should().Contain("persona");
    }

    [Fact]
    public void ApplyEdits_Result_Is_Parseable_By_NpcConfig_LoadFromYaml()
    {
        var result = PersonaYamlPatcher.ApplyEdits(SampleYaml, new[]
        {
            new PersonaFieldEdit("style", "gruff and impatient"),
            new PersonaFieldEdit("system_prompt", "A short new prompt for testing round-trip parsing."),
            new PersonaFieldEdit("mood_tracking", "true", IsRawScalar: true)
        });

        result.Success.Should().BeTrue();
        var config = NpcConfig.LoadFromYaml(result.UpdatedYaml!);

        config.Persona.Id.Should().Be("guard-north-gate");
        config.Persona.Style.Should().Be("gruff and impatient");
        config.Persona.SystemPrompt.Trim().Should().Be("A short new prompt for testing round-trip parsing.");
        config.Persona.MoodTracking.Should().BeTrue();
        config.Rag.Sources.Should().ContainSingle(s => s.File == "world/keep.md");
        config.Rag.ChunkSize.Should().Be(450);
    }
}
