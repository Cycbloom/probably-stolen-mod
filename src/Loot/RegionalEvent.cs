using System;
using System.Globalization;

namespace DumpingGroundMap
{
    internal sealed class RegionalEvent
    {
        internal readonly string RegionId;
        private readonly string titleZh, titleEn, storyZh, storyEn, goodsZh, goodsEn;
        private RegionalEvent(string id, string zh, string en, string story, string storyEnglish, string goods, string goodsEnglish)
        {
            RegionId = id; titleZh = zh; titleEn = en; storyZh = story; storyEn = storyEnglish;
            goodsZh = goods; goodsEn = goodsEnglish;
        }
        internal string Title { get { return Copy.Text(titleZh, titleEn); } }
        internal float JunkMultiplier
        {
            get { return RegionId == "scrap" || RegionId == "med" ? 0.6f
                : RegionId == "water" ? 0.85f : RegionId == "vehicles" ? 0.8f : 0.75f; }
        }
        internal string Description
        {
            get
            {
                string effect = Copy.Text("普通垃圾权重 −", "Ordinary junk weight -")
                    + ((1f - JunkMultiplier) * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
                if (RegionId == "gate") effect += Copy.Text(" · 受伤风险 −20%", " · Wound risk -20%");
                else effect += Copy.Text(" · ", " · ") + Copy.Text(goodsZh, goodsEn)
                    + Copy.Text("权重最多 +50%", " weight up to +50%");
                if (RegionId == "water") effect += Copy.Text(" · 水量分布中心55%→65%", " · Fill center 55% to 65%");
                return effect;
            }
        }
        internal string Tooltip
        {
            get { return Title + "\n" + Copy.Text(storyZh, storyEn) + "\n" + Description
                + Copy.Text("\n今日有效，明日结束。仅调整普通主掉落；贵重、箱卡和大小奖规则不变。\n特产权重在普通非垃圾池内重分配；极端设置下降低倾斜幅度，避免事件降低收益。",
                    "\nToday only. Applies to ordinary main loot; rare, crate/card and reward rules stay unchanged.\nTheme weights are redistributed within ordinary non-junk loot; strength is reduced at extreme settings to prevent a loss of EV."); }
        }
        internal bool Targets(Region region, string itemId)
        {
            int category = region.Loot.CategoryOf(itemId);
            switch (RegionId)
            {
                case "refuse": return category == 1 || category == 2;
                case "scrap": return category == 1;
                case "electronics": return category == 1 || (category == 3 && itemId != "system_module_ruined" && itemId != "system_module_corrupt");
                case "vehicles": return category == 2 || category == 3;
                case "med": return category == 1 || category == 2;
                case "weapons": return category == 3 || category == 4;
                case "water": return PumpWater.IsContainer(itemId);
                default: return false;
            }
        }
        internal static readonly RegionalEvent[] All =
        {
            new RegionalEvent("gate", "分拣后的废料", "Sorted Waste", "有人把碎玻璃和杂物归拢到了路边，可用的东西露了出来。", "Someone cleared the glass and clutter, uncovering usable goods.", "", ""),
            new RegionalEvent("scrap", "工坊清运", "Workshop Clearance", "工坊刚清出一批边角料，废堆里还能找到不少可用材料。", "A workshop has dumped fresh offcuts and usable stock.", "原材料", "Raw materials"),
            new RegionalEvent("refuse", "商铺集中清仓", "Shop Clear-out", "一批商铺同时清仓，食品和日用品混在新送来的废料中。", "Local shops have discarded food and household stock.", "食品、日用品", "Food and household goods"),
            new RegionalEvent("med", "药房清库", "Pharmacy Clearance", "药房腾空了旧库房，一些医疗耗材和药品还用得上。", "A pharmacy has cleared storage; some supplies and medicine remain useful.", "医疗耗材、药品", "Medical supplies and medicine"),
            new RegionalEvent("weapons", "军械清退", "Armory Clear-out", "退下来的军械箱被倒进废堆，散落着弹药和配件。", "Discarded armory crates have spilled ammunition and gun parts.", "弹药、配件", "Ammo and gun mods"),
            new RegionalEvent("electronics", "设备换代", "Equipment Upgrade", "一批淘汰设备刚送到，电子件和能用的模组还没被挑走。", "Newly discarded equipment still holds components and usable modules.", "电子件、可用模组", "Electronics and usable modules"),
            new RegionalEvent("vehicles", "新车入场", "Fresh Wrecks", "新拖来的报废车还没拆完，车厢里留着工具和可拆零件。", "Freshly towed wrecks still contain tools and salvageable parts.", "工具、机械零件", "Tools and mechanical parts"),
            new RegionalEvent("water", "储水罐排空", "Tank Drainage", "旧储水罐正在清空，周围散落着还带水的瓶桶。", "Old tanks are being drained, leaving partly filled bottles and jugs.", "带水容器", "Water containers")
        };
        internal static RegionalEvent For(string regionId) { return Array.Find(All, e => e.RegionId == regionId); }
    }

    internal static class RegionalEventRules
    {
        internal static float WoundChance(float original, bool active) { return active ? original * 0.8f : original; }

        internal static EffectiveTable Apply(Region region, EffectiveTable original, RegionalEvent effect)
        {
            if (effect == null || effect.RegionId != region.Id || original.WithoutRegionalEvent != null) return original;
            float[] probs = (float[])original.Probs.Clone();
            float[] generic = (float[])original.Probs.Clone();
            float[] values = new float[original.Count];
            float mass = 0f, junk = 0f, normal = 0f, targetTotal = 0f;
            for (int i = 0; i < original.Count; ++i)
            {
                values[i] = LootCatalog.ExpectedValueOf(original.Items[i], region.Id);
                if (original.Slots[i].Kind != LootKind.Ordinary) continue;
                mass += probs[i];
                if (original.Items[i] == "junk") { junk += probs[i]; continue; }
                normal += probs[i];
                if (region.Id == "water" && PumpWater.IsContainer(original.Items[i]))
                    values[i] = PumpWater.ExpectedValue(original.Items[i], LootCatalog.ValueOf(original.Items[i]), true);
                probs[i] *= effect.Targets(region, original.Items[i]) ? 1.5f : 1f;
                targetTotal += probs[i];
            }
            float denominator = junk * effect.JunkMultiplier + normal;
            float newJunk = denominator > 0f ? mass * junk * effect.JunkMultiplier / denominator : junk;
            float available = Math.Max(0f, mass - newJunk);
            for (int i = 0; i < probs.Length; ++i)
            {
                if (original.Slots[i].Kind != LootKind.Ordinary) continue;
                if (original.Items[i] == "junk") probs[i] = generic[i] = newJunk;
                else
                {
                    probs[i] = targetTotal > 0f ? available * probs[i] / targetTotal : 0f;
                    generic[i] = normal > 0f ? available * original.Probs[i] / normal : 0f;
                }
            }
            float expected = Dot(probs, values), safe = Dot(generic, values);
            // Thematic events must not become a monetary penalty at 0% junk or
            // other unusual user settings. Keep the strongest affordable bias.
            if (expected < original.Ev && safe > expected)
            {
                float strength = Math.Max(0f, Math.Min(1f, (safe - original.Ev) / (safe - expected)));
                for (int i = 0; i < probs.Length; ++i) probs[i] = generic[i] + strength * (probs[i] - generic[i]);
                expected = Dot(probs, values);
            }
            int junkSlot = Array.IndexOf(original.Items, "junk");
            return new EffectiveTable(original.Items, probs, original.IsHigh, original.HighChance,
                junkSlot >= 0 ? probs[junkSlot] : 0f, expected, original.HighStart, original.HighCount,
                region, original.JunkScale, original, effect, values);
        }
        private static float Dot(float[] probabilities, float[] values)
        {
            float total = 0f;
            for (int i = 0; i < probabilities.Length; ++i) total += probabilities[i] * values[i];
            return total;
        }
    }
}
