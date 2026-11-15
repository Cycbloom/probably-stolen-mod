# 垃圾场拾荒地图 · 0.3.67

夜间外出 → 城市地图选「垃圾场」→ 点「前往」时先打开本 mod 的作业地图，选一块地标、点「进入该区域」，才真正进入垃圾场；进入后点「拾荒」直接按该区的物品表拾取，本次停留内不再询问。离开垃圾场（回家、去别的地点、重新进入）后再来，需要重新选一次。取消或返回键关闭地图。选择只在本次停留内有效，不写入存档。

地图共 5 个地标，分两档：

| 地标 | 档位 | 声望门槛 |
|---|---|---:|
| 入口空地 | 低级 | 0 |
| 生活废料带 | 低级 | 0 |
| 拆解堆 | 高级 | 40 |
| 医疗废弃点 | 高级 | 40 |
| 废武器堆 | 高级 | 40 |

## 声望与解锁

每趟拾荒结束后，把这一趟捡到的所有物品按当前金币价值（`GameItem.GetCurrentValue`）相加，**每 100 点价值记 1 点声望**。不足 100 的部分不丢，会留在存档里继续累计。

声望必须按存档隔离，所以不写进 `ModConfig`，而是用游戏自带的 mod 数据接口写进存档本体的 `PlayerStore.modData`（`ModHelper.GetModDataInt / SetModDataInt(Core.Id, "rep_total_value", 0)`），随存档一起保存。`OnGameLoadedLate` 读取、`OnNewGameStarted` 归零；`ModConfig` 只存掉落与调试设置。

**落盘时机是结束一天时的存档**：中途强退或崩溃，当晚声望会丢，与当晚捡到的其它收益一致。声望只存 `PlayerStore.modData`，模组不读写任何文件。门槛数字集中在一张表里：`UnlockCost.table`（三个高级区统一 40，0.3.24 起），改门槛只改这一处。柱子上另有五个奖励点 20 / 40 / 60 / 80 / 100（0.3.46 起）：20 自 0.3.27 起生效（每天开张时多一名拾荒客客人）；40 就是高级区门槛；80 已生效（贵重品出现率解锁 + 补给箱 / 钥匙卡）；60 已在 0.3.52 生效（爆大奖），100 只落了门槛与档名。

未解锁的地标在图上压暗、编号后直接标出所需声望；点它会提示「需要声望 40 才能进入「拆解堆」；当前声望 12」。地图下方常驻一行当前声望与三个门槛。

### 声望在评价箱中的展示

商店场景里的评价箱（`FeedbackBox`）点开就是游戏原生的派系声望面板（`ReputationUIManager`）。本模组在面板每次打开时，于原生派系行之后追加一行「拾荒客 / Scavenger」：

| 位置 | 内容 |
|---|---|
| 标题 | 拾荒客（英文环境 Scavenger），沿用原生标题的配色与字号 |
| 状态 | 等级名：生手 / 拾荒老手 / 熟门熟路 / 眼力毒辣 / 识货行家 / 自己人（Rookie / Scavenger Veteran / Knows the Back Ways / Keen Eye / Connoisseur / One of Us） |
| 数字 | 0–200 刻度上的两位小数：`x.xx/200`（显示值 = 累计价值 / 100，`F2`） |
| 绿条 | 不自己算比例，直接用原生 `Init → UpdateBar → GetBarHeight(v) = 65.5 × v / 100` |
| 图标 | 柱上 20 / 40 / 60 / 80 / 100 五个奖励点。20–80 用笑脸一族并逐级放大（相对预制体原始大小）：20 `spriteSmile` 0.65 倍、40 `spriteSmile` 0.78 倍、60 `spriteHappy` 0.90 倍、80 `spriteHappy` 1.00 倍；100 用 `spriteStar`（1.00 倍）作为最高档标记。未解锁 alpha 0.35。20、40、80 已生效，60 与 100 仍是档名占位；五个图标（含未解锁的）都能悬停，显示自带的中英双语说明 |

等级分界：0–20 生手、20–40 拾荒老手、40–60 熟门熟路、60–80 眼力毒辣、80–100 识货行家、100+ 自己人。

实现上是在 `ReputationUIManager.OpenUI` 的 Postfix 里 `Instantiate(reputationPrefab, layout.transform, false)`，再合成一个只存在于内存里的 `StoreReputation`（`perks` 填 20 / 40 / 60 / 80 / 100 五个奖励点）交给原生 `ReputationElement.Init` 排版，随后覆盖状态文本与数字文本、按档位覆盖图标 sprite 与透明度，并给每个图标挂上本 mod 的悬停说明。**不往 `PlayerStore.storeReputations` 里塞条目，不写存档，不进 ES3**，面板关闭后这一行随原生清空一起销毁。

- 原生 `layout` 若有布局组（`LayoutGroup`），追加后调用 `LayoutRebuilder.ForceRebuildLayoutImmediate`；没有布局组时按上一行的高度和位置手动往下排。这一条需要在游戏里目视确认，代码里两条路径都保留了。
- 类型全部编译期绑定：`[HarmonyPatch]` + `AccessTools.Method(typeof(ReputationUIManager), "OpenUI")`，其余字段直接访问公开成员；不使用 `Type.GetType`、`TypeByName`、`Activator` 或程序集扫描，清单能力保持为空。游戏更新改动这些成员时会在编译期暴露，而不是运行期静默失败。
- **不做跨 mod 兼容判断**：唯一的短路是「本次打开已建过行」（0.3.26 起按 `openCount` 序号判断，不查找子物体；0.3.25 及以前用 `layout` 下的 `DGM_ReputationRow`，会因原生 `Destroy` 延迟而误跳过第二次打开）。不检查災變日 / ReputationInterfaceRemake 是否在运行，也不判断面板是否被别的 mod 改过；其它 mod 的运行状态只写进诊断日志，不参与任何分支。0.3.22 起删除了此前的 `LighthouseSheet` / `ForeignRunning` 判断。
- **0.3.25 悬停说明**：原生 tooltip 的取文链路是 `PerkElement.OnPointerEnter → ReputationUIManager.UpdateToolTipContent(perk)`，而 `UpdateToolTipContent` 只会用 `StoreReputationPerk` 的本地化键（`GetLocalizedPerkName/Description/FlavorText`）去 `SetText`，并且**不负责显示 tooltip**（原生 `OnPointerExit` 是空实现，tooltip 的显隐只看 GameObject 是否 active，位置由原生 `UpdateTooltipPosition` 每帧跟随鼠标）。我们没有 `rep_perk_<perkId>_*` 词条，所以把五个图标的 `PerkElement` 关掉（`perk.enabled = false`，Unity 不再向它派发指针事件），在同一个 GameObject 上挂本 mod 的 `DgmPerkTooltip`（`IPointerEnterHandler` / `IPointerExitHandler`）：进入时填 `perkTitle`（名称 + `[已激活/未激活]` + `20/200`）、清空 `perkStatus` / `perkProgress`、填 `perkdescription` 与 `perkFlavorText`，再 `reputationTooltip.SetActive(true)`；离开或组件禁用时 `SetActive(false)`。全部是公开字段访问，不新增资源文件、不反射、不读写文件。
- **0.3.25 图标归属**：柱上图标不再按子物体索引硬映射（0.3.24 日志出现过 `perks total=4`，第四个来自行预制体自带、会让 20/40/60 错位）。现在在 `Init` 之前记下已有的 `PerkElement`，`Init` 之后把它们移除，剩下的按 `anchoredPosition.y`（= `GetBarHeight(阈值)`）排序，依次对应 20 / 40 / 60 / 80 / 100。
- **0.3.26 每次打开都重建**：0.3.25 用 `layout.transform.Find(RowName)` 防重复，原生 `OpenUI` 却是用 `Destroy` 清空 layout（延迟到帧末才真正移除），第二次打开时旧行还挂在父子树里、查找必定命中，于是整行被跳过。现在改为「打开序号」判重：`OpenUI` 的 Prefix 把 `openCount` 加一，Postfix 只在 `builtAtOpen != openCount`（或跨帧）时建行，建完记录序号与帧号；不再查找子物体。日志会打 `open=#N frame=F children=…`，连续开关两次会看到 `open=#1` / `open=#2` 各一条 `added`。
- **0.3.26 两位小数**：数字改为 `Reputation.ExactPoints`（累计价值 / 100f）的 `F2` 输出，如 `12.34/200`；柱高与图标解锁仍用整数点数（`totalValue / 100` 向下取整），逻辑不变。
- **0.3.29 声望飘字开关**：`ModConfig` 新增布尔项 `reputation_pop`（分组「声望飘字 / Reputation Pop」，默认开）。标题在对方 mod 未运行时自动变成「声望飘字（需安装 Reputation Pop，当前未安装）」，说明文字写明依赖与关闭后的行为。`ReputationPopBridge.Show()` 先看开关：关 → 直接返回并只记一条 `reputation pop: disabled by setting`；开但对方不在 → 只记一条 `reputation pop: unavailable (mod not running)`；开且对方在运行 → 照常记录 delta。分支只看 `Tuning.ReputationPopEnabled` 与 `IsModRunning`，不改存档。
- **0.3.30 调试项**：设置里新增分组「调试 / Debug」。`debug_enabled`（「启用调试项」，默认关）打开后，`debug_points`（「设置声望点数」，0–200，0.5 步进）才显示并生效；改动立即把 `rep_total_value` 写成 `round(points × 100)`，同步内存态并打日志 `debug: reputation set to 19.5 points (value=1950)`。关闭时代码不写存档（首次改动只记一条 `debug: reputation setter ignored (debug off)`），标签和滑条用 `CustomUIElement.SetVisible` 隐藏。原生设置界面没有条件可见能力，隐藏只作用于这两个元素，行位仍占空。
- **0.3.28 读档必须与存档一致**：删掉了 `Reload()` 里「同一个 runID 就跳过读取」的优化。同一局内读回较早的自动存档（如 Shutter Autosave 的快照）时 runID 不变，旧逻辑会保留内存里的较新数值，表现为声望不回退。现在每次 `OnGameLoadedLate` 都无条件 `StoredValue()` 覆盖 `totalValue`，日志带新旧值 `… value=<存档值> points=<点数> previous=<内存旧值>`；同族的「今天已加过拾荒客客人」标记（`ScavengerPerkPatch.lastStore/lastDay`）也在读档与新档时清空，避免读档后当天误跳过或重复加。
- **0.3.25 累加日志**：每趟拾荒输出一条 `[DumpingGroundMap] scavenge: items=<件数> value=<本次价值> total=<累计价值> points=<当前点数>`；补丁绑定失败会打 `scavenge: patch target NOT FOUND`，找不到 `PlayerStore` 会打 `reputation add skipped`，物品价值全为 0 会打 `items found but total value is 0`。
- **0.3.24 容器高度检查**：诊断日志会打出 `bar=<当前柱高>px barParent=<父容器高>px expectedAt200=131.0px`；父容器不足 131px 时会额外打 `bar container is only …px, 0–200 scale needs 131.0px` 的警告，此时 200 点会画出可视区，需要回报日志再定方案。
- **0.3.21 诊断日志**：补丁关键节点各打一条 `Debug.Log`，前缀 `[DumpingGroundMap] reputation row`，包括：目标方法是否取到、Postfix 首次进入（附带 `disaster_day` / `cirno9.reputation_interface_remake` 的运行状态）、`layout` / `prefab` 是否为空、是否因重复行跳过、文本覆盖结果（状态/数字/条比例/标题）、布局组的类型与子物体数、最终 `anchoredPosition` 与 `sizeDelta`。异常走 `Debug.LogError` 打完整堆栈。只写游戏日志，不写文件。

### 20 点奖励：每天多一名拾荒客客人（0.3.27）

客人的生成链路是 `PlayerStore.OnShutterOpened()`（每天开张调用一次）→ `StoreClientManager.GenerateClient()`，后者依次跑 `HandleNormalClient`（数量来自 `GetNormalCustomerCount`）、`HandlePerkClient`、`HandleServiceClient` 等。注入点选在 `GenerateClient()` 的 Postfix（全程序集只有 `OnShutterOpened` 一处调用它，一天恰好一次）：

- `Reputation.Points >= ScavengerPerkPatch.Threshold`（常量 20）时，`StoreClientList.CreateScavGeneral()` 造一名原生拾荒客客人，`ModHelper.SetClientSource(client, Core.Id)` 打上本 mod 来源标记，再 `AddClient(client)` 加进去。
- 用 `AddClient` 而非 `TryAddClient`：后者会先过 `IsClientOnCooldown`，`scavGeneral` 出现过一次后 `clientTracker` 就长期非 0，会把每日奖励永久拦掉；`AddClient` 与原生 `HandlePerkClient` 的做法一致。
- 同日防重：记下 `runID + StoreStation.GetDayCounter()`，同一天再来一次（理论上不会）会打 `skip, already added` 并跳过。
- **完全不碰 `GetNormalCustomerCount`**，所以与拾荒人开局、囤积癖短眠者之类改客人数量/睡眠的 mod 各改各的，不会重复加。只读 `Reputation.Points`，不写存档结构。

日志：补丁绑定时一条 `scavenger perk: patch target StoreClientManager.GenerateClient`；生效时一条 `scavenger perk: +1 scavenger client day=<天数> points=<点数> identifier=scavGeneral source=local.dumping_ground_map`；失败分别打 `CreateScavGeneral returned null` / `add failed: …`。

**悬停说明（0.3.36）**：20 点这一档实际是两件事——每天多一名拾荒客客人，以及首次达到 20 点时一次性获得一枚「拾荒者信物」放在前台。图标悬停文案已补成两件并标明信物只给一次，中英文同步。

**20 档定名与附赠便条（0.3.37 / 0.3.39 / 0.3.40 / 0.3.42 / 0.3.43）**：

- 名称改成「拾荒老手 / Scavenger Veteran」，抽成 `Reputation.Perk20NameZh` / `Perk20NameEn` 两个常量；40（老手）、60（活地图）两档和状态栏的四档等级名都没动（这里的「老手 / 活地图」是 0.3.37 时的旧名，0.3.46 已改成五档新名）。
- 首次达到 20 点，开张时在前台放**两件**：`scav_token` 信物 + 一张原生 `handnote` 便签。便签用 `PreBuiltItemHelper.CreateHandnote(正文, 标题, 落款, 1)` 生成，标题同时是物品名；点开走 `HandnoteUIManager` 的小纸条面板（标题 / 正文 / 落款），不是 `slip` 那种整页文档。全天可用于同一用途的候选只有 `slip`（整页文档）、`postit`（机器上留的 Note，没有阅读 UI）与 `handnote`（原版感谢条 / 欠条用的便签），选 `handnote`。
- 文案是拾荒者的第一人称复数口吻：标题「写给店主 / To the shopkeeper」（**标题同时是物品名**，改标题就等于改前台那件物品显示的名字），正文「这枚信物，只留给有本事的人。你的拾荒本领，我们服气。往后的日子里，我们会常来你店里照应。」/ "This only goes to people who've earned it. We've seen you work the dump. We'll be by your shop from now on."，落款「拾荒者们 / — the scavengers」。
- 两件原子发放：先放信物再放便条；便条放不下就 `RemoveItemFromStoreInventory` 把信物收回来、**不写** `scav_medal_given`，次日重来。前台满时连物品都不创建，避免泄漏。
- 悬停说明改成三件并述（每天一位客人 + 一枚信物 + 一张便条，仅一次）。

**客人来访原因（0.3.38）**：原生 `StoreClient.attractedType` / `attractedItemId` 只能填类型名或物品名，表达不了「被名声吸引」，所以改在 UI 侧：给 `NegociationUIManager.RefreshClientHeader(StoreClient)` 加 Postfix，在 `frame.clientSubtitle` 赋值之后覆盖文本。

- 判据：`storeClient.sourceModId == Core.Id` 且声望 ≥ 20。标记由 `ScavengerPerkPatch` 创建客人时调用 `ModHelper.SetClientSource(client, Core.Id)` 写入（就是 `StoreClient.sourceModId` 这个 public 字符串字段，不是 getter）。不再用 `identifier == scavGeneral`：原生随机客池、开场剧情、拖车摊贩与展示柜预测都会生成同 identifier 的拾荒客，无法区分是不是我们加的。
- 文案「被「拾荒老手」的名声吸引 / Drawn by a Scavenger Veteran's reputation」。副标题原生在无原因时是隐藏的、客人名上移 13px，补文本后用原版同样的 `SetRect(48, 70, 530, 26)` 恢复排布。
- 读档语义：客人不进存档（每天开张 `GenerateClient` 整批新建、`EndDay` → `DismissClientStack` 清空），读档后当天重新开张会重新生成并重新打标，因此行为确定，不存在「标记丢了对不上」的情况。
- 该标记的其它读取点只有原生的 `ModSourceHelper.GetClientModName` → 客人到达时 `RecordClientSource` 在对话记录里加一行「来自 mod 垃圾场拾荒地图 / Dumping Ground Map」（受原生选项 `showModSourceInRecorder` 控制，默认开）。除这一行外没有别处读它，不影响议价、价格与存档。
- 日志 `client reason: rewritten id=<identifier> day=<天数> points=<点数> text=…` 只在文本真的变化时打一条，不刷屏。

### Reputation Pop 联动（0.3.27）

装了「声望增加显示 / Reputation Pop」（`reputation_pop`）时，每次拾荒结算（仍是 `ScavengeReputationPatch` 的 `GetRandomScavengedItem` Postfix，不合并成整趟一次）额外调用它的公开入口：

- 目标用字面量 `AccessTools.Method("ReputationPop.Pop:Record", new[] { typeof(string), typeof(double) })` + `Delegate.CreateDelegate` 缓存成 `Action<string, double>`，参数 `("DGM_SCAVENGER", 本次价值 / 100.0)`。
- `ModHelper.IsModRunning("reputation_pop")` 为假时直接短路：不解析、不装补丁、不报错。类型不存在时 `AccessTools.Method` 返回 `null`，只打一条 `NOT FOUND`。
- 派系名：首次解析成功后手动 `new Harmony(Core.Id + ".reputation_pop").Patch(...)` 给 `StoreReputation.GetFactionNameByID` 挂 Prefix，仅 `id == "DGM_SCAVENGER"` 时返回「拾荒客 / Scavenger」，其它 id 原样放行。补丁只在对方 mod 运行时才装，不常驻。
- 颜色用对方默认值（红色），不自己绘制。
- 日志：`reputation pop: resolved ReputationPop.Pop:Record`（成功）/ `NOT FOUND` 或 `resolve failed: …`（失败）、`reputation pop: name prefix patched`，以及每次弹出一条 `reputation pop: delta=<本次增量> faction=DGM_SCAVENGER`。

扫描器复核（`ModScanner.Scan`）：字面量写法不含 `reflection-external`，`<Capabilities />` 仍为空。

### 20 点纪念：第二天开张多一枚拾荒者信物（0.3.31）

照搬「神秘仰慕者 / Secret Admirer」的做法：它是在 `StoreShutterButton.OnShutterDoorOpenMorning()` 的 Postfix 里用 `PlayerStore.AddDirectSellingItemToTable(...)` 把礼物放到前台。我们不用 Harmony，因为同一方法最后一行就是原生 `ModHook.FireOnShutterOpenedLate()`，订阅它位置等价。

时序（`StoreShutterButton.OpenShutter()`）：

```
PlayerStore.OnShutterOpened()   → GenerateClient()（20 点客人奖励在这里）
OpenShutter() / ToggleShutterDoor()
StoreShutterButton.OnShutterDoorOpenMorning()
  … 原生报纸 / 导师纸条 / 仰慕者礼物
  ModHook.FireOnShutterOpenedLate()   ← 我们放信物
EmporiumEntry.ShowFrontArea()   → 前台这时才显示，所以信物“开业时就在”
PlayerStore.OnLateShutterOpen()
```

- 条件：`Reputation.Points >= 20` 且 `!ModHelper.GetModDataBool(Core.Id, "scav_medal_given", false)`。
- 放置：`PlayerStore.Instance.TryAddDirectSellingItemToTable("scav_token", true, false, true, 0)`（owned、非赃物、跳过派系拦截、热量 0）。成功才写标记；前台满时不写，次日开张重试。
- 物品：`scav_token`「拾荒者信物 / Scavenger's Token」，图集精灵名就是 `scav_medal`，价值 100。它的原生效果只数**展示柜**里的数量（每枚 +20% 拾荒客到访），放前台不触发，所以不会和 20 点客人奖励叠加；玩家以后自己把它摆进展示柜才拿到原版加成。
- 一次性：标记写在 `PlayerStore.modData`（`scav_medal_given`），随存档保存，即“每个存档时间线一次”。读回发过之后的存档不再发；回档到发之前的存档会再发一次，因为那条时间线从未收到过。
- 日志：`scav medal: +1 scav_token on front counter day=<天数> points=<点数>` / `scav medal: skip (already given) points=<点数>` / `scav medal: front counter full, retry next opening day=<天数>`。
- **风险点**：事件发生在 `ShowFrontArea()` 之前，理论上此时前台容器已可用（同一方法里前面的原生纸条就是这么放的）。如果实测信物没出现，先看 `TryAddDirectSellingItemToTable` 是否返回 false；若不是，说明容器尚未就绪，需改用 `PlayerStore.OnLateShutterOpen()` 的 Postfix。`ScavMedal.cs` 的文件头注释已标出这一点。


### 五档骨架与 80 点奖励（0.3.46）

声望奖励改为五档：**20 拾荒老手 / 40 熟门熟路 / 60 眼力毒辣 / 80 识货行家 / 100 自己人**（英文 Scavenger Veteran / Knows the Back Ways / Keen Eye / Connoisseur / One of Us）。20 与 40 的行为不变；60 的隐藏储藏与次日见报在 0.3.52 落地（见下一节）；100 目前只落了门槛、档名与悬停文案，功能（雇一名拾荒者帮手）尚未实现，上线后需再核一次文案；80 已在 0.3.47 落地。

80 点（识货行家）生效两件事：

- **贵重品出现率解锁**：生效规则只有一条 `声望 ≥ 80 ? 滑条值 : 0`。80 之前贵重品池不进最终表（滑条上的值只保留、不生效）；达到 80 后完全按滑条数值生效——写 3% 就是 3%，写 8% 就是 8%，范围仍是 0–30%，默认 3%。沿用现有贵重品机制：从普通物品份额里扣，垃圾概率不变。
- **补给箱与钥匙卡**：这两项的概率由两个滑条决定——**补给箱概率**与**钥匙卡概率**，默认都是 **0.5%**，范围 0–5%、按 0.1 吸附，0% 表示不出。仍然只在声望达到 80 时生效；两项各自从普通物品份额里扣，垃圾概率不变，同一区只会出自己的配对：

| 区域 | 补给箱 | 钥匙卡 |
|---|---|---|
| 拆解堆 | `loot_crate_engineering`（工程箱） | `eng_keycard` |
| 生活废料带 | `loot_crate_service`（服务箱） | `ser_keycard` |
| 医疗废弃点 | `loot_crate_medical`（医疗箱） | `med_keycard` |
| 废武器堆 | `loot_crate_security`（安保箱） | `sec_keycard` |
| 入口空地 | 不放箱子 | 不放卡 |

箱子走 `ItemSpawner.Spawn("loot_crate_*")`：游戏自己的 `PreBuiltItemHelper` 会把 `loot_crate_*` 建成满箱上锁状态，不需要也不使用 `med_box` / `eng_box` 这类会给空盒的容器 id。钥匙卡压在同一个最终权重表里，因此拾荒时也可能先翻到卡、后翻到箱；面板上 80 档的悬停说明已写明「拾荒时可能遇到贵重品，偶尔还能翻到补给箱和对应的钥匙卡」。

**当场提示（0.3.60）**：翻到箱子或卡时各弹一条通知，和大小奖共用「受伤」那条通知条。箱子用「翻出一个上锁的…」的口吻，卡用「摸到一张…」，物品名取本地化词条（箱子是 `工程箱 / 服务箱 / 医疗箱 / 安保箱`，卡是游戏里的一次性钥匙卡）：
- 中文：「你在架子顶上翻出一个上锁的工程箱。」／「你在抽屉缝里摸到一张一次性钥匙卡（工程）。」
- 英文："You found a locked engineering crate on the top shelf." / "You picked up an engineering keycard under the sink."

面板实现：`Reputation.Thresholds` / `PerkThresholds` 统一为 20 / 40 / 60 / 80 / 100，柱上五个图标；状态名取当前达到的最高档，低于 20 仍显示「生手」。

### 五档悬停文案（0.3.51 定稿）

标题只有档名 + 状态 + 当前点数，例如「熟门熟路 [已激活] 40/200」，不再带「拾荒客·」前缀。五档统一「正文一行 + 斜体风味一行」：正文里句子之间只用句号、不手动换行。

| 点数 | 档名 | 正文（同一行） | 风味行（斜体） |
|---:|---|---|---|
| 20 | 拾荒老手 | 你结交了许多拾荒者朋友。每天会多一名拾荒者来你店里。 | 拾荒者们知道哪家店会给好价钱。 |
| 40 | 熟门熟路 | 你对这片场子越来越熟。三个高级拾荒场对你开放：拆解堆、医疗废弃点、废武器堆。 | 你知道哪里能捡到更好的东西。 |
| 60 | 眼力毒辣 | 拾荒时更有机会找到隐藏的储藏。第二天你会在报纸上看到自己的事迹。 | 别人翻过去的东西，你还会再看一眼。 |
| 80 | 识货行家 | 拾荒时可能遇到贵重品。偶尔还能翻到补给箱和对应的钥匙卡。 | 值钱的东西，你一眼就认得出来。 |
| 100 | 自己人 | 拾荒者会把你当成自己人。拾荒前可以花钱雇一名拾荒者帮手。 | 在这片垃圾场里，所有人都把你当兄弟。 |

60 的隐藏储藏 / 报纸事迹已在 0.3.52 实现；100 的雇人服务尚未实现，文案先落，功能上线后再核一次。

### 60 点爆大奖：垃圾场里翻出的储藏（0.3.52；0.3.67 起五区各有样板）

0.3.52 先在生活废料带落地，0.3.67 起五个区各有一套样板（`StashTemplate`），奖池都取本区主题类、7–8 件。中奖概率仍是**全五区共用同一个 AIMD 窗口**，不按区各算一份：

- 起点 **0.05%**；未中且窗口还没到上限一半时每次拾荒翻倍，过半后改为每次 **+0.05%** 线性爬升。
- 中奖后窗口**砍半**，再从该值线性往上爬。
- 上限来自设置项 `stash_cap`（默认 1.0%，范围 0.1–5.0%，按 0.1 吸附）。**声望不到 60 时上限压到 0.1%**，不是完全不中。
- **一趟拾荒最多中一次**：进入某块地后重置这个限制，换区或重新进图才会再给机会。
- 中奖**当场给 7–8 件**该区样板物品（随机件数），直接进本次拾荒的返回列表，所以会照常计入声望。
- 中奖当场复用原生拾荒界面「受伤」那条通知（`StoreUIManager.Instance.Notify(text, "sec")`），0.3.67 起每区一句自己的「翻到了什么」，地点措辞仍从该区池里随机取，下一行统一是「今天在这个地方，每次搜索都可能多一件东西。」，不另做 UI。
- 中奖后**当天、同一区域**，后续每次搜索固定再给 **1 件**（0.3.59 起由 25% 概率改为 100%）。不再改任何权重表。
- 中奖当晚把「次日新闻」写进存档（`stash_news_day`）；第二天开日时由 `StoreEventManager.OnDayStart` 的 Prefix 调原生 `QueueFuturEvent` 排队，`HandleFuturEvent` 在同一次开日里立刻激活它（读档错过开日会在下一次开日补发）。事件 id 沿用 `dgm_stash_<region>`，中英文字面文本直接写在代码里，新闻 UI 不过 `LocHelper`，没有改本地化资源。

各区样板（奖池 / 报纸标题 / 价格修正）：

| 区域 | 奖池（本区主题类） | 报纸标题 | 价格修正 |
|---|---|---|---|
| 入口空地 | 工具 7 件：螺丝刀、钢丝钳、焊枪、模组提取器（基础）、撬棍、美工刀、斧头 | 门口空地挖出的工具箱 / A Tool Chest Dug Up at the Main Gate | `TOOL` −20% |
| 拆解堆 | 材料 8 件：废金属、3D打印耗材、金属螺丝、线缆、枪械零件、电子元件、金属锭、助焊剂 | 拆解堆出了整垛料 / A Whole Stack of Stock From the Scrap Pile | `MATERIAL` −20% |
| 生活废料带 | 食品 4 件（零嘴、加工果汁、猫粮棒、合成肉）+ 日用品 4 件（牙膏、卷纸、洗发水、净水器） | 废料带里翻出的年货 / A Holiday Haul in the Refuse Lane | `FOOD` −20% 与 `HOUSEHOLD_GOOD` −20% 两条 |
| 医疗废弃点 | 医疗 8 件：绷带、止血绷带、外用绷带、血袋、医用酒精、药膏、噬菌霉素片、医疗用品（普通） | 医疗废弃点翻出成箱药品 / Crates of Medicine From the Medical Dump | 不挂（原版没有医疗降价先例，只写报纸） |
| 废武器堆 | 弹药 3 件（.22 手枪弹、重手枪弹、非致命手枪弹）+ 配件 2 件（加长 .22 弹匣、消音器）+ 枪支 3 件（3D打印手枪、土制手枪、左轮） | 废武器堆的军械箱 / An Arms Crate in the Weapon Dump | `AMMUNITION` / `FIREARM` / `GUN_MOD` 三条各 −20% |

`NegociationData` 的类型串按 `SetGameItemType` 的实际取值核对过：`FOOD`、`HOUSEHOLD_GOOD`、`MATERIAL`、`TOOL`、`AMMUNITION`、`FIREARM`、`GUN_MOD` 都能在对应物品的工厂方法里找到同名写入。土制 / 简易枪械配件会同时带 `MAKESHIFT`，但它们先经 `GunModHelper.InitGunMod` 拿到了 `GUN_MOD`，所以这条修正照样覆盖。

存档键（`PlayerStore.modData`，与声望同一套机制）：`stash_chance`、`stash_phase`、`stash_hit_day`、`stash_hit_region`、`stash_news_day`、`stash_news_fired`。读档时无条件重读并夹紧，窗口不会跳变。

扩展位：`StashTemplate` 里每条样板带奖励池、当场提示、中英文案与两个平行数组 `ItemTypes / PricePercents`；不挂价格的区把两个数组留空，只在报纸正文里叙事。

### 60 点小奖：翻出一整箱同一样东西（0.3.56 起，0.3.57 改件数，0.3.58 改门槛，0.3.60 扩候选）

小奖与大奖共用 60 档门槛，但走完全独立的另一条概率：设置项 `minor_percent`（「小奖概率（%，0–10）」），默认 3.0%，范围 0–10%，按 0.1 吸附，0% 不出。每次拾荒独立掷一次，**不写 AIMD 窗口**。

- **两档门槛（0.3.58）**：声望 ≥ 60 时按设置值全值生效；声望 < 60 时按设置值的 **1/10** 生效（默认 3% → 0.3%，设 6% → 0.6%，设 0 则两档都是 0）。
- 每个区 4–6 种「整箱」（0.3.60 起由 2–3 种扩开），命中后随机抽一种，箱里只放**同一样东西**；候选单价都在 15–60 之间，不含垃圾，同一区尽量覆盖不同品类。
- 0.3.57 起件数不再写死：每次在 `小奖整箱价值 × [0.75, 1.25]` 里取一个金额，除以该箱物品单价后四舍五入，夹在 1–12 件。`小奖整箱价值` 默认 200、范围 50–400、按 10 吸附；它只影响小奖，不影响大奖和基础 EV。
- 目标 200 时各区（物品 / 件数 / 总值）：合成肉 6–10（150–250）、能量饮料 8–12（144–216）、贝氏优质香草冰淇淋 4–6（160–240）、一卷厕纸 6–10（150–250）、一卷纸巾 4–7（140–245）、止血绷带 9–12（144–192）；电子元件（普通）5–8（165–264）、枪械零件 6–10（150–250）、金属锭 3–5（150–250）、螺丝刀 6–10（150–250）、损坏模组 3–6（135–270）；止血绷带 9–12（144–192）、噬菌霉素片 6–10（150–250）、医用酒精 4–7（140–245）、血袋 5–8（165–264）、医疗用品（普通）4–6（160–240）；.22 SR弹药 8–12（160–240）、10毫米弹药 5–8（150–240）、加长.22弹匣 4–7（140–245）、美工刀 5–9（140–252）、厨刀 3–6（135–270）、“解放者”3D打印手枪 4–6（160–240）。
- 物品 id：生活废料带 `processed_meat` / `energy_drink` / `beis_icecream` / `toilet_paper` / `paper_towel` / `hemostatic_bandage_item`；拆解堆 `common_electronic` / `gun_part` / `metal_ingot` / `screwdriver` / `system_module_corrupt`；医疗废弃点 `hemostatic_bandage_item` / `phagimycin_pill` / `rubbing_alcohol` / `blood_bag` / `common_medical`；废武器堆 `small_pistol_ammo` / `heavy_pistol_ammo` / `mag_22_ext` / `box_cutter` / `kitchen_knife` / `printed_gun`。改动只换候选与句式和地点，命中率、目标价值与件数算法不变，所以小奖的期望收益不变，只是箱子的内容更多样。
- 入口空地没有整箱（低级区不给）。
- **不登新闻、不改价格、不触发大奖的当日额外收获**；物品进同一次返回列表，计入声望（与正常拾荒一致）。
- 与大奖共存：**先掷大奖，大奖命中就完全跳过小奖**；大奖没中才掷小奖。当天同区额外收获最后判定（每次固定 1 件），不受小奖影响。
- 提示沿用「受伤」那条通知条，0.3.60 起与其他三类统一成「谁在哪里翻到什么」：「你惊喜地发现，{地点}压着一整箱{物品名}。」英文 "To your surprise, a whole crate of {item} was tucked {place}." 物品名取游戏本地化，地点从该区措辞池里随机取一条，整句只出现一个地点；四类提示（补给箱 / 钥匙卡 / 小奖 / 大奖）共用这套地点池。
- 原版「泥炭」就是合成肉 `processed_meat`（`PriceHelper.PEAT` 与该物品 `unitValue` 同为 25；`CreatePeatClient` 的收尾动作是把 `processed_meat` 加进直售表），是普通食物物品，可直接 `ItemSpawner.Spawn`；原版只有一个 `PeatContent` 报纸事件，没有可复用的箱子物品。

日志前缀 `[DumpingGroundMap] stash:`：`loaded`、`jackpot`、`notice shown`、`miss`、`bonus`、`news queued`、`news queue failed`、`reset for new game`、`spawn failed`；补丁挂载时另打一条 `stash news: patch target StoreEventManager.OnDayStart`。

## 掉落设置

地图窗口底部不再有设置按钮，全部调节都在游戏的**模组设置窗口**（模组列表里本 mod 的「设置」）里，形态与原生条目一致：多条滑条加一条下拉。滑条的数值直接显示在滑条右侧的同一行，不再另起一行。

| 设置 | 形态 | 范围 | 默认 | 说明 |
|---|---|---|---|---|
| 垃圾出现率倍率 | 滑条 | 0–100% | 50% | 每次拾荒垃圾出现的多少：50% 把原本的垃圾量减半（入口空地约 49% → 24%），100% 是原版水平，0% 不再出垃圾；不是「垃圾占全部掉落的一半」 |
| 贵重品出现率 | 滑条 | 0–30%（步长 0.5） | 3% | 每次拾荒单独判断这一趟是否出贵重品，没出不会累积、没有保底；8% 约每 12 次出 1 次。贵重品从普通物品里换出来，垃圾不受影响。声望达到 80（识货行家）后本项才生效：80 之前按 0 算，80 之后完全按滑条数值生效（滑条始终可调，设置值会保留） |
| 补给箱概率 | 滑条 | 0–5%（步长 0.1） | 0.5% | 声望达到 80（识货行家）后，每次拾荒出补给箱的绝对概率；从普通物品份额里扣，垃圾概率不变，0% 表示不出。只在本区有对应箱子时出现（拆解堆、生活废料带、医疗废弃点、废武器堆） |
| 钥匙卡概率 | 滑条 | 0–5%（步长 0.1） | 0.5% | 声望达到 80 后，每次拾荒出本区对应钥匙卡的绝对概率；与补给箱各自独立判定，同样从普通物品份额里扣，垃圾概率不变，0% 表示不出 |
| 小奖概率 | 滑条 | 0–10%（步长 0.1） | 3.0% | 每次拾荒独立掷一次小奖：声望 ≥ 60 按本值全值生效，声望 < 60 按本值的 1/10 生效（默认 3% → 0.3%），0% 两档都不出；命中翻出该区一整箱同一种物品（每区 4–6 种），不登新闻、不改价格、不触发大奖的当日额外收获，也不影响爆大奖的 AIMD 窗口；大奖命中时跳过小奖 |
| 小奖整箱价值 | 滑条 | 50–400（步长 10） | 200 | 只决定小奖那一整箱值多少钱，不影响大奖，也不影响上面的基础 EV。每次小奖在 `目标值 × 0.75–1.25` 里取金额，除以该物品单价后四舍五入得到件数（1–12 件）；调到 100 件数会明显变少 |
| 爆大奖概率上限 | 滑条 | 0.1–5.0%（步长 0.1） | 1.0% | 60 档「眼力毒辣」的 AIMD 上限：未中翻倍、过半后每次 +0.05%、中奖砍半。声望不到 60 时上限压到 0.1%（仍可能中）；五区共用同一个窗口，0.3.67 起五个区都有样板。中奖后当天同区后续每次搜索固定多 1 件 |
| 区域专精度 | 下拉（四档） | 较少 / 中等 / 较多 / 全部 | 较多 | 决定除垃圾外，捡到的东西有多偏向本区特产：档位越高，本区特产越常见、别处的通用物品越少；「全部」时除垃圾外只出本区特产。每个区各有自己的特产清单，垃圾不受影响 |

原来的「偏好类别」跨区下拉已删除：地点本身就是主题，再叠一个全区通用的类别只会让两个区域变成同一种出产。0.3.7 起，原先四个区各一条的区域专精度滑条也合并成上面这一条——既然不分区域，就不再逐区设置。0.3.10 删除了「贵重品保底」：贵重品本身价值高，再加保底会明显影响平衡，现在只由出现率一条概率控制。0.3.12 起，区域专精度不再向玩家暴露 0–100 的数字，改为四档下拉；默认改为「较多」，垃圾倍率默认改为 50%、范围收紧到 0–100%。0.3.14 把三条设置的 (i) 提示改写成短句加例子：垃圾那条说明 50% 不是垃圾占比，贵重品那条说明每次拾荒独立判断、没有保底，专精那条说明每个区各有特产、垃圾不受影响。0.3.15 把区域从 4 个扩到 7 个，七张基础表都按「垃圾 100% 时单次价值 12.26」重解垃圾权重，并把终端区原先 26.62 的默认收益压平。0.3.16 把 7 个区域合并回 5 个（报废终端区并入拆解堆、配件箱并入废武器堆），改成「低级两区 / 高级三区」的声望梯度，专精改为「原版表 → 本区主题表」的线性插值。0.3.17 把声望从侧车文件搬进存档内的 `PlayerStore.modData`。0.3.18 删除了旧的侧车迁移与全部磁盘读写，避免触发模组扫描器的文件权限判定。0.3.19 让声望以一行「垃圾场」出现在评价箱的原生声望面板里，纯内存合成、不写存档。0.3.20 把该功能改成编译期类型绑定（无字符串反射、无程序集扫描），冲突检测改用 `ModHelper.IsModRunning`，清单能力仍为空。

### 区域专精度怎么算

四个档位对应四个内部比例 f：较少 25%、中等 50%、较多 75%、全部 100%。五个区共用同一个档位，但各有自己的主题表（入口空地 = 原版构成；拆解堆 = 材料 / 工具 / 电子废料；生活废料带 = 日用 / 食品 / 少量医疗；医疗废弃点 = 医疗；废武器堆 = 武器）。0.3.62 起五张主题表全部是「大类 → 子项」两层结构（见下方「五区大类表」）。

每区都有两张表：**原版表**（即入口空地那张，未改动的 `dumpingGroundTG`）与**本区主题表**。对每件物品，权重直接线性插值：

```
w(f) = (1 − f) × 原版权重 + f × 主题权重
```

垃圾也参与这条插值（垃圾在两张表里各自是一个权重）。插值完再按固定顺序合成概率：

1. 垃圾概率 = 插值后的垃圾占比 × 垃圾倍率（垃圾倍率只缩放这一步，不参与插值）。
2. 贵重品总概率从**非垃圾物品**里按比例让出，不再动垃圾。
3. 剩下的份额按权重分给非垃圾物品。

因此 f = 0 时五个区都等于原版表；f = 100% 时只剩本区主题物品；中间档是两者的混合。默认档（垃圾 50%、专精较多）下各区：

| 区域 | 较少 25% | 中等 50% | 较多 75%（默认） | 全部 100% | 档位 | 默认档垃圾概率 |
|---|---:|---:|---:|---:|---|---:|
| 入口空地 | 16.01 | 15.36 | 14.71 | 14.06 | 低级 | 24.4% |
| 生活废料带 | 16.13 | 15.59 | 15.05 | 14.51 | 低级 | 24.0% |
| 拆解堆 | 16.39 | 16.15 | **15.94** | 15.76 | 高级 · 40 | 28.8% |
| 医疗废弃点 | 16.55 | 16.44 | **16.33** | 16.22 | 高级 · 40 | 23.5% |
| 废武器堆 | 19.15 | 21.62 | **24.09** | 26.54 | 高级 · 40 | 24.9% |

0.3.62 起类内权重一律与单价成反比，五区默认档整体下移（0.3.61：16.66 / 16.53 / 18.54 / 18.35 / 26.92），低级区与高级区的差距缩小。废武器堆仍是最高的一区（24.09）：它 50% 是垃圾，非垃圾部分又由材料、近战、子弹、配件、枪支分摊，非垃圾均价约 34.8，是其它区（16.6–20.2）的一倍多——这是「废武器堆 = 高级武器区」的既有定位，不是权重失误。

### 调节与写盘

垃圾、贵重品、补给箱、钥匙卡、小奖、小奖整箱价值六条滑条是拖动输入：步长垃圾 1%、贵重品 0.5%、补给箱 / 钥匙卡 / 小奖 0.1%、整箱价值 10，拖动过程中滑条右侧的数值与下方五区对比逐帧刷新，但只在松手（PointerUp）或点窗口底部「应用」时才写模组配置；松手事件丢失时会在下一次拖动开始或点「应用」时补写。区域专精度是原生下拉，选中后由游戏窗口的「应用」写入。五个预设按钮（默认 / 专精拉满 / 赌贵重品 / 默认 200 / 贴近原版 100）由本 mod 注入在五区对比下方：前三个写垃圾、贵重品与专精，后两个只把小奖整箱价值设为 200 / 100，不动其它设置；点击后立即写入并同步所有控件；「重置」沿用游戏窗口自带的按钮（把控件复位到默认，再点「应用」生效）。

- 默认：垃圾 50% / 贵重品 0% / 区域专精度 较多（75%）/ 小奖 3% / 小奖整箱价值 200
- 专精拉满：区域专精度 全部（100%）
- 赌贵重品：贵重品 8%（垃圾 50% / 区域专精度 较多）
- 默认 200：只把小奖整箱价值设为 200
- 贴近原版 100：只把小奖整箱价值设为 100（件数随之变少）

### 界面结构

注入方式：`ModConfigWindow.BuildEntry` 的 prefix 记录当前条目与 builder，postfix 在该条目原生行结束后追加本 mod 的行；另外补丁 `CustomUIElement.SetFixedWidth`，在原生代码给滑条定宽（同一行尚未结束）的那一刻，把数值标签插进这一行滑条之后，所以数值和滑条在同一行、位于滑条右侧。原生滑条预制体没有自带数值文本组件（Assembly-CSharp 里不存在监听 onValueChanged 的 SliderValue 之类组件，滑条分支只传了 null 回调和 224 宽度），因此只能这样在同位置补一个标签；若该补丁因为游戏版本变化失效，会自动退回「数值单独一行」的旧形态，不会丢读数。

界面顺序：垃圾倍率（右侧数值）→ 贵重品出现率（右侧数值）→ 补给箱概率 → 钥匙卡概率 → 小奖概率 → 小奖整箱价值 → 爆大奖概率上限（均带右侧数值）→ 区域专精度（四档下拉）→「每次拾荒大约能捡到的物品价值（拖动时实时刷新）」标题 → **原版参考（垃圾 100%、无专精）：各区 12.26** → 五区「原版 12.26 → 现在 y.yy（±%）」五行 → 各区平均 → 第一行三个预设按钮（默认 / 专精拉满 / 赌贵重品）+ 第二行两个小奖预设（默认 200 / 贴近原版 100）。原版参考是固定基准（原版表，垃圾 100%、无专精、无贵重品），不随当前设置变化；f = 0 时五区都等于原版表，所以基准行只有一个数。点击预设时，区域专精度下拉会被同步切到对应档位（0.3.13 修复）。0.3.11 移除了标题下方原来那行「本次：… 四区数字同时变化 / 仅这些区变化」的动态说明。

0.3.3 修复保留：PointerUp 触发器只挂在持有 `Slider` 组件的那个物体上；挂到子图形上会让 `EventTrigger` 抢走按下与拖动事件，滑条收不到 `OnDrag`。诊断日志前缀 `[DGM-SLIDER]`：注入时打印每条滑条的绑定与范围，拖动开始/结束各一行，拖动中每 0.25 秒一行（含回调计数与首区价值）。

### 区域专精度换档即时刷新（0.3.34）

原生 `ModConfigWindow.BuildEntry` 给下拉框传的 `onChanged` 和滑条一样是 `null`，换档只改控件、按「应用」才写回配置项，所以 EV 行一直不变。现在在注入时用 `CustomUIElement.OnDropdown` 自己绑监听：

- 换档 → `Tuning.SetSpecialtyTierLive(index)` 立即写回配置项（不清 `hasLive`，不会打断正在预览的滑条）→ `OnChanged` → `Tuning.Invalidated()` 清缓存 → `NativeSettings.RefreshIfOpen()` → `Refresh()`。
- 与垃圾倍率拖动（`SetJunkPercentLive`）、贵重品出现率拖动（`SetHighPercentLive`）、预设按钮（`Set*` 三条）共用同一个 `Refresh()`：每区「原版 → 现在」、各区平均、原版参考都在同一处重算。
- 预设仍然显式 `SetIndex(tier, false)` 同步下拉显示；`notify=false` 不会回调监听，且 `SetSpecialtyTierLive` 对同值直接返回，不会重复刷新或循环。
- 日志：`[DGM-SLIDER] region specialty: dropdown bound (options=4)`，每次换档一行 `[DGM-SLIDER] ev refresh tier=3 percent=100 source=dropdown`。

### 调试项（0.3.32）

原生 `ModConfigWindow.BuildEntry` 给开关和滑条传的回调都是 `null`，只有按下「应用」才把控件值写回配置项；也没有「按条件隐藏整行」的能力。0.3.30 用 `CustomUIElement.SetVisible` 隐藏 `debug_points`，导致勾选后不会实时出现，而且同一行里被 `SetFixedWidth` 补丁误加的数值标签（错误地复用给了 `highValue`）仍然显示为 `0.0%`，看起来像「拖不动」。

0.3.32 改成：

- 两行常显，删除整套隐藏/显示逻辑；`AttachValueLabel` 只在 `junk_percent` / `high_percent` 上追加标签，不再影响调试行与 `highValue`。
- `debug_enabled` 绑 `OnToggle` → `Tuning.SetDebugEnabledLive`，勾选即时生效（原生 `Apply` 时值已相同，不会重复触发）。
- `debug_points` 绑 `OnSlider`（拖动时按 0.5 吸附）与 `PointerUp` → `Tuning.SetDebugPointsFromSlider`，松手写入；开关关闭时只记一条 `debug: reputation setter ignored (debug off)`。
- 控件仍是原生 float 滑条：`AddSlider(min, max, value, null, wholeNumbers=false, key)` + `SetFixedWidth(224)`，范围 0–200 由 `Define` 传入；绑定时会打一行 `[DGM-SLIDER] debug_points: slider bound (min=0 max=200 whole=False)`。
- 数值显示（0.3.33）：调试行右侧单独一条 `debugValue` 标签（宽 56px，左对齐），一位小数（`19.5` / `200.0`）。拖动时按 0.5 吸附实时刷新，松手后回读 `Tuning.DebugPoints`（开关关闭时会被忽略，标签因此退回真实值）。不复用 `highValue`，所以垃圾/贵重品两行的标签互不影响。
- 空间：原生滑条固定 224px，调试值最多 5 个字符，56px 足够；垃圾/贵重品仍是 72px（带 `%`）。若窗口再窄，优先缩到 48px 或改右对齐，不影响其它行。

### 调试点数为什么没保住（0.3.35）

Player.log 证据（2026-11-14 21:18–21:19）：

```
[DGM-SLIDER] debug slider release raw=19 entry=19 ...
NullReferenceException
  at UnityEngine.Object.Instantiate ...
  at TreeNodeRender..ctor / GameGridInventory ...
  at PlayerStore..ctor
  at PlayerStore.get_Instance
  at DumpingGroundMap.Reputation.StoreKey
  at DumpingGroundMap.Reputation.SetPoints
  at DumpingGroundMap.Tuning...<OnChanged>b__...
[ModConfig] OnChanged of config entry "debug_points" threw ...
```

- 模组设置窗口只有主菜单的「模组」入口能打开（`MainMenuUIController.OpenModPanel` → `ModUIManager.OpenPanel`），改 19 时还没有 `PlayerStore`。
- `PlayerStore.Instance` 的 getter 在没有存档时会**新建** PlayerStore；构造函数要 `GameGridInventory` / `TreeNodeRender`，主菜单场景没有这些对象，必抛 `NullReferenceException`。
- 异常被 `ModConfig` 的 `OnChanged` 调用处吞掉，只打一行日志；滑条释放日志读的是配置项值，所以仍显示 `applied=19`，看起来像写成功了。
- 随后无论读档还是新档，存档里都是 0（新档还有一条 `reputation reset for new game`）。
- 第二个隐患：即使有存档，`modData` 也只在日终 `SaveGame` 时落盘，改完立刻强退同样会丢。

0.3.35 的改法：

- `Reputation.StoreKey` / `SetPoints` 先查 `PlayerStore.HasInstance`，没有存档绝不触碰 `Instance`。
- 主菜单改点数 → `SaveFiles.IndexSave<bool>("dgm_debug_pending", true)` + `SaveFiles.IndexSave<float>("dgm_debug_pending_points", points)`，存在游戏自己的 `saves_index.es3` 里；读档（`OnGameLoadedLate` → `Reload`）或新档（`OnNewGameStarted` → `NewGame`）时应用一次，然后 `IndexDeleteKey` 清掉，不会每次读档都覆盖。
- 有存档时改点数 → `Reputation.SetPoints` 写 `rep_total_value`，再用原生入口 `public void PlayerStore.SaveGame()` 落盘。调用排在下一帧的 `LateUpdate`（`DeferredSave`），避开读档时 `EmporiumEntry.Start` 尚未跑完的初始化窗口；`SaveGame` 内部本来就会处理槽文件被占用 / 损坏，和日终存档是同一条写盘路径。
- 关掉「启用调试项」会清掉排队值。
- 日志：`debug: no save loaded, queued 19 points for next load`、`debug: applied queued 19 points (value=1900 saveNow=yes)`、`save now (debug): slot=… day=… value=1900 points=19`、`save now (queued): …`。

正常存档时机不受影响：日终的 `SaveGame` 仍然照常执行，调试保存只是多写一次同一槽位、同一份数据；不改写存档结构、不新增键（只有全局索引里两个 `dgm_debug_pending*` 临时键）。

### 主菜单改点数没生效（0.3.44）

证据（2026-11-14 22:52，GameLog）：

```
[22:52:44] [Log] [DumpingGroundMap] debug: reputation set to 19 points (value=1900)
[22:52:44] [Warning] [DumpingGroundMap] save now (debug) failed:
[22:52:53] [Log] [DumpingGroundMap] reputation load: ... value=0 points=0 previous=1900
```

- 设置窗口只能在主菜单打开（`MainMenuUIController.OpenModPanel` → `ModUIManager.OpenPanel`，全程序集仅此一处）；回到主菜单后 `PlayerStore` 对象仍在内存里，`PlayerStore.HasInstance` 仍为 true，旧判据因此走了「直接写内存 + 立即存档」分支。
- 但主菜单是 `EmporiumMenu` 场景，`EmporiumEntry.Instance` 已经是 null；`PlayerStore.SaveGame()` 第一行就解引用 `EmporiumEntry.Instance.invElement`，直接抛异常（异常消息为空），值只留在内存。
- 紧接着读档，`Reload()` 无条件用存档里的旧值（0）覆盖内存，于是看起来完全没生效。

0.3.44 的改法：

- 新增 `Reputation.ApplyDebugPoints`：只有 `PlayerStore.HasInstance && EmporiumEntry.Instance != null`（真正在 Emporium 场景里）才直接写内存并调 `SaveGame`；否则一律写进 `SaveFiles` 索引排队，等下次读档/新档应用。
- `SaveNow` 失败时把当前值重新排进 `dgm_debug_pending*`，不再静默丢弃；日志同时打印异常类型（`Describe`），消息为空也能看出是哪一类异常。
- 日志：`debug: Emporium not loaded, applying on next load`、`debug: queued 19 points for next load (Emporium not loaded)`；失败时 `save now (debug) failed: NullReferenceException` 加 `debug: save failed, 19 points queued for next load`。

正确操作：主菜单 → 模组 → 本模组设置 → 拖点数（松手即生效）→ 关窗口 → **读档**（或开新档）。值在读档完成时写入，并立刻补存一次；不需要再点一次应用。


贵重品池（价值 250–400，按 `unitValue`）：一袋“蓝血”、高级滤水器、海德拉科技微型净水器、瓶装泉水、赛纳普斯注射器、电子元件（稀有）、模组提取器（高级）；各区的具体分布不同。贵重品不受区域专精度影响，它本身就是稀有掉落。

### 掉落表审查（0.3.9 / 0.3.15）

四个老区的基础表与贵重品池逐项核对过 `Assembly-CSharp` 里的物品定义（`*ItemDirectory` 的注册标签、`EnableTag` 参数、`Init*Helper` 调用与 `SetSpriteAndShape` 的形状名），移除了两件不属于随机垃圾的东西：

| 物品 | 游戏内名称 | 依据 | 处理 |
|---|---|---|---|
| `luxury_supply_crate` | 给埃莉诺的慰问包裹 | `ContainerItemDirectory` 容器；`EnableTag("IMPORTANT_TAG")`；外观形状 `eleanor_box`（埃莉诺的盒子）；`StolenHelper.InitStolenItem` 标记为他人所有 | 移除（剧情物品，不是「奢侈品补给箱」） |
| `chem_scanner` | 成分光谱仪 | `MedsItemDirectory`；定义了 `maySelectSlotItemFunc / canSelectSlotItemFunc / mayThisTargetItemFunc / canThisTargetItemFunc / onItemTargetItemFunc` 一整套「对背包内其它物品使用」的检测逻辑，并带 `CAN_USE_ON_NON_OWNED_TAG` | 移除（功能装置，用途是拆解鉴定，不是可回收垃圾） |

基础表 53 项全部是普通材料 / 工具 / 日用 / 食品 / 药品 / 制式武器 / 模组，没有任何一项带 `IMPORTANT_TAG`、`CONTAINER_TAG` 或容器 / 检测器 / 装置类回调，因此一项未删。贵重品池由 9 件减为 7 件，各区权重按原比例保留（只去掉被删的两项），贵重品总概率与基础表概率不变。

0.3.15 新增的近战武器、枪械、弹药、枪械配件与医疗品逐项扫过对应 `*ItemDirectory` 的工厂方法：6 件近战、11 件枪械 / 弹药、31 件枪械配件、11 件医疗品（另有 `common_medical` 注册在 `MaterialDirectory`），均未出现 `IMPORTANT_TAG`、容器标记或剧情 / 偷窃初始化调用，可安全进入随机池。

## 掉落权重

每个区域有一张独立的「主题表」，与原版表按专精度线性插值（见上一节）。表内概率 = 该物品权重 ÷ 该区权重总和。

| 地标 | 档位 | 定位 | 大类份额（整表按 10000 计） | 垃圾基准占比 |
|---|---|---|---|---:|
| 入口空地 | 低级 | 通用杂项 | 垃圾 4875、材料 1750、食品 1000、日用 850、医疗 450、近战 500、工具 350、杂项 225 | 48.75% |
| 生活废料带 | 低级 | 日用与消耗品 | 垃圾 4767、食品 2600、日用 1800、医疗 500、空瓶 333 | 47.67% |
| 拆解堆 | 高级 · 40 | 材料 / 工具 / 电子废料 | 垃圾 5989、材料 2900、工具 520、模组 400、杂项 191 | 59.89% |
| 医疗废弃点 | 高级 · 40 | 医疗品 | 垃圾 4647、医疗耗材 3200、药品 2000、器械 153 | 46.47% |
| 废武器堆 | 高级 · 40 | 武器 | 垃圾 5000、材料 2000、近战 1500、子弹 480、配件 520、枪支 500 | 50.00% |

0.3.62 起五区统一走 `ScavLoot.BuildCategories`：先定大类份额，再按类内权重把份额分给子项，取整误差补给类内权重最大的一项。垃圾份额沿用各区原有基准，没有一刀切成 50%。类内权重一律与单价成反比（单价越低权重越大），避免单一高价物品概率过高、也把 EV 从 0.3.61 的水平收回来。

垃圾份额就是主题表里「垃圾」那一项的权重，在「垃圾倍率 100%、专精全部」时正好等于垃圾占比。

0.3.15 的定标（垃圾 100%、专精全部时单次价值 12.26）只作参考：0.3.62 起不再按该目标反解，改由大类份额与反比权重直接决定。

物品价值取数：游戏代码中 `GameItem.unitValue` 字段（`SetValue` 的满盒物品按内容折算：10 条检测条 + 盒子 = 31，9 片净水片 + 盒子 = 55；`random_*_module` 按生成的基准模组 70 计）。

五区基准值（垃圾倍率 100%、专精全部、无贵重品）：

| 区域 | 档位 | 基准价值 | 0.3.61 |
|---|---|---:|---:|
| 入口空地 | 低级 | 10.49 | 12.26 |
| 生活废料带 | 低级 | 10.81 | 12.26 |
| 拆解堆 | 高级 · 40 | 10.30 | 12.26 |
| 医疗废弃点 | 高级 · 40 | 12.38 | 14.10 |
| 废武器堆 | 高级 · 40 | 18.70 | 21.20 |

默认档（垃圾 50%、专精较多、无贵重品）为 14.71 / 14.94 / 15.94 / 16.51 / 24.09（0.3.61：16.66 / 16.53 / 18.54 / 18.35 / 26.92）。0.3.65 起生活废料带与医疗废弃点的药品里多了免疫宁次品；0.3.66 起生活废料带的日用里多了酿酒原料与假烟，这一区略降。

### 五区大类表（0.3.62）

主题表是「大类 → 子项」两层：大类份额固定（整表按 10000 计），类内权重只决定子项之间的相对比例。垃圾是独立一档，不参与类内分配。下面列出跨区共用的材料类、以及废武器堆独有的四个大类；其余区的大类构成见上表，完整类内权重（含单价）在 [`docs/掉落权重与期望收益.md`](docs/掉落权重与期望收益.md) 的 v0.3.62 章节。

**材料（五区通用，权重 140 / 120 / 20 / 18 / 12 / 8 / 4）**

| 物品 | id | 单价 | 类内权重 | 类内占比 |
|---|---|---:|---:|---:|
| 废金属 | `scrap_metal` | 10 | 140 | 43.5% |
| 3D打印耗材 | `printer_plastic` | 12 | 120 | 37.3% |
| 金属螺丝 | `nuts_metal` | 14 | 20 | 6.2% |
| 线缆 | `wire` | 16 | 18 | 5.6% |
| 枪械零件 | `gun_part` | 25 | 12 | 3.7% |
| 电子元件（普通） | `common_electronic` | 33 | 8 | 2.5% |
| 金属锭 | `metal_ingot` | 50 | 4 | 1.2% |

材料类的权重由枪匠链条的实际消耗量决定，不是纯按单价：枪匠用 3D 打印机（`MachinePrinter`）加打印芯片造枪、弹匣和近战，芯片工厂通过 `SetSingleAdditionalRequiredItem` 声明每次打印消耗的材料，Cecil 扫出的配方里出现最多的是 `printer_plastic`（手枪芯片 ×2、.22 弹匣 ×2–3、美工刀 ×1）与 `scrap_metal`（消音器 ×5、内部弹簧 ×2、各类 10mm 弹匣 ×2–3、砍刀系 ×2–5），`metal_ingot` 只用在土枪框架与战斗刀/砍刀上（×1–3）。线缆、金属螺丝不参与任何打印配方，因此压到低价档以下。枪械零件（25）由枪械组装消耗，按需求排在电子元件之前，但已不再是材料里最多的一项。

**近战（废武器堆，权重 30 / 26 / 20 / 14 / 10 / 8 / 6 / 5 / 3 / 2 / 1）**

| 物品 | id | 单价 | 类内权重 | 类内占比 |
|---|---|---:|---:|---:|
| 玻璃刀 | `glass_shard_shiv` | 8 | 30 | 21.6% |
| 铅管 | `pipe_weapon` | 15 | 26 | 18.7% |
| 美工刀 | `box_cutter` | 28 | 20 | 14.4% |
| 厨刀 | `kitchen_knife` | 45 | 14 | 10.1% |
| 短柄斧 | `hatchet` | 55 | 10 | 7.2% |
| 切肉刀 | `kitchen_cleaver` | 65 | 8 | 5.8% |
| 撬棍 | `crowbar` | 75 | 6 | 4.3% |
| 战斗匕首 | `combat_knife` | 75 | 5 | 3.6% |
| 剥皮刀 | `skinning_knife` | 100 | 3 | 2.2% |
| 眩晕警棍 | `stun_baton` | 165 | 2 | 1.4% |
| 战斗砍刀 | `combat_machete` | 185 | 1 | 0.7% |

0.3.61 里玻璃刀（8）权重 267、低于美工刀（28）的 386，方向是反的；0.3.62 已倒过来：最便宜的玻璃刀第一，越贵越靠后。

**子弹（废武器堆，权重 30 / 24 / 20 / 18 / 14 / 13 / 10）**

| 物品 | id | 单价 | 类内权重 | 类内占比 |
|---|---|---:|---:|---:|
| 劣质小口径手枪弹 | `small_pistol_ammo_nl` | 14 | 30 | 23.3% |
| 小口径手枪弹 | `small_pistol_ammo` | 20 | 24 | 18.6% |
| 劣质大口径手枪弹 | `heavy_pistol_ammo_nl` | 24 | 20 | 15.5% |
| 优质小口径手枪弹 | `small_pistol_ammo_p` | 26 | 18 | 14.0% |
| 大口径手枪弹 | `heavy_pistol_ammo` | 30 | 14 | 10.9% |
| 蚀刻弹 | `lou_etched_round` | 30 | 13 | 10.1% |
| 优质大口径手枪弹 | `heavy_pistol_ammo_p` | 36 | 10 | 7.8% |

**配件（废武器堆，权重 40–6，按单价递减）**

| 物品 | id | 单价 | 类内权重 |
|---|---|---:|---:|
| .22 弹匣 / 简易消音器 | `mag_22` / `silencer_makeshift` | 15 | 40 |
| 玩具激光笔 | `laser_toy` | 18 | 33 |
| 改良内部弹簧组 | `internal_springs_improved` | 20 | 30 |
| 大容量 .22 弹匣 / 简易红点 | `mag_22_high` / `rds_makeshift` | 25 | 24 |
| 10mm 弹匣 | `mag_10mm` | 30 | 20 |
| 短枪托 / 加长 .22 弹匣 | `short_stock` / `mag_22_ext` | 35 | 17 |
| 握把 / 内部弹簧 / 两脚架 / 大容量 10mm 弹匣 | `grip` / `internal_springs` / `bipod` / `mag_10mm_high` | 40 | 15 |
| 加长 10mm / 补偿器 / 激光 / 扳机组 | `mag_10mm_ext` / `compensator` / `laser` / `trigger_group` | 50 | 12 |
| 其余中高价件 | `mag_10mm_smg` … `laser2` | 55–100 | 11–6 |

**枪支（废武器堆，权重 40 / 22 / 14 / 9 / 6 / 4 / 3 / 2）**

| 物品 | id | 单价 | 类内权重 | 类内占比 |
|---|---|---:|---:|---:|
| 3D 打印枪 | `printed_gun` | 40 | 40 | 40.0% |
| 土制手枪 | `handmade_pistol` | 120 | 22 | 22.0% |
| 重型土制手枪 | `heavy_handmade_pistol` | 160 | 14 | 14.0% |
| 格洛克机匣 | `glock_receiver` | 200 | 9 | 9.0% |
| 兰斯维尔左轮手枪 | `revolver` | 240 | 6 | 6.0% |
| “脉冲卫士”电击枪 | `stun_gun` | 250 | 4 | 4.0% |
| “和平使者”重型冲锋枪 | `smg` | 280 | 3 | 3.0% |
| “壁垒”双动式霰弹枪 | `shotgun` | 310 | 2 | 2.0% |

子弹与配件名义上各 5%，实际把 20 份从子弹挪给配件（480 / 520），让配件总量略高于子弹。**子弹保持单件**：`GameItem` 虽然有 `unitCount` / `SetStack`，但 `MayStack` 依赖可空的 `mayStackItemFunc`，弹药工厂（`SmallPistolRound` / `HeavyPistolRound`）没有设置它，自动并堆不成立；强行改数量还会让声望用的 `GetCurrentValue` 与交易用的 `GetValue` 口径不一致，所以不做「一小把」。

**枪械成色**：游戏里没有耐久度字段，也不是靠不同 item id；枪的成色是物品特征 `ItemFeatureList.EquipmentCondition`（New / Excellent / Good / Fair / Poor，对应成交价修正 +25% / +12% / 0 / −12% / −25%）。0.3.61 起废武器堆的枪在生成后按权重重掷成色并覆盖工厂默认值，以破旧为主：

| 枪支 | 全新 | 优良 | 良好 | 一般 | 破旧 | 成色期望系数 |
|---|---:|---:|---:|---:|---:|---:|
| `printed_gun` | 1 | 1 | 9 | 24 | 65 | 0.81 |
| `handmade_pistol` | 1 | 1 | 10 | 30 | 58 | 0.82 |
| `heavy_handmade_pistol` | 1 | 2 | 13 | 34 | 50 | 0.84 |
| `glock_receiver` | 2 | 4 | 22 | 34 | 38 | 0.87 |
| `revolver` | 2 | 4 | 21 | 33 | 40 | 0.87 |
| `stun_gun` | 3 | 4 | 23 | 35 | 35 | 0.88 |
| `smg` | 2 | 4 | 20 | 34 | 40 | 0.87 |
| `shotgun` | 2 | 4 | 20 | 32 | 42 | 0.87 |

### 贵重品池（0.3.62 建池，0.3.64 去超科技，0.3.65 加免疫宁正品与 100–200 普货，0.3.66 加烟与反应器）

「贵重品」是本模组自己拼的一池稀有物品，和游戏自带的「奢侈品 / `LUXURY_ITEM`」类别不是一回事：那个是买卖与展览用的奢侈品标签，本池只是拾荒时按概率抽取的物品清单。它们不进基础表，只有贵重品出现率大于 0 且声望达到 80 时才进最终表，概率取「贵重品出现率」滑条（默认 3%），五个区都能掉到。

| 分类 | 物品 | id | 单价 |
|---|---|---|---:|
| 原有 7 件 | 一袋“蓝血” | `blue_blood_bag` | 250 |
| 原有 7 件 | 高级滤水器 | `water_filter_adv` | 350 |
| 原有 7 件 | 海德拉科技微型净水器 | `portable_water_purifier` | 325 |
| 原有 7 件 | 瓶装泉水 | `bottled_water_premium` | 325 |
| 原有 7 件 | 赛纳普斯注射器 | `black_injector` | 350 |
| 原有 7 件 | 电子元件（稀有） | `rare_electronic` | 350 |
| 原有 7 件 | 模组提取器（高级） | `module_extractor_advanced` | 350 |
| 芯片类 | 3D打印机芯片（.22手枪） | `printer_chip_pistol_double` | 400 |
| 芯片类 | 3D打印机芯片（“铁铸10”手枪框架） | `printer_chip_heavy_handmade_pistol_frame` | 450 |
| 芯片类 | 3D打印机芯片（战斗砍刀） | `printer_chip_combat_machete` | 480 |
| 芯片类 | 3D打印机芯片（枪械零件） | `printer_chip_gun_part` | 300 |
| 芯片类 | 3D打印机芯片（加长10毫米弹匣） | `printer_chip_10_mag_ext` | 400 |
| 贵重枪支 | 兰斯维尔左轮手枪 | `revolver` | 240 |
| 贵重枪支 | “脉冲卫士”电击枪 | `stun_gun` | 250 |
| 贵重枪支 | “和平使者”重型冲锋枪 | `smg` | 280 |
| 贵重枪支 | “壁垒”双动式霰弹枪 | `shotgun` | 310 |
| 贵重近战 | 战斗砍刀 | `combat_machete` | 185 |
| 贵重近战 | 眩晕警棍 | `stun_baton` | 165 |
| 高价材料与工具 | 助熔剂（高级） | `advanced_flux_agent` | 300 |
| 高价材料与工具 | 便携式X射线扫描仪 | `xray_scanner` | 400 |
| 0.3.65 新增：正品免疫宁 | 免疫宁注射器（正品） | `large_purple_injector` | 160 |
| 0.3.65 新增：100–200 普货 | 哨兵3型 | `glock_receiver` | 200 |
| 0.3.65 新增：100–200 普货 | “铁铸10”手枪 | `heavy_handmade_pistol` | 160 |
| 0.3.65 新增：100–200 普货 | 外科手术工具套装 | `surgery_tool` | 200 |
| 0.3.65 新增：100–200 普货 | 模组提取器（基础） | `module_extractor` | 100 |
| 0.3.66 新增：烟 | 一包海明威香烟（正品） | `packet_red_cigarette` | 80 |
| 0.3.66 新增：酿酒 | 酵母生物反应器 | `wine_yeast_infinite` | 250 |

**0.3.66 的烟酒线**：生活废料带与拆解堆加了下面这些普通物品，成品酒（玩家酿的六档品质）**不**进垃圾堆：

| 物品 | 价格 | 位置（权重） | 说明 |
|---|---:|---|---|
| 一包海明威香烟（仿品） | 40 | 生活废料带·日用（8） | 补 `CigaretteCounterfeit()` 并写 `unitValue` |
| 一包海明威香烟（正品） | 80 | 贵重品池（生活 3） | 补 `CigaretteAuthenticity()` |
| 小瓶酒精 | ≈25 | 生活废料带·食品（3） | `PreBuildChemHelper.CreateMiniAlcoholBottle()` 现做 |
| 食品券（仿品） | 3 | 拆解堆·杂项（3） | 外观像真的、兑不了；正品券不进垃圾堆 |
| 酒瓶 | 12 | 生活废料带·空瓶（与空啤酒瓶 3:1） | 空的，给玩家自酿用 |
| 荧光莓 | 14 | 生活废料带·日用（10） | 酿酒原料 |
| 酵母 | 10 | 生活废料带·日用（12） | 酿酒原料 |
| 合成酵母 | 8 | 生活废料带·日用（15） | 红魔鬼同厂 |
| 超级酵母 | 20 | 生活废料带·日用（5） | 酿酒原料 |
| 酵母生物反应器 | 250 | 贵重品池（拆解 2） | 酿酒线的高价件 |

香烟的真伪条件 `modValue` 同样是 0，所以生成后自己写 `unitValue`（正 80 / 仿 40），做法与免疫宁一致；仿品券走 `StampFakeAuthenticity()`，只值 3。

**免疫宁的四种状态（0.3.65）**：游戏里正品 / 仿品 / 过期 / 不能用共用同一个 item id `large_purple_injector`，靠 `CATEGORY_GENUINE_INJECTOR` 条件区分，四档的 `modValueDisplay` 分别是 0 / −75% / −50% / −90%。这四项条件的 `modValue` 都是 0（游戏本身不按状态改价），所以本 mod 在生成后自己补条件并把 `unitValue` 同步成对应价格：

| 状态 | 价格 | 归属 |
|---|---:|---|
| 正品 | 160 | 贵重品（只从贵重品槽位出现） |
| 过期 | 80 | 普通表（生活废料带 / 医疗废弃点的医疗类） |
| 仿品 | 40 | 同上，生成时按 40% / 35% / 25% 掷 |
| 不能用 | 16 | 同上 |

普通表里放的是虚拟 id `large_purple_injector_bad`，平均 50（80×0.4 + 40×0.35 + 16×0.25），告示板显示为「免疫宁注射器（过期/仿品/不能用）」。

**100–200 的「大货 / 普货」**：上面四件 100–200 的物品在普通表里本来就是各自区的大货（哨兵3型、铁铸10、手术工具、模组提取器），0.3.65 起同时以低权重进入贵重品池，所以同名的两条会同时出现在告示板的不同分组里——普通分组那条是普通掉落，贵重品分组那条是 80 档的额外抽取。

**0.3.64 起池子里不再有超科技（hypertech）物品。** 原本在池里的 `hypertech_electronic`（电子元件（超科技），单价 2500）已移除；游戏内其余带「超科技」字样的物品（`hypertech_power_cell`、`turbo_booster_hypertech`、`hypertech_bay`）本来就不在池里。它是池里唯一的超科技件，单价又是其余物品（165–480）的 5–15 倍，掉一次就盖过整池，与「一小批 240–480 的稀有件」的定位不符。

芯片是现成物品：`ModuleDirectory` 注册了 24 个 `printer_chip_*`，单价 100–480。这里只取偏贵的 5 个（≥300），避免和材料类里的普通打印件重复。芯片的显示名自带 `×{0}` 计数占位符，由游戏在生成时填。

**各区有没有差分**：池子本身五区共用同一份 27 件清单，没有「某区独有」或「某区不掉」的条目；差别在**池内权重**（1–5），所以每区抽到某件的相对概率不同，池内均价也不同（入口 275.7 / 拆解 288.1 / 生活 272.2 / 医疗 265.3 / 废武器 287.6）：

| 偏向 | 权重更高的区 | 说明 |
|---|---|---|
| 净水 / 蓝血 / 注射器 | 生活废料带、医疗废弃点 | 医疗与日用倾向 |
| 芯片类 | 拆解堆 | 拆解堆偏芯片与材料 |
| 贵重枪支 | 废武器堆 | 武器区偏枪 |
| 近战 | 拆解堆、废武器堆、医疗废弃点 | 便宜贵重件补位 |

## 告示板（0.3.63 改版）

地图上每个地标下方有一块「告示板」按钮，打开的是该区「原版 → 现在」的分布清单。0.3.63 起不再按单品平铺，改为**按大类分组**：

**分组结构**

- 按本区主题表的大类顺序，一个大类一个分组；组头是「大类名 · 该组小计」，组内两列列出子项名与各自概率。
- 原版表里有、本区主题表没有的物品（低专精时会混进来的那批）统一收在「其他（低专精混入的原版物品）」一组。
- 贵重品单独成组「贵重品（80 档 + 贵重品出现率）」，组内是 27 件贵重品；80 档之前这 27 件的概率全是 0，所以整组直接隐藏，不占版面。同名的普通大货（0.3.65 的 100–200 四件）会同时出现在自己的大类分组里，两条概率分别取各自槽位。
- 某区没有的大类**不会生成分组**（例如入口空地没有子弹类，就没有这一组），不是显示为 0。

**每组显示的字段**

| 位置 | 内容 |
|---|---|
| 组头 | 大类名 + 该组所有子项概率之和（实时小计） |
| 组内每一格 | 子项名 + 该子项当前生效概率 |

**四行动态概率（「档位加成」）**

滚动区底部另有一块「档位加成」，四项按声望档位出现，值全部取自实际结算用的同一套数据：

| 行 | 显示条件 | 取值来源 |
|---|---|---|
| 大奖 | 声望 ≥ 60（0.3.67 起五区都有样板，恒显示） | `StashFind.CurrentChancePercent`：五区共用的当前 AIMD 窗口值（上限由 stash_cap 定，不到 60 时压到 0.1%） |
| 小奖 | 声望 ≥ 60 且本区有小奖整箱 | `Tuning.EffectiveMinorPercent`：`minor_percent` 经 60 档门控（不到 60 按 1/10） |
| 物资箱 | 声望 ≥ 80 且该区有箱子、概率大于 0 | 生效表里该箱子的实际概率（从普通物品份额里扣） |
| 钥匙卡 | 声望 ≥ 80 且该区有卡、概率大于 0 | 生效表里该卡的实际概率 |

没到对应档位、或本区根本没有这项内容时，**整行隐藏**（`SetVisible(false)`，布局组会重排、不留空格），不会显示 0。目前大奖只有生活废料带有样板，小奖只有拆解堆 / 生活废料带 / 医疗废弃点 / 废武器堆有整箱，补给箱与钥匙卡只有这四个区有，入口空地四项都没有。

**实时性与版式**

- 标签只在开窗时建一次，之后只改文本。改专精档位、垃圾倍率、贵重品出现率、补给箱 / 钥匙卡概率、小奖概率或大奖上限，都会经 `Tuning.Invalidated()` → `Picker.RefreshBoardIfOpen()` 就地重算，窗口不重建。
- 内容区是滚动区（`BeginScroll(300)`），五区不同的大类数量、贵重品 21 行都靠滚动容纳，不溢出窗口。
- 窗口底部单独一行「本区期望价值」，与设置窗口里的 EV 口径一致。
- 概率格式：≥0.1% 走常规百分比；不足 0.1% 给两位小数；恰好 0 显示 `0.0%`（表示当前档位下这件物品不出现）。
- 夜间事件（垃圾事件）在运行时临时抬高「垃圾」，其余物品按比例让位；告示板读的是同一张生效表，所以也会跟着变。

## 兼容性

区域只在玩家确认后，作用于本次原版垃圾场的普通掉落组 `dumpingGroundTG` 中「主掉落」的那一次抽取。受伤、空手、拾荒次数、背包发现及工具额外掉落由原玩法和原有 Mod 照常结算。`ModHook.OnLootRolled` 仍会以对应的原版表名触发。

旧配置文件里的 `theme` 条目不再读取，也不会显示；留在配置里无副作用。

0.3.7 起，旧的四条 `focus_gate / focus_scrap / focus_refuse / focus_terminal` 同样不再读取，改为单一 `specialty` 条目；旧值留在配置文件里无副作用。0.3.8 只把这条设置的中文名改为「区域专精度」，配置键仍是 `specialty`，已保存的数值继续生效。

0.3.10 删除贵重品保底后，旧配置里的 `high_pity` 条目不再定义、不再读取、也不再显示；旧值留在配置文件里无副作用，不清理也不影响垃圾倍率、贵重品出现率与区域专精度三项。若曾在模组设置里拖动过保底滑条，升级后该行直接消失，其余设置保持原值。

0.3.12 的旧值处理：

- `specialty` 键沿用，但类型从 0–100 的数字改为四档名称。启动时若读到旧的数字，会按「就近取档」自动换算并立即写回档位名：0–37 → 较少，38–62 → 中等，63–87 → 较多，88–100 → 全部（即 0 → 25%、50 → 50%、75 → 75%、100 → 100%）。转换只做一次，之后读到的就是档位名。
- 旧的 `junk_percent` 若大于 100（原范围上限 200），读取时会被夹到新的上限 100，不会报错；50 及以下的旧值原样保留。默认值从 100 变为 50，只影响从未改过这一项的新配置。
- 其它键与数值不受影响。

编译：在 PowerShell 运行 `./build.ps1`，生成 `dist/`。运行 `./build.ps1 -Install` 同时放进本地 `Mods/DumpingGroundMap/`。底图已包含；改图后可用 Pillow 运行 `python draw_map.py` 重新生成。

## 版本与提交约定

本目录是一个独立的 Git 仓库（首个提交为 0.3.9，之前的历史已被覆盖，无法回溯，不要伪造）。`dist/` 是构建产物，不入库。

- **一次改动一个提交**：一个功能、一个修复、一次数值调整各提交一次，不要混在一起；提交前先跑 `./build.ps1` 确认能编译。
- **首行格式**：`<版本号> <类型>: <一句话主题>`，例如 `0.3.12 feat: 区域专精度改为四档下拉`。类型用 `feat` / `fix` / `tune` / `docs` / `build`。
- **正文写原因**：说明为什么改、影响哪些设置项或数值、验证方式；纯文案改动也要写一句原因。
- **版本号**：每次要发布给游戏用的构建递增 `manifest.xml` 里的 `ModVersion`，并在最后一个提交打 `vX.Y.Z` 标签（如 `v0.3.12`）。同一版本内的多次未发布提交可共用当前版本号。
- **发布流程**：改版本 → `./build.ps1` → 提交 → `git tag vX.Y.Z` → 关闭游戏后部署到 `Mods/DumpingGroundMap/`。
- 掉落权重与收益的详细推导在仓库内的 [`docs/掉落权重与期望收益.md`](docs/掉落权重与期望收益.md)，改数值时与本 README 的表格一起改。
