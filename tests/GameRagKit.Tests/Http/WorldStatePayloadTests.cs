using FluentAssertions;
using GameRagKit.Http;

namespace GameRagKit.Tests.Http;

public sealed class WorldStatePayloadTests
{
    [Fact]
    public void ToWorldState_Maps_All_Fields()
    {
        var payload = new WorldStatePayload
        {
            TimeOfDay = "night",
            InCombat = true,
            NearbyEntities = new List<NearbyEntityPayload>
            {
                new("goblin-1", "goblin", 3.5)
            },
            PlayerInventory = new List<InventoryItemPayload>
            {
                new("brass_token", 2)
            },
            Custom = new Dictionary<string, string> { ["weather"] = "raining" }
        };

        var worldState = payload.ToWorldState();

        worldState.TimeOfDay.Should().Be("night");
        worldState.InCombat.Should().BeTrue();
        worldState.NearbyEntities.Should().ContainSingle(e => e.Id == "goblin-1" && e.Type == "goblin" && e.DistanceMeters == 3.5);
        worldState.PlayerInventory.Should().ContainSingle(i => i.ItemId == "brass_token" && i.Quantity == 2);
        worldState.Custom.Should().ContainKey("weather").WhoseValue.Should().Be("raining");
    }

    [Fact]
    public void ToWorldState_Defaults_Missing_Quantity_To_One()
    {
        var payload = new WorldStatePayload
        {
            PlayerInventory = new List<InventoryItemPayload> { new("key", null) }
        };

        var worldState = payload.ToWorldState();

        worldState.PlayerInventory.Should().ContainSingle(i => i.ItemId == "key" && i.Quantity == 1);
    }

    [Fact]
    public void ToWorldState_With_No_Collections_Returns_Empty_Lists_Not_Null()
    {
        var payload = new WorldStatePayload();

        var worldState = payload.ToWorldState();

        worldState.NearbyEntities.Should().NotBeNull().And.BeEmpty();
        worldState.PlayerInventory.Should().NotBeNull().And.BeEmpty();
        worldState.Custom.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToAskOptions_With_Null_WorldState_Payload_Produces_Null_WorldState()
    {
        var request = new AskHttpRequest("npc", "question", new AskOptionsPayload());

        var options = request.ToAskOptions();

        options.WorldState.Should().BeNull();
    }

    [Fact]
    public void ToAskOptions_Maps_WorldState_Payload_Through()
    {
        var request = new AskHttpRequest(
            "npc",
            "question",
            new AskOptionsPayload
            {
                WorldState = new WorldStatePayload { TimeOfDay = "dusk" }
            });

        var options = request.ToAskOptions();

        options.WorldState.Should().NotBeNull();
        options.WorldState!.TimeOfDay.Should().Be("dusk");
    }
}
