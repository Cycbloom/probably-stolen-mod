using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal static class LootCalculator
    {
        internal static EffectiveTable Build(Region region, LootSettings settings, bool perk80, float junkScale = 1f, int reputation = 0,
            ForeignLootTable foreign = null)
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
            // vanillaFraction[i]：这一槽有多大比例来自「非专精」分支（原版表那份）。
            // 声望倾斜对单个物品是整体缩放，不改变这个比例。
            float[] vanillaFraction = new float[count];
            float otherTotal = 0f;
            for (int i = 0; i < count; ++i)
            {
                if (i == junkIndex) continue;
                int themeIndex = theme.IndexOf(ids[i]);
                int vanillaIndex = vanilla.IndexOf(ids[i]);
                float themeWeight = themeIndex >= 0 ? theme.Weights[themeIndex] / themeNonJunk : 0f;
                float vanillaWeight = vanillaIndex >= 0 ? vanilla.Weights[vanillaIndex] / vanillaNonJunk : 0f;
                float vanillaBranch = (1f - specialty) * vanillaWeight;
                float weight = vanillaBranch + specialty * themeWeight;
                weights[i] = weight;
                vanillaFraction[i] = weight > 0f ? vanillaBranch / weight : 0f;
                otherTotal += weight;
            }

            // 2) 垃圾概率 = 插值后的垃圾占比 × 垃圾倍率。垃圾倍率是唯一能
            //    单独调垃圾多少的旋钮，专精插值不会把它清零。
            // Locked previews use the distribution available when that area opens.
            LootBalance.Apply(region, ids, weights, Math.Max(reputation, region.RequiredReputation));
            float junkBase = LootBalance.JunkBase(region);
            float junkProb = ProbabilityMath.Clamp(junkBase * settings.JunkPercent / 100f, 0f, 0.995f);

            // 3) The valuable share and the 80-tier crate/keycard shares are
            //    carved out of the non-junk items only.
            float highChance = ProbabilityMath.Clamp((perk80 ? settings.HighPercent : 0f), 0f, 30f) / 100f;
            string crateId = LootCatalog.CrateFor(region.Id);
            string cardId = LootCatalog.CardFor(region.Id);
            SupplyEntry[] supplies = SupplyPool.For(region.Id);
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
            // 3b) 外来条目：设置值是上限，实际份额取「上限」与「这些条目在原版组
            //     里自然占的比例」中的较小值。池空时份额归零，概率不留空洞。
            int foreignCount = 0;
            float foreignChance = 0f;
            if (foreign != null && !foreign.IsEmpty)
            {
                float cap = ProbabilityMath.Clamp(settings.ForeignPercent, 0f, 8f) / 100f;
                if (cap > 0f)
                {
                    foreignChance = Math.Min(cap, foreign.NaturalShare);
                    float room = Math.Max(0f, 0.995f - junkProb - reserved);
                    foreignChance = Math.Min(foreignChance, room);
                    if (foreignChance > 0f) foreignCount = foreign.Count;
                }
            }
            if (foreignCount == 0) foreignChance = 0f;

            // 普通槽扣掉外来份额之前的总量；外来份额在 3c 里按「非专精优先」
            // 从垃圾与普通槽里扣，扣完总量仍然是 1。
            if (otherTotal <= 0f) junkProb = Math.Max(0f, 1f - reserved);
            float normalProb = Math.Max(0f, 1f - junkProb - reserved);

            int highCount = LootCatalog.Valuables.Length;
            int specialCount = supplies.Length * 2;
            int foreignStart = count + highCount;

            string[] items = new string[count + highCount + foreignCount + specialCount];
            float[] probs = new float[count + highCount + foreignCount + specialCount];
            bool[] isHigh = new bool[count + highCount + foreignCount + specialCount];
            bool[] isJunk = new bool[count + highCount + foreignCount + specialCount];

            for (int i = 0; i < count; ++i)
            {
                items[i] = ids[i];
                if (i == junkIndex) probs[i] = junkProb;
                else probs[i] = otherTotal > 0f ? normalProb * (weights[i] / otherTotal) : 0f;
                isJunk[i] = i == junkIndex;
            }

            // 3c) 外来份额优先从「非专精」部分出：垃圾，加上普通物品里由原版表
            //     插值来的那一份；两者按各自占比等比压缩，专精物品最后才动。
            //     非专精不够时（例如专精 100% 且垃圾关闭）才从专精部分按比例扣。
            if (foreignCount > 0 && foreignChance > 0f)
            {
                float[] vanillaPart = new float[count];
                float[] specialtyPart = new float[count];
                float nonSpecialty = junkIndex >= 0 ? junkProb : 0f;
                float specialtyTotal = 0f;
                for (int i = 0; i < count; ++i)
                {
                    if (i == junkIndex) continue;
                    vanillaPart[i] = probs[i] * vanillaFraction[i];
                    specialtyPart[i] = probs[i] - vanillaPart[i];
                    nonSpecialty += vanillaPart[i];
                    specialtyTotal += specialtyPart[i];
                }
                float taken = Math.Min(foreignChance, nonSpecialty);
                if (taken > 0f && nonSpecialty > 0f)
                {
                    float scale = taken / nonSpecialty;
                    if (junkIndex >= 0) junkProb -= junkProb * scale;
                    for (int i = 0; i < count; ++i)
                    {
                        if (i == junkIndex) continue;
                        probs[i] -= vanillaPart[i] * scale;
                    }
                }
                float rest = foreignChance - taken;
                if (rest > 0f && specialtyTotal > 0f)
                {
                    float scale = Math.Min(1f, rest / specialtyTotal);
                    for (int i = 0; i < count; ++i)
                    {
                        if (i == junkIndex) continue;
                        probs[i] -= specialtyPart[i] * scale;
                    }
                }
                if (junkIndex >= 0) probs[junkIndex] = junkProb;
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

            if (foreignCount > 0)
            {
                float poolTotal = foreign.Total;
                for (int j = 0; j < foreignCount; ++j)
                {
                    int slot = foreignStart + j;
                    items[slot] = foreign.Ids[j];
                    probs[slot] = poolTotal > 0f ? foreignChance * foreign.Weights[j] / poolTotal : 0f;
                }
            }

            int slotNext = foreignStart + foreignCount;
            int supplyTotal = 0;
            foreach (SupplyEntry entry in supplies) supplyTotal += entry.Weight;
            foreach (SupplyEntry entry in supplies)
            {
                float share = entry.Weight / (float)supplyTotal;
                items[slotNext] = entry.CrateId;
                probs[slotNext] = crateChance * share;
                ++slotNext;
                items[slotNext] = entry.CardId;
                probs[slotNext] = cardChance * share;
                ++slotNext;
            }

            // 4) Match the native event roll: scale only junk's weight, then
            // normalize the entire table, including valuables and crate/card slots.
            junkScale = ProbabilityMath.NormalizeJunkScale(junkScale);
            if (junkScale != 1f)
            {
                float total = 0f;
                for (int i = 0; i < probs.Length; ++i)
                {
                    if (isJunk[i]) probs[i] *= junkScale;
                    total += probs[i];
                }
                if (total > 0f)
                    for (int i = 0; i < probs.Length; ++i) probs[i] /= total;
                highChance = total > 0f ? highChance / total : 0f;
            }

            // 5) Expected value of one event-adjusted roll.
            float expected = 0f;
            for (int i = 0; i < items.Length; ++i)
                expected += probs[i] * LootCatalog.ExpectedValueOf(items[i], region.Id);

            float finalJunk = junkIndex >= 0 ? probs[junkIndex] : 0f;
            return new EffectiveTable(items, probs, isHigh, highChance, finalJunk, expected,
                count, highCount, region, junkScale, null, null, null, foreignStart, foreignCount);
        }

    }
}
