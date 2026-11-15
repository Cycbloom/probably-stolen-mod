using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal static class LootCalculator
    {
        internal static EffectiveTable Build(Region region, LootSettings settings, bool perk80)
        {
            RegionLootTable theme = region.Loot;
            RegionLootTable vanilla = ScavLoot.Vanilla;
            float specialty = ProbabilityMath.Clamp(settings.SpecialtyPercent, 0f, 100f) / 100f;

            // 1) 两张表各自归一化：非垃圾权重和记成 1，垃圾单独记成
            //    「每 1 份非垃圾权重对应多少垃圾权重」。专精就是这两张表
            //    之间的线性插值，垃圾也一起插。
            float vanillaNonJunk = 0f;
            for (int i = 0; i < vanilla.Count; ++i)
                if (vanilla.Items[i] != "junk") vanillaNonJunk += vanilla.Weights[i];
            float themeNonJunk = 0f;
            for (int i = 0; i < theme.Count; ++i)
                if (theme.Items[i] != "junk") themeNonJunk += theme.Weights[i];
            if (vanillaNonJunk <= 0f) vanillaNonJunk = 1f;
            if (themeNonJunk <= 0f) themeNonJunk = 1f;

            // 两张表的并集：主题表在前，原版表独有、主题表没有的物品补在后面。
            List<string> idList = new List<string>(theme.Items);
            for (int i = 0; i < vanilla.Count; ++i)
                if (theme.IndexOf(vanilla.Items[i]) < 0) idList.Add(vanilla.Items[i]);

            int count = idList.Count;
            string[] ids = idList.ToArray();
            int junkIndex = Array.IndexOf(ids, "junk");
            float[] weights = new float[count];
            float otherTotal = 0f;
            for (int i = 0; i < count; ++i)
            {
                if (i == junkIndex) continue;
                int themeIndex = theme.IndexOf(ids[i]);
                int vanillaIndex = vanilla.IndexOf(ids[i]);
                float themeWeight = themeIndex >= 0 ? theme.Weights[themeIndex] / themeNonJunk : 0f;
                float vanillaWeight = vanillaIndex >= 0 ? vanilla.Weights[vanillaIndex] / vanillaNonJunk : 0f;
                float weight = (1f - specialty) * vanillaWeight + specialty * themeWeight;
                weights[i] = weight;
                otherTotal += weight;
            }

            // 2) 垃圾概率 = 插值后的垃圾占比 × 垃圾倍率。垃圾倍率是唯一能
            //    单独调垃圾多少的旋钮，专精插值不会把它清零。
            float vanillaJunkShare = vanilla.JunkWeight / vanillaNonJunk;
            float themeJunkShare = theme.JunkWeight / themeNonJunk;
            float junkPerNonJunk = (1f - specialty) * vanillaJunkShare + specialty * themeJunkShare;
            float junkBase = junkPerNonJunk / (junkPerNonJunk + 1f);
            float junkProb = ProbabilityMath.Clamp(junkBase * settings.JunkPercent / 100f, 0f, 0.995f);

            // 3) The valuable share and the 80-tier crate/keycard shares are
            //    carved out of the non-junk items only.
            float highChance = ProbabilityMath.Clamp((perk80 ? settings.HighPercent : 0f), 0f, 30f) / 100f;
            string crateId = LootCatalog.CrateFor(region.Id);
            string cardId = LootCatalog.CardFor(region.Id);
            bool withCrate = crateId != null;
            bool withCard = cardId != null;
            float crateChance = withCrate && perk80 ? ProbabilityMath.Clamp(settings.CratePercent, 0f, 5f) / 100f : 0f;
            float cardChance = withCard && perk80 ? ProbabilityMath.Clamp(settings.KeycardPercent, 0f, 5f) / 100f : 0f;
            float reserved = highChance + crateChance + cardChance;
            if (reserved > 0.995f - junkProb)
            {
                float scale = reserved > 0f ? Math.Max(0f, 0.995f - junkProb) / reserved : 0f;
                highChance *= scale;
                crateChance *= scale;
                cardChance *= scale;
                reserved = highChance + crateChance + cardChance;
            }
            if (otherTotal <= 0f) junkProb = 1f - reserved;
            float normalProb = Math.Max(0f, 1f - junkProb - reserved);

            int highCount = LootCatalog.Valuables.Length;
            int specialCount = (withCrate ? 1 : 0) + (withCard ? 1 : 0);

            string[] items = new string[count + highCount + specialCount];
            float[] probs = new float[count + highCount + specialCount];
            bool[] isHigh = new bool[count + highCount + specialCount];
            bool[] isJunk = new bool[count + highCount + specialCount];

            for (int i = 0; i < count; ++i)
            {
                items[i] = ids[i];
                if (i == junkIndex) probs[i] = junkProb;
                else probs[i] = otherTotal > 0f ? normalProb * (weights[i] / otherTotal) : 0f;
                isJunk[i] = i == junkIndex;
            }

            {
                float highTotal = 0f;
                for (int j = 0; j < highCount; ++j) highTotal += LootCatalog.Valuables[j].WeightFor(region.Id);
                if (highTotal <= 0f) highTotal = 1f;
                for (int j = 0; j < highCount; ++j)
                {
                    int slot = count + j;
                    items[slot] = LootCatalog.Valuables[j].Id;
                    probs[slot] = highChance * LootCatalog.Valuables[j].WeightFor(region.Id) / highTotal;
                    isHigh[slot] = true;
                }
            }

            int slotNext = count + highCount;
            if (withCrate)
            {
                items[slotNext] = crateId;
                probs[slotNext] = crateChance;
                ++slotNext;
            }
            if (withCard)
            {
                items[slotNext] = cardId;
                probs[slotNext] = cardChance;
            }

            // 4) Expected value of one roll.
            float expected = 0f;
            for (int i = 0; i < items.Length; ++i)
                expected += probs[i] * LootCatalog.ValueOf(items[i]);

            float finalJunk = junkIndex >= 0 ? probs[junkIndex] : 0f;
            return new EffectiveTable(items, probs, isHigh, isJunk, highChance, finalJunk, expected,
                count, highCount, region);
        }

    }
}
