namespace DumpingGroundMap
{
    internal enum LootKind { Ordinary, Valuable, Foreign, SupplyCrate, Keycard }

    /// <summary>Slot identity includes its kind; identical item IDs may occur in two pools.</summary>
    internal sealed class LootSlot
    {
        internal readonly string ItemId;
        internal readonly LootKind Kind;
        internal readonly int Category;

        internal LootSlot(string itemId, LootKind kind, int category)
        {
            ItemId = itemId;
            Kind = kind;
            Category = category;
        }
    }
}
