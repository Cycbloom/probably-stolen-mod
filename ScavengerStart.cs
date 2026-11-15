using System;
using System.Collections.Generic;
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
    /// 初始声望 20 点不放在 onStart：HandleInitialItem 末尾会触发
    /// ModHook.OnNewGameStarted，Reputation 在那里把声望归零；本类在
    /// Reputation.Hook() 之后订阅同一个事件，因此排在归零之后写入 20 点。
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

        /// <summary>初始家当从 600 起始资金里扣掉的额度，扣完剩 200。</summary>
        private const int StartCashCost = 400;

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
                ModHook.OnNewGameStarted += OnNewGameStarted;
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
            ModHook.OnNewGameStarted -= OnNewGameStarted;
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
                    "你在垃圾场里长大，知道哪堆废料底下压着能换钱的东西。开局就带着一身拾荒家当，名声也已经有点分量。",
                    "You grew up in the dump and know which pile hides something worth carrying home. "
                    + "You start with a scavenger's kit, and people already know your name.");
            };
            scenario.tagProvider = delegate { return Copy.Text("标准开局", "Standard start"); };
            scenario.tagTone = StartingScenario.TagTone.Positive;

            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("每趟拾荒次数 +2", "Two extra scavenging attempts per trip");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text(
                    "夜里去垃圾场拾荒后，第二天客人不会减少；去别处过夜仍然会少一名客人",
                    "A night scavenging the dump won't cost you a customer the next day; "
                    + "nights spent anywhere else still do");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text(
                    "初始物资：简易储藏区、基础背包、工具箱（螺丝刀 / 剪线钳 / 手电筒）、基础绷带 ×2",
                    "Starting gear: makeshift storage bay, basic backpack, toolbox with a screwdriver, "
                    + "wire cutters and a flashlight, plus 2 basic bandages");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("初始拾荒客声望 " + StartReputation + " 点",
                    "Start with " + StartReputation + " Scavenger reputation");
            });
            scenario.startWithProviders.Add(delegate
            {
                return Copy.Text("初始信用点 200（家当已从起始资金里扣除）",
                    "200 starting credits (the kit is deducted from your starting cash)");
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
                Add(inv, "backpack_medium", "satchel");

                GameItem toolbox = Add(inv, "toolbox", null);
                if (toolbox != null) UnlockToolboxSlots(toolbox, ToolboxSlots);

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

        /// <summary>工具优先塞进工具箱，放不下再走后场（与原生 AddHandyToolbox 相同）。</summary>
        private static void GiveTool(GameGridInventory inv, GameItem toolbox, string id)
        {
            GameItem tool = Make(id);
            if (tool == null) return;
            if (toolbox != null && GraphUtils.TryAcceptAll(toolbox, tool, -1) == 0)
            {
                Debug.Log("[DumpingGroundMap] scavenger start: " + id + " into toolbox");
                return;
            }
            GraphUtils.EmporiumTryAdd(inv, tool);
            Debug.Log("[DumpingGroundMap] scavenger start: " + id + " into back room");
        }

        /// <summary>新档把拾荒客声望写成 20 点（在 Reputation.NewGame 归零之后执行）。</summary>
        private static void OnNewGameStarted(string scenarioId)
        {
            if (!string.Equals(scenarioId, ScenarioId, StringComparison.Ordinal)) return;
            try
            {
                Reputation.SetPoints(StartReputation);
                ScavengerPerkPatch.ResetCache();
                Debug.Log("[DumpingGroundMap] scavenger start: reputation = " + StartReputation);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger start: reputation failed: " + ex.Message);
            }
        }
    }

    /// <summary>拾荒次数 +2。只在当前存档用「拾荒客」开局时生效。</summary>
    [HarmonyPatch(typeof(ScavHelper), "GetMaxScavAttempts")]
    internal static class ScavengerAttemptPatch
    {
        private static void Postfix(ref int __result)
        {
            if (!Core.Enabled || __result <= 0) return;
            if (!ScavengerStart.Active) return;
            __result += ScavengerStart.ExtraScavAttempts;
        }
    }

    /// <summary>
    /// 短眠者的劣化版：昨夜去的是垃圾场时，把「夜间外出」的客人减 1 抵掉。
    ///
    /// 原生 GetNormalCustomerCount 的扣减条件是
    /// healthData.wentToAfterhoursLastNight &amp;&amp; !StartingPerk.IsShortSleeper()，
    /// 减完还会 Clamp(0, 6)。直接在返回值上加 1 会在「原生已夹到 0」时多出一名客人，
    /// 所以改成前缀里临时把 wentToAfterhoursLastNight 置 false，让原方法按
    /// 「没外出」的路径完整算一遍（含 Clamp），返回后再恢复，语义与短眠者一致。
    /// </summary>
    [HarmonyPatch(typeof(StoreClientManager), "GetNormalCustomerCount")]
    internal static class ScavengerCustomerPatch
    {
        private static bool suppressed;

        private static void Prefix()
        {
            suppressed = false;
            if (!Core.Enabled || !ScavengerStart.Active) return;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            if (store == null || store.healthData == null) return;
            if (!store.healthData.wentToAfterhoursLastNight) return;
            // 原版短眠者本来就不扣，不要再加回来。
            if (StartingPerk.IsShortSleeper()) return;
            MapUIManager map = MapUIManager.Instance;
            if (map == null || map.LastVisitedTonightId != ScavengerStart.DumpingGroundId) return;

            store.healthData.wentToAfterhoursLastNight = false;
            suppressed = true;
        }

        private static void Postfix()
        {
            Restore();
        }

        private static Exception Finalizer(Exception __exception)
        {
            Restore();
            return __exception;
        }

        private static void Restore()
        {
            if (!suppressed) return;
            suppressed = false;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            if (store != null && store.healthData != null)
            {
                store.healthData.wentToAfterhoursLastNight = true;
            }
        }
    }
}
