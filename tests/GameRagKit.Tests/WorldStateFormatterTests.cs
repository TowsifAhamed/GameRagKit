using FluentAssertions;
using GameRagKit;

namespace GameRagKit.Tests;

public sealed class WorldStateFormatterTests
{
    [Fact]
    public void Format_Null_WorldState_Returns_Empty()
    {
        WorldStateFormatter.Format(null).Should().BeEmpty();
    }

    [Fact]
    public void Format_Empty_WorldState_Returns_Empty()
    {
        var worldState = new WorldState();

        WorldStateFormatter.Format(worldState).Should().BeEmpty();
    }

    [Fact]
    public void Format_TimeOfDay_Only()
    {
        var worldState = new WorldState { TimeOfDay = "night" };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().Contain("WORLD STATE:");
        result.Should().Contain("Time of day: night");
    }

    [Fact]
    public void Format_InCombat_True_Adds_Combat_Line()
    {
        var worldState = new WorldState { InCombat = true };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().Contain("Player is currently in combat.");
    }

    [Fact]
    public void Format_InCombat_False_Does_Not_Add_Combat_Line()
    {
        var worldState = new WorldState { InCombat = false };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Format_NearbyEntities_With_Type_And_Distance()
    {
        var worldState = new WorldState
        {
            NearbyEntities = new[]
            {
                new NearbyEntity("goblin-1", "goblin", 3.2),
                new NearbyEntity("chest-1")
            }
        };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().Contain("Nearby: goblin-1 (goblin, 3.2m away); chest-1");
    }

    [Fact]
    public void Format_NearbyEntity_With_Type_Only_No_Distance()
    {
        var worldState = new WorldState
        {
            NearbyEntities = new[] { new NearbyEntity("guard-1", "guard") }
        };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().Contain("Nearby: guard-1 (guard)");
    }

    [Fact]
    public void Format_PlayerInventory_Lists_Items_With_Quantity()
    {
        var worldState = new WorldState
        {
            PlayerInventory = new[]
            {
                new InventoryItem("brass_token", 1),
                new InventoryItem("gold_coin", 42)
            }
        };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().Contain("Player inventory: brass_token x1, gold_coin x42");
    }

    [Fact]
    public void Format_Custom_Fields_Are_Included()
    {
        var worldState = new WorldState
        {
            Custom = new Dictionary<string, string> { ["weather"] = "raining", ["quest_stage"] = "3" }
        };

        var result = WorldStateFormatter.Format(worldState);

        result.Should().Contain("weather: raining");
        result.Should().Contain("quest_stage: 3");
    }

    [Fact]
    public void Format_Custom_Fields_Skips_Blank_Values()
    {
        var worldState = new WorldState
        {
            Custom = new Dictionary<string, string> { ["empty_field"] = "" }
        };

        WorldStateFormatter.Format(worldState).Should().BeEmpty();
    }

    [Fact]
    public void Format_Combines_All_Sections_In_Fixed_Order()
    {
        var worldState = new WorldState
        {
            TimeOfDay = "dawn",
            InCombat = true,
            NearbyEntities = new[] { new NearbyEntity("wolf-1", "wolf", 5) },
            PlayerInventory = new[] { new InventoryItem("sword", 1) },
            Custom = new Dictionary<string, string> { ["weather"] = "foggy" }
        };

        var result = WorldStateFormatter.Format(worldState);

        var timeIndex = result.IndexOf("Time of day: dawn", StringComparison.Ordinal);
        var combatIndex = result.IndexOf("Player is currently in combat.", StringComparison.Ordinal);
        var nearbyIndex = result.IndexOf("Nearby: wolf-1", StringComparison.Ordinal);
        var inventoryIndex = result.IndexOf("Player inventory: sword x1", StringComparison.Ordinal);
        var customIndex = result.IndexOf("weather: foggy", StringComparison.Ordinal);

        timeIndex.Should().BeLessThan(combatIndex);
        combatIndex.Should().BeLessThan(nearbyIndex);
        nearbyIndex.Should().BeLessThan(inventoryIndex);
        inventoryIndex.Should().BeLessThan(customIndex);
    }

    [Fact]
    public void Format_Result_Ends_With_Separator()
    {
        var worldState = new WorldState { TimeOfDay = "night" };

        var result = WorldStateFormatter.Format(worldState);

        result.TrimEnd('\r', '\n').Should().EndWith("---");
    }
}
