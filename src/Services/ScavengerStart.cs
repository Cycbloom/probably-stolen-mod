using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 「拾荒客」新游戏专精（开局）。
    ///
    /// 游戏自带的新游戏专精界面（NewGameSpecializationUI）读的是
    /// StartingScenario.GetVisible()；mod 场景必须用 StartType.MODDED，
    /// 通过公开的 StartingScenario.Register 注册，游戏在
    /// NewGameData.HandleInitialItem 里对 MODDED 只做一件事：
    /// 取 startScenarioId 对应的场景并调用它的 onStart。所以初始物资、
    /// 起始信用点都由我们自己的 onStart 负责，和原生各开局的写法一致。
    ///
    /// 两个被动效果不写进存档，只在运行时按「当前存档的开局 id」判定：
    /// - 拾荒次数 +2：ScavHelper.GetMaxScavAttempts 的 Postfix。
    /// - 夜间去垃圾场拾荒后，次日客人不减少：见 ScavengerCustomerPatch。
    /// 没选这个开局（startScenarioId 不是我们的 id）时两条补丁都不生效。
    ///
    /// 初始声望按 20 点基准、乘游戏的难度倍率（Difficulty.Rules.
    /// reputationGainMultiplier）后写入；不放在 onStart：HandleInitialItem 末尾
    /// 会触发 ModHook.OnNewGameStarted，Reputation 在那里把声望归零；由 ModLifecycle 在归零之后显式调用本类写入。
    /// </summary>
    internal static class ScavengerStart
    {
        /// <summary>mod 场景 id，写进 PlayerStore.startScenarioId，随存档保存。</summary>
        internal const string ScenarioId = "local.dumping_ground_map.scavenger";

        /// <summary>垃圾场（AfterhourLocation.DumpingGroundLocation.Id）。</summary>
        internal const string DumpingGroundId = "dumping_ground";

        /// <summary>沿用原生 junker（未开放的拾荒者开局）的图标，避免新增美术资源。</summary>
        private const string IconKey = "icons_junker";

        /// <summary>初始拾荒客声望。</summary>
        internal const int StartReputation = 20;

        /// <summary>每趟拾荒额外次数。</summary>
        internal const int ExtraScavAttempts = 2;

        /// <summary>初始家当从 600 起始资金里扣掉的额度，扣完剩 400。</summary>
        private const int StartCashCost = 200;

        /// <summary>工具箱解锁的格子数，正好放三件工具。</summary>
        private const int ToolboxSlots = 3;

        private const string TitleZh = "拾荒客";
        private const string TitleEn = "Scavenger";

        private static bool hooked;
        private static bool registered;

        /// <summary>当前存档是不是用本专精开的新档。旧档 startScenarioId 为空，返回 false。</summary>
        internal static bool Active
        {
            get
            {
                try
                {
                    PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
                    return store != null && store.startScenarioId == ScenarioId;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static void Hook()
        {
            if (hooked) return;
            try
            {
                RegisterScenario();
                hooked = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[DumpingGroundMap] scavenger start: hook failed: " + ex);
            }
        }

        internal static void Unhook()
        {
            if (!hooked) return;
            try
            {
                StartingScenario.RemoveMod(Core.Id);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: unregister failed: " + ex.Message);
            }
            registered = false;
            hooked = false;
        }

        /// <summary>注册到原生专精列表；重复启用时 Register 会按 id 覆盖，不会出现两行。</summary>
        private static void RegisterScenario()
        {
            if (registered) return;

            StartingScenario scenario = new StartingScenario();
            scenario.id = ScenarioId;
            scenario.startType = NewGameData.StartType.MODDED;
            scenario.source = Core.Id;
            scenario.iconKey = IconKey;
            scenario.titleProvider = delegate { return Copy.Text(TitleZh, TitleEn); };
            scenario.descriptionProvider = delegate
            {
                return Copy.Text(
                    "在导师找到你之前，你是一名拾荒客，虽一贫如洗但口碑可靠。"
                    + "纵然学了新本事，你依旧不忘老本行——盘下一家店铺后，全部家当只有一间简易储藏区、"
                    + "一只儿童背包和一套用旧了的工具，都是当年的旧行头。"
                    + "你清楚哪堆废料底下压着值钱货，也明白在这一行里，名声比什么都重要。",
                    "Before The Mentor found you, you were a scavenger, poor but with a solid reputation. "
                    + "Despite your training, you never forgot your past: your store now holds nothing but "
                    + "a makeshift storage bay, a kid's backpack and a set of worn tools, all of it your old gear. "
                    + "You know which piles hide something worth carrying home, and you know a scavenger's name "
                    + "is earned one haul at a time.");
            };
            scenario.tagProvider = delegate { return Copy.Text("标准开局", "Standard start"); };
            scenario.tagTone = StartingScenario.TagTone.Positive;

            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("拾荒客的装备与工具", "Scavenger's Gear and Tools");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("额外设备（简易储藏区）",
                    "Additional equipment (Makeshift Storage Bay)");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("提升初始声望（拾荒客）",
                    "Increased starting reputation (Scavengers)");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("降低初始信用点", "Decreased starting Credit count");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("增加每趟拾荒次数（+2）", "Increased scavenging attempts per trip (+2)");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("夜间去垃圾场拾荒不影响次日客流",
                    "Night trips to the dump don't cost you customers the next day");
            });

            scenario.onStart = OnStart;
            StartingScenario.Register(scenario);
            registered = true;
            Debug.Log("[DumpingGroundMap] scavenger start: registered " + ScenarioId);
        }

        /// <summary>
        /// 新游戏建店时发放家当。写法与原生各开局一致：
        /// DirectoryMaster.Item(id, true) 造已拥有的物品，GraphUtils.EmporiumTryAdd
        /// 放进后场窗口（放不下时由原版路由到柜台 / 展示柜）。
        /// </summary>
        private static void OnStart()
        {
            try
            {
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (entry == null || entry.invElement == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] scavenger start: no EmporiumEntry, gear skipped");
                    return;
                }
                GameGridInventory inv = entry.invElement;
                Add(inv, "makeshift_storage_bay", "storage_bay");
                Add(inv, "kids_backpack", "satchel");

                GameItem toolbox = Add(inv, "toolbox", null);
                if (toolbox != null)
                {
                    UnlockToolboxSlots(toolbox, ToolboxSlots);
                    MakeShabby(toolbox);
                }

                GiveTool(inv, toolbox, "screwdriver");
                GiveTool(inv, toolbox, "wire_cutter");
                GiveTool(inv, toolbox, "flashlight");
                Add(inv, "bandage_item", null);
                Add(inv, "bandage_item", null);

                PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
                if (store != null)
                {
                    store.playerCash -= StartCashCost;
                    Debug.Log("[DumpingGroundMap] scavenger start: gear given, cash -" + StartCashCost
                        + " left=" + store.playerCash);
                }
                else
                {
                    Debug.LogWarning("[DumpingGroundMap] scavenger start: no PlayerStore, cash untouched");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: gear failed: " + ex);
            }
        }

        /// <summary>造一件物品并放进后场；主 id 不存在时退回备用 id。</summary>
        private static GameItem Add(GameGridInventory inv, string id, string fallbackId)
        {
            GameItem item = Make(id);
            if (item == null && !string.IsNullOrEmpty(fallbackId)) item = Make(fallbackId);
            if (item == null) return null;
            GraphUtils.EmporiumTryAdd(inv, item);
            Debug.Log("[DumpingGroundMap] scavenger start: gave " + item.identifier
                + " placed=" + (item.parentInventory != null));
            return item;
        }

        private static GameItem Make(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id) || !DirectoryMaster.Has<GameItem>(id)) return null;
                return DirectoryMaster.Item(id, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: item '" + id + "' failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>工具箱默认没有可用格子，按原生 Handy 的做法用标签解锁。</summary>
        private static void UnlockToolboxSlots(GameItem toolbox, int slots)
        {
            try
            {
                toolbox.ModifyTag("TOOLBOX_UNLOCKED_SLOTS_INT", delegate(TagState state)
                {
                    state.Enable();
                    state.SetInt(slots);
                }, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: toolbox slots failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 给工具类物品挂原生「破败」属性（ItemFeatureHelper.SetShabby，
        /// 原生 NewGameData.HandleInitialItem 给旧货用的就是这一条）。
        /// 它只加 ItemCondition（CATEGORY_SHABBY / identifier = shabby / modValue = -75），
        /// 让价值掉到基准的 25%，不改任何使用逻辑。
        /// </summary>
        private static void MakeShabby(GameItem item)
        {
            try
            {
                long baseValue = item.unitValue;
                ItemFeatureHelper.SetShabby(item);
                Debug.Log("[DumpingGroundMap] scavenger start: " + item.identifier + " shabby base="
                    + baseValue + " now=" + item.GetCurrentValue(false, false, false, false, 0L));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: shabby failed on "
                    + item.identifier + ": " + ex.Message);
            }
        }

        /// <summary>工具优先塞进工具箱，放不下再走后场（与原生 AddHandyToolbox 相同）。</summary>
        private static void GiveTool(GameGridInventory inv, GameItem toolbox, string id)
        {
            GameItem tool = Make(id);
            if (tool == null) return;
            MakeShabby(tool);
            if (toolbox != null && GraphUtils.TryAcceptAll(toolbox, tool, -1) == 0)
            {
                Debug.Log("[DumpingGroundMap] scavenger start: " + id + " into toolbox");
                return;
            }
            GraphUtils.EmporiumTryAdd(inv, tool);
            Debug.Log("[DumpingGroundMap] scavenger start: " + id + " into back room");
        }

        /// <summary>
        /// 新档把拾荒客声望写成 20 点（在 Reputation.NewGame 归零之后执行）。
        /// 与拾荒收益同一口径：乘游戏的 Difficulty.Rules.reputationGainMultiplier，
        /// 最高难度「赤字连连」是 10 点、最低难度「价格欺诈」是 30 点。
        /// </summary>
        internal static void OnNewGameStarted(string scenarioId)
        {
            if (!string.Equals(scenarioId, ScenarioId, StringComparison.Ordinal)) return;
            try
            {
                string difficultyId;
                float multiplier = Reputation.ReadGainMultiplier(out difficultyId);
                float points = StartReputation * multiplier;
                Reputation.SetPoints(points);
                ScavengerPerkPatch.ResetCache();
                Debug.Log("[DumpingGroundMap] scavenger start: reputation = "
                    + points.ToString("0.##", CultureInfo.InvariantCulture)
                    + " (base " + StartReputation + " x "
                    + multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                    + " difficulty=" + difficultyId + ")");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: reputation failed: " + ex.Message);
            }
        }
    }
}
