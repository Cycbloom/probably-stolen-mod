using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal sealed class LootNoticeGroup
    {
        internal readonly int Category;
        internal readonly bool Valuable;
        internal readonly List<int> Slots = new List<int>();

        internal LootNoticeGroup(int category, bool valuable)
        {
            Category = category;
            Valuable = valuable;
        }
    }

    /// <summary>Projects actual slots for display. Crates and cards have separate bonus rows.</summary>
    internal static class LootNoticeModel
    {
        internal static List<LootNoticeGroup> Groups(EffectiveTable table)
        {
            List<LootNoticeGroup> result = new List<LootNoticeGroup>();
            for (int i = 0; i < table.Count; ++i)
            {
                LootSlot slot = table.Slots[i];
                if (slot.Kind == LootKind.SupplyCrate || slot.Kind == LootKind.Keycard) continue;
                bool valuable = slot.Kind == LootKind.Valuable;
                LootNoticeGroup group = result.Find(g => g.Category == slot.Category && g.Valuable == valuable);
                if (group == null)
                {
                    group = new LootNoticeGroup(slot.Category, valuable);
                    result.Add(group);
                }
                group.Slots.Add(i);
            }
            return result;
        }
    }
}
