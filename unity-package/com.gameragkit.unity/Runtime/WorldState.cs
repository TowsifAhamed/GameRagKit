using System;

namespace GameRagKit.Unity
{
    /// <summary>
    /// Mirrors the server's WorldStatePayload shape (see docs/world-state.md). Serialized
    /// with JsonUtility -- unlike action args, every field here has a fixed shape, so the
    /// built-in serializer is sufficient.
    /// </summary>
    [Serializable]
    public class WorldState
    {
        public string timeOfDay;
        public bool inCombat;
        public NearbyEntity[] nearbyEntities = Array.Empty<NearbyEntity>();
        public InventoryItem[] playerInventory = Array.Empty<InventoryItem>();
    }

    [Serializable]
    public class NearbyEntity
    {
        public string id;
        public string type;
        public float distanceMeters;

        public NearbyEntity(string id, string type = null, float distanceMeters = 0f)
        {
            this.id = id;
            this.type = type;
            this.distanceMeters = distanceMeters;
        }
    }

    [Serializable]
    public class InventoryItem
    {
        public string itemId;
        public int quantity = 1;

        public InventoryItem(string itemId, int quantity = 1)
        {
            this.itemId = itemId;
            this.quantity = quantity;
        }
    }
}
