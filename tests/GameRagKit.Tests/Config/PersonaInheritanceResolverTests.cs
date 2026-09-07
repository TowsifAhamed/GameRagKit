using FluentAssertions;
using GameRagKit.Config;

namespace GameRagKit.Tests.Config;

public sealed class PersonaInheritanceResolverTests : IDisposable
{
    private readonly string _root;

    public PersonaInheritanceResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gamerag-persona-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteFragment(string tier, string id, string yaml)
    {
        var dir = Path.Combine(_root, tier, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "persona.yaml"), yaml);
    }

    [Fact]
    public void Resolve_Walks_A_Fully_Custom_Dev_Declared_Tier_Chain_Not_Just_World_Region_Faction()
    {
        WriteFragment("continent", "aros", """
        system_prompt: The continent of Aros is scarred by an old war.
        """);
        WriteFragment("kingdom", "eldoria", """
        system_prompt: The kingdom of Eldoria prizes honor above wealth.
        """);
        WriteFragment("guild", "merchants-guild", """
        system_prompt: The Merchants Guild speaks in the language of coin.
        """);

        var persona = new PersonaConfig
        {
            Id = "shop-clerk",
            Tiers = new List<PersonaTier>
            {
                new("continent", "aros"),
                new("kingdom", "eldoria"),
                new("guild", "merchants-guild")
            },
            SystemPrompt = "You personally undercharge friends."
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.SystemPrompt.Should().Be(
            "The continent of Aros is scarred by an old war.\n\n" +
            "The kingdom of Eldoria prizes honor above wealth.\n\n" +
            "The Merchants Guild speaks in the language of coin.\n\n" +
            "You personally undercharge friends.");
    }

    [Fact]
    public void Resolve_With_No_Tier_Ids_And_No_Npc_Prompt_Produces_Empty_System_Prompt()
    {
        var persona = new PersonaConfig { Id = "solo-npc" };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.SystemPrompt.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_With_No_Fragment_Files_Falls_Back_To_Npc_Own_SystemPrompt()
    {
        var persona = new PersonaConfig
        {
            Id = "solo-npc",
            WorldId = "eldoria",
            SystemPrompt = "You are a lone wanderer."
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.SystemPrompt.Should().Be("You are a lone wanderer.");
    }

    [Fact]
    public void Resolve_Concatenates_World_Region_Faction_And_Npc_Prompts_In_Order()
    {
        WriteFragment("world", "eldoria", """
        system_prompt: This is the world of Eldoria, a land recovering from war.
        """);
        WriteFragment("region", "valeria", """
        system_prompt: Valeria is a coastal region known for its markets.
        """);
        WriteFragment("faction", "city-guard", """
        system_prompt: The City Guard values order and duty above all.
        """);

        var persona = new PersonaConfig
        {
            Id = "guard-north-gate",
            WorldId = "eldoria",
            RegionId = "valeria",
            FactionId = "city-guard",
            SystemPrompt = "You personally hate mornings."
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.SystemPrompt.Should().Be(
            "This is the world of Eldoria, a land recovering from war.\n\n" +
            "Valeria is a coastal region known for its markets.\n\n" +
            "The City Guard values order and duty above all.\n\n" +
            "You personally hate mornings.");
    }

    [Fact]
    public void Resolve_Skips_Tiers_Without_A_Fragment_File_Or_Without_A_Declared_Id()
    {
        WriteFragment("world", "eldoria", """
        system_prompt: This is the world of Eldoria.
        """);
        WriteFragment("faction", "city-guard", """
        system_prompt: The City Guard values order and duty above all.
        """);

        var persona = new PersonaConfig
        {
            Id = "guard-north-gate",
            WorldId = "eldoria",
            FactionId = "city-guard"
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.SystemPrompt.Should().Be(
            "This is the world of Eldoria.\n\n" +
            "The City Guard values order and duty above all.");
    }

    [Fact]
    public void Resolve_Npc_Can_Declare_Nothing_And_Fully_Inherit_From_Ancestors()
    {
        WriteFragment("world", "eldoria", """
        system_prompt: This is the world of Eldoria.
        style: formal
        mood_tracking: true
        traits:
          - stoic
        """);
        WriteFragment("faction", "city-guard", """
        system_prompt: The City Guard values order and duty above all.
        traits:
          - dutiful
        """);

        var persona = new PersonaConfig
        {
            Id = "guard-north-gate",
            WorldId = "eldoria",
            FactionId = "city-guard"
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.SystemPrompt.Should().Be(
            "This is the world of Eldoria.\n\nThe City Guard values order and duty above all.");
        resolved.Style.Should().Be("formal");
        resolved.MoodTracking.Should().BeTrue();
        resolved.Traits.Should().Equal("stoic", "dutiful");
    }

    [Fact]
    public void Resolve_Npc_Own_Style_Overrides_Inherited_Style()
    {
        WriteFragment("world", "eldoria", """
        style: formal
        """);

        var persona = new PersonaConfig
        {
            Id = "guard-north-gate",
            WorldId = "eldoria",
            Style = "gruff and impatient"
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.Style.Should().Be("gruff and impatient");
    }

    [Fact]
    public void Resolve_Falls_Back_To_The_Nearest_Ancestor_Style_When_Npc_And_Closer_Tiers_Dont_Set_One()
    {
        WriteFragment("world", "eldoria", """
        style: formal
        """);
        WriteFragment("faction", "city-guard", """
        system_prompt: The City Guard values order and duty above all.
        """);

        var persona = new PersonaConfig
        {
            Id = "guard-north-gate",
            WorldId = "eldoria",
            FactionId = "city-guard"
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.Style.Should().Be("formal");
    }

    [Fact]
    public void Resolve_Falls_Back_To_Concise_Style_When_No_Tier_Declares_One()
    {
        var persona = new PersonaConfig { Id = "solo-npc" };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.Style.Should().Be("concise");
    }

    [Fact]
    public void Resolve_Merges_Actions_From_All_Tiers_Plus_Npc()
    {
        WriteFragment("faction", "city-guard", """
        actions:
          - name: sound_alarm
            description: Alerts nearby guards
        """);

        var persona = new PersonaConfig
        {
            Id = "guard-north-gate",
            FactionId = "city-guard",
            Actions = new List<ActionDefinition>
            {
                new() { Name = "give_item", Description = "Gives an item to the player" }
            }
        };

        var resolved = PersonaInheritanceResolver.Resolve(_root, persona);

        resolved.Actions.Select(a => a.Name).Should().Equal("sound_alarm", "give_item");
    }
}
