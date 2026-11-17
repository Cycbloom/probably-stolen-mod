# Harmony 补丁 → ModHook 迁移盘点

- 盘点日期：2026-11-17
- 方式：静态读取仓库源码 + 用 DseCecil 反编译 `Probably Stolen_Data/Managed/Assembly-CSharp.dll`（只读，未运行游戏、未构建）
- 范围：`src/` 下全部 `[HarmonyPatch]`（44 处）与 `NativeLootEvents` 里的反射触发
- 结论先说：**44 处里只有 1 处能干净地换成原生钩子**，43 处必须留；另有 1 处（`NativeLootEvents`）属于"得改设计"，代价很高，本轮不建议动。原指南"有钩子就用钩子"在这里的自然结论是：ModHook 覆盖的是**流程节点**，而本模组大量工作是**改写函数内部返回值与 UI 构建**，两者不重叠。

---

## 一、游戏原生 `ModHook` 实际清单（反编译所得）

`ModHook` 是单例，41 个静态事件，41 个 `FireOn*` 方法。以下是从 IL 里查到的**全部** 47 个触发点（同一钩子可能在多个位置触发）：

| 钩子 | 触发位置 | 参数 |
|---|---|---|
| `OnModItemDirectoryInit` | `ModItemDirectory.InitDirectory` | `ModItemDirectory` |
| `OnGenerateCustomerVeryEarly/Early/Normal/Late/VeryLate` | `StoreClientManager.GenerateClient` | `StoreClientManager` |
| `OnGameLoadedInit/Early/Normal/Late` | `PlayerStore.LoadGame` | 无 |
| `OnDifficultyApplied` | `PlayerStore.LoadGame`、`PlayerStore.StartNewGame` | `string difficultyId` |
| `OnShutterOpenedEarly/Late` | `StoreShutterButton.OnShutterDoorOpenMorning` | 无 |
| `OnShutterClosedEarly/Late` | `StoreShutterButton.EndAndClose` | 无 |
| `OnGoingSleepEarly/Late` | `PlayerStore.EndNight` | 无 |
| `OnHandlingNightlyServicesEarly/Late` | `PlayerStore.EndNight` | 无 |
| `OnWakingUpEarly/Late` | `StartOfDayUIManager.OpenUI` | 无 |
| `OnLeavingStoreEarly/Late` | `EmporiumEntry.OnLeaveStore` | 无 |
| `OnReturningStoreEarly/Late` | `EmporiumEntry.OnArriveStore` | 无 |
| `OnPlaceInventorInventoryItemEarly/Late` | `StoreClientList.PlaceInventorInventory`、`PlaceInventorSpecialStock` | `List<...>` |
| `OnCreateTooltipEarly/Late` | `ModInternal.CreateTooltipEarly/Late` | `RichTextBuilder, GameItem` |
| `OnCreateDevTooltipEarly/Late` | `ModInternal.CreateDevTooltipEarly/Late` | 同上 |
| `OnModAssetsLoaded` | `LoadSoundsCoroutine.MoveNext` | `LoadedMod` |
| `OnModConfigApplied` | `ModConfigWindow.Apply` | `string modId` |
| `OnLootTablesLoaded` | `LootRegistry.OnVanillaLoaded` | 无 |
| `OnLootRolled` | `LootRegistry.RollInternal` | `LootRollContext` |
| `OnPaperOpened` | `PaperBookRegistry.FireOpened` | `string bookId, PaperUI` |
| `OnNetworkUpgradeUnlocked` | `NetworkUpgrade.Unlock` | `string upgradeId` |
| `OnNewGameStarted` | `NewGameData.HandleInitialItem` | `string scenarioId` |
| `OnAfterhourArriveEarly/Late` | `MapUIManager.Travel` 的协程 | `string locationId` |
| `OnAfterhourLeaveEarly/Late` | `MapUIManager.ReturnNow` 的协程 | `string locationId` |
| `OnAfterhourEventStarted/Ended` | `AfterhourEvents.Start/End` | `locationId, eventId` |

**未查到的钩子**（明确不存在，不是"没看"）：掉落生成（`ScavHelper.SpawnDumpingGroundLoot`）、拾荒次数、伤口概率与掷骰、顾客数量、日开始（`StoreStation.StartDay` / `StoreEventManager.OnDayStart`）、地图与店铺 UI 构建、确认弹窗、模组设置面板构建、声望面板。这些位置没有任何 `FireOn*` 调用。

关键时序（对迁移判断有决定性影响）：

- `OnLootRolled` 在 `LootRegistry.RollInternal` **函数体内**触发；本模组对 `RollInternal` 的 `[HarmonyPriority(Priority.Last)]` 后置补丁在它**之后**执行。
- `OnGenerateCustomerVeryLate` 是 `GenerateClient` 里的最后一个钩子；本模组的 `ScavengerPerkPatch` 是该方法的后置补丁，**排在所有顾客钩子之后**。
- `OnModConfigApplied` 在 `ModConfigWindow.Apply` 体内触发；本模组对该方法的 `Postfix` 在其后。

---

## 二、逐条分类（44 处 + 1 处反射）

图例：**能换** = 有等价钩子；**该留** = 无钩子或语义不等价；**得改设计** = 机制上没有钩子可承接，要换实现思路。

| # | 位置 | 目标方法 | 形态 | 分类 | 依据 / 对应钩子 |
|---:|---|---|---|---|---|
| 1 | `Compatibility/ScavengingExpansionBridge.cs:150` | `LootRegistry.GetTableEntries` | Postfix 改返回列表 | 该留 | 按查询动态决定候选池；`OnLootTablesLoaded` 只在原版加载时触发一次 |
| 2 | `Compatibility/ScavengingExpansionBridge.cs:160` | `LootRegistry.RollInternal` | `Priority.Last` Postfix | 该留 | 需要"晚于所有 `OnLootRolled` 订阅者"的确定顺序，钩子无优先级 |
| 3 | `Patches/DumpingGroundLootPatch.cs:47` | `ScavHelper.SpawnDumpingGroundLoot` | Prefix 跳过原逻辑 | 该留 | 整段替换掉落生成；无钩子，且需要 `__state` 与 DiscoveryLoot 协作 |
| 4 | `Patches/GroundPatches.cs:8` | `EmporiumEntry.SetupAfterhourInv` | Postfix | 该留 | 地面窗口注入；无 UI 构建钩子 |
| 5 | `Patches/GroundPatches.cs:29` | `EmporiumEntry.Validate` | Postfix | 该留 | 同上 |
| 6 | `Patches/GroundPatches.cs:41` | `EmporiumEntry.ShowAfterhourInv` | Postfix | 该留 | 同上 |
| 7 | `Patches/GroundPatches.cs:53` | `EmporiumEntry.HideAfterhourInv` | Postfix | 该留 | 同上 |
| 8 | `Patches/GroundPatches.cs:62` | `ScavHelper.ScavengeDumpingGrounds` | Prefix（作用域） | 该留 | 需要在原流程前后夹住状态；无钩子 |
| 9 | `Patches/GroundPatches.cs:78` | `GraphUtils.TryAcceptAll` | Postfix 改 `__result` | 该留 | 容量判定内部结果；无钩子 |
| 10 | `Patches/HelperPatches.cs:11` | `MapUIManager.CheckItemLeftOnGround` | Prefix | 该留 | 离场提示判定；无钩子 |
| 11 | `Patches/HelperPatches.cs:27` | `GenericConfirmationUIManager.OpenPanel` | Postfix | 该留 | 原生确认弹窗改造；无钩子 |
| 12 | `Patches/HelperPatches.cs:37` | `GenericConfirmationUIManager.ClosePanel` | Postfix | 该留 | 同上 |
| 13 | `Patches/HelperPatches.cs:47` | `EmporiumEntry.SetupAfterhourInv` | Postfix | 该留 | 帮手状态复位；无钩子 |
| 14 | `Patches/MapPatches.cs:12` | `MapUIManager.ScavengeButtonClick` | Prefix 返回 false | 该留 | 接管按钮流程；必须阻止原逻辑 |
| 15 | `Patches/MapPatches.cs:21` | `DumpingGroundLocation.OnScavenge` | Prefix 返回 false | 该留 | 打开本模组选区 UI；必须阻止原逻辑 |
| 16 | `Patches/MapPatches.cs:41` | `MapUIManager.Travel` | Prefix 返回 false | 该留 | 必须在出发前改写目的地；`OnAfterhourArrive*` 在到达后才触发 |
| 17 | `Patches/MapPatches.cs:55` | `DumpingGroundLocation.Leave` | Postfix | 该留 | 弱候选 `OnAfterhourLeave*`，但后者在 `ReturnNow` 协程触发、且对其他地点也触发，语义不等价 |
| 18 | `Patches/MapPatches.cs:61` | `MapUIManager.CloseLocationPanel` | Prefix | 该留 | 关闭面板时收尾；无钩子 |
| 19 | `Patches/MapPatches.cs:67` | `MapUIManager.Update` | Postfix | 该留 | 每帧悬停反馈；无钩子 |
| 20 | `Patches/MapPatches.cs:78` | `MapUIManager.ShowStage` | Postfix | 该留 | 切换区域背景；无钩子 |
| 21 | `Patches/MapPatches.cs:85` | `MapUIManager.HideStage` | Postfix | 该留 | 恢复原背景；无钩子 |
| 22 | `Patches/RegionalEventPatches.cs:5` | `StoreStation.StartDay` | Postfix | 该留 | 日开始数据结算；`OnWakingUpEarly` 在 UI 打开时才触发，晚于数据层 |
| 23 | `Patches/RegionalEventPatches.cs:19` | `ScavHelper.GetMinorWoundChance` | Postfix 改 `__result` | 该留 | 改概率数值；无钩子 |
| 24 | `Patches/RegionalEventPatches.cs:30` | `ScavHelper.GetMajorWoundChance` | Postfix 改 `__result` | 该留 | 同上 |
| 25 | `Patches/ScavengeReputationPatch.cs:17` | `ScavHelper.GetRandomScavengedItem(GameItem&)` | Postfix | 该留 | 声望结算要看**最终**物品（含第三方改写）；`OnLootRolled` 更早，拿不到最终结果 |
| 26 | `Patches/ScavengerOutingPatches.cs:6` | `LocHelper.GetLocalizedUI(string, object[])` | Prefix 改 `__result` | 该留 | 改服务面板文案；`OnCreateTooltip*` 只给 `GameItem` 提示框，不是同一处 |
| 27 | `Patches/ScavengerOutingPatches.cs:23` | `EndOfDayUIManager.InitUI` | Prefix | 该留 | 日终面板初始化；无钩子 |
| 28 | `Patches/ScavengerOutingPatches.cs:29` | `PlayerStore.UpdateServiceCost` | Postfix | 该留 | 费用同步；无钩子 |
| 29 | `Patches/ScavengerOutingPatches.cs:35` | `ScavHelper.GetMaxScavAttempts` | Postfix 改 `__result` | 该留 | 加次数；无钩子 |
| 30 | `Patches/ScavengerOutingPatches.cs:44` | `ScavHelper.GetMinorWoundChance` | Postfix | 该留 | 同行服务免伤；无钩子 |
| 31 | `Patches/ScavengerOutingPatches.cs:50` | `ScavHelper.GetMajorWoundChance` | Postfix | 该留 | 同上 |
| 32 | `Patches/ScavengerOutingPatches.cs:56` | `ScavHelper.RollMinorWound` | `Priority.Last` | 该留 | 需要晚于第三方改掷骰；钩子无优先级 |
| 33 | `Patches/ScavengerOutingPatches.cs:68` | `ScavHelper.RollMajorWound` | `Priority.Last` | 该留 | 同上 |
| 34 | `Patches/ScavengerPerkPatches.cs:26` | `StoreClientManager.GenerateClient` | Postfix | **能换 → `OnGenerateCustomerVeryLate`** | 见下节论证 |
| 35 | `Patches/ScavengerPerkPatches.cs:118` | `NegociationUIManager.RefreshClientHeader(StoreClient)` | Postfix | 该留 | 给客户解释行追加文本；无钩子 |
| 36 | `Patches/ScavengerStartPatches.cs:10` | `ScavHelper.GetMaxScavAttempts` | Postfix | 该留 | 与 #29 叠加，改返回值；无钩子 |
| 37 | `Patches/ScavengerStartPatches.cs:31` | `StoreClientManager.GetNormalCustomerCount` | Prefix/Postfix/Finalizer | 该留 | 临时改写 `healthData.wentToAfterhoursLastNight` 并在返回后复原，钩子做不到"夹住" |
| 38 | `Patches/ScavengerStartPatches.cs:68` | `PlayerStore.HandleAfterhourDebuff` | Prefix 返回 false | 该留 | 需要阻断原生夜间报告；`OnHandlingNightlyServices*` 是通知，不能取消 |
| 39 | `Patches/SettingsPatches.cs:12` | `ModConfigWindow.BuildEntry` | Prefix + Postfix | 该留 | 构建设置项；无钩子 |
| 40 | `Patches/SettingsPatches.cs:31` | `CustomUIElement.SetFixedWidth` | Postfix | 该留 | 同一行追加数值标签；无钩子 |
| 41 | `Patches/SettingsPatches.cs:40` | `ModConfigWindow.ResetWidgets` | Prefix + Postfix | 该留 | 重置前后的状态迁移；无钩子 |
| 42 | `Patches/SettingsPatches.cs:54` | `ModConfigWindow.Apply` | Prefix + Postfix | 该留 | Prefix 必须"应用前"提交拖拽值；只有 Postfix 有弱候选 `OnModConfigApplied`，拆开收益低且时机会提前 |
| 43 | `Patches/StashNewsPatch.cs:10` | `StoreEventManager.OnDayStart` | Prefix | 该留 | 日开始注入便签；无钩子 |
| 44 | `UI/ReputationBoardPatch.cs:23` | `ReputationUIManager.OpenUI` | Postfix | 该留 | 追加一行；`OnPaperOpened` 是纸质手册，不是这里 |
| — | `Compatibility/NativeLootEvents.cs:10-33` | `ModHook.FireOnLootRolled`（反射 invoke） | 手动触发 | **得改设计** | 见下节 |

统计：能换 1、该留 43、得改设计 1。

### #34 为什么算"能换"

`ScavengerPerkPatch` 在 `GenerateClient` 返回后追加一个拾荒客客户，并用 `(runID, 天)` 内存标记防同一天重复加。`OnGenerateCustomerVeryLate` 是该方法里最后一个钩子，参数就是 `StoreClientManager`，位置在后置补丁之前一点点。

替换后的语义差（**必须实测确认**）：

- 执行位置从"方法返回后"提前到"方法返回前最后一次钩子"。若 `GenerateClient` 在 VeryLate 之后还有对客户列表的读取（例如排序、计数），我们的追加会晚于它、而不是完全在方法之外。
- 优点：钩子的 `SafeInvoke` 自带 try/catch，异常不会破坏顾客生成；当前 Postfix 抛异常会中断原方法返回。
- 不变项：一次性标记（`lastStore`/`lastDay`）与 `ResetCache()` 必须照旧保留；`OnGenerateCustomerVeryLate` 可能被其他模组订阅，追加顺序取决于订阅先后。

### `NativeLootEvents` 为什么归"得改设计"

本模组自己实现了掉落解析（`LootRollResolver`），不经过 `LootRegistry.RollInternal`，所以游戏不会替我们发 `OnLootRolled`；目前用 `AccessTools.Method(typeof(ModHook), "FireOnLootRolled")` 反射调用私有静态方法补发。

- 现状风险：`FireOnLootRolled` 是私有成员，游戏改名/改签名就会在运行时抛 `MissingMethodException`（代码里已有显式抛错），但不会静默失效。
- 扫描器角度：`typeof` + 字面量名，不产出能力，2026-12-10 的收紧也不影响。
- 改设计的方向：让自定义掉落回退到 `LootRegistry.RollInternal`，由游戏自己发钩子。代价是所有自定义掉落路径（区域表、箱卡、大小奖）都要改走原生掷取，收益与当前分层结构冲突，回归面覆盖全部掉落数值。**不建议在钩子迁移里做**。

---

## 三、迁移批次建议

### 批 1：`ScavengerPerkPatch` → `OnGenerateCustomerVeryLate`（唯一等价项，风险低）

- 涉及：#34。
- 验证点：
  1. 同店同日只追加一次（沿用 `lastStore`/`lastDay`），读档后 `ResetCache` 生效、不重复；
  2. 追加发生在 `GenerateClient` 返回前，顾客总数与其他模组的加减不打架；
  3. 与「更好的拾荒」等同订阅 VeryLate 的模组各触发一次；
  4. 抛异常时只记日志，不影响顾客生成。
- 影响面：只影响 20 声望后每日额外拾荒客，可用现有 1,104 项兼容检查 + 控制台日志独立验证。

### 批 2：`ModConfigApplyPatch` 后置部分（需先验证重入，风险中）

- 涉及：#42 的 Postfix。
- 前置验证：`NativeSettings.RefreshIfOpen()` 在 `Apply` 体内执行是否会重入（它会重建控件，而 `Apply` 可能仍在遍历条目）。**验证不通过就不做**。
- 若通过：Prefix 保留为补丁，Postfix 改为订阅 `OnModConfigApplied`，并核对 modId 过滤。
- 影响面：设置面板刷新时机，玩家可见但不改数值。

### 批 3：`NativeLootEvents` 的反射触发（设计级，风险高，暂缓）

- 涉及：`NativeLootEvents.Notify`。
- 若要做：自定义掉落改走 `LootRegistry.RollInternal`，删除补发逻辑。代价是掉落链路整体重构 + 全量数值回归。
- 在此之前保持现状，并把"依赖私有 `FireOnLootRolled`"写进 `docs/ARCHITECTURE.md` 的已知耦合。

### 不建议迁移：其余 43 处

理由集中在三点：改返回值/概率（14 处）、接管或阻断原生流程（8 处）、改 UI 构建与布局（13 处）、需要确定的执行顺序（3 处）、需要夹住方法内状态（5 处）。这几类在 `ModHook` 里**没有对应触发点**（见第一节"未查到的钩子"），迁移只会变成"用钩子做不了、再退回 Harmony"。

---

## 四、迁移时必须守住的兼容契约

1. **拾荒拓展桥接顺序**：#2 的 `Priority.Last` 与 #1 的返回列表改写，保证第三方技能抽表、掷取看到的是本模组补全后的池子。换成钩子会失去"晚于所有 `OnLootRolled` 订阅者"的确定性。
2. **「更好的拾荒」一次性状态**：桥接里 `probed` / `nextProbe` 与 30 秒重试窗口只在 `Resolve` 内维护，迁移任何补丁都不得把该状态挪到会在主菜单执行的路径。
3. **声望结算顺序**：#25 必须看到 `GetRandomScavengedItem` 的**最终**产出（第三方可能替换 ID）。若提前到 `OnLootRolled`，结算会按替换前的物品计价。
4. **事件只发一次**：`OnLootRolled` 每个解析出的物品恰好发一次（`NativeLootEvents` 补发一次，游戏原生路径不经过这里）。任何"顺手也调一下 `RollInternal`"的改动都会导致双发，第三方会重复计数。

---

## 五、复现方式

```powershell
# 仅读取，不改仓库
$managed='...\Probably Stolen_Data\Managed'
[void][Reflection.Assembly]::LoadFrom("$managed\DseCecil.dll")
$asm=[DseCecil.AssemblyDefinition]::ReadAssembly("$managed\Assembly-CSharp.dll")
# 1) ModHook 的事件与方法
$asm.MainModule.Types | ? Name -eq 'ModHook' | % { $_.Fields; $_.Methods }
# 2) 钩子触发点：遍历所有方法 IL，找 DeclaringType.Name -eq 'ModHook' 且 Name -like 'Fire*' 的调用
```
