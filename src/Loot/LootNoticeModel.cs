using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal sealed class LootNoticeGroup
    {
        internal readonly int Category;
        internal readonly bool Valuable;

        /// <summary>其它模组注入的条目，单独一行展示，不并入原版物品那组。</summary>
        internal readonly bool Foreign;

        internal readonly List<int> Slots = new List<int>();

        internal LootNoticeGroup(int category, bool valuable, bool foreign = false)
        {
            Category = category;
            Valuable = valuable;
            Foreign = foreign;
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
                bool foreign = slot.Kind == LootKind.Foreign;
                LootNoticeGroup group = result.Find(g => g.Category == slot.Category && g.Valuable == valuable && g.Foreign == foreign);
                if (group == null)
                {
                    group = new LootNoticeGroup(slot.Category, valuable, foreign);
                    result.Add(group);
                }
                group.Slots.Add(i);
            }
            return result;
        }
    }
}
