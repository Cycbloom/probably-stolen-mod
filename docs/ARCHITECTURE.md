# 架构

| 目录/入口 | 职责 |
|---|---|
| src/Core.cs、ModLifecycle.cs | 加载器入口；统一启停、读档、新档顺序与界面刷新订阅 |
| src/Loot | 区域与物品定义、计算输入快照、最终概率表、奖池、物品变体与生成 |
| src/Configuration | 读取与迁移 ModConfig、滑条预览值、配置变更通知、概率缓存接入 |
| src/Progression | 声望、奖励门槛、大奖状态、存档归属与临时状态恢复 |
| src/UI | 地图选择、掉落告示板、设置控件、声望行与 tooltip |
| src/Services | 开局物资、地面尺寸/布局、付费搬运、同行夜间服务 |
| src/Compatibility | 原生掉落事件及可选 Reputation Pop / Better Scavenging / Scavenging Expansion 联动 |
| src/Patches | Harmony 接入点，调用具体服务 |
| tests、tools | 进程内规则检查、游戏扫描器检查；不打进发布包 |

## 掉落流程

```mermaid
flowchart LR
  Config[配置快照] --> Calculator[LootCalculator]
  Catalog[区域表与物品定义] --> Calculator
  Progress[声望门槛] --> Calculator
  Calculator --> Table[EffectiveTable 与 LootSlot]
  Table --> Notice[LootNoticeModel / View]
  Table --> Roll[抽取物品 token]
  Roll --> Event[OnLootRolled 调整真实 ID]
  Event --> Factory[LootFactory 创建与补状态]
```

`LootCalculator.Build` 接收 `LootSettings`、80 档解锁状态和夜间事件垃圾权重倍率，不读取配置、存档或 UI。`LootTableCache` 的输入比较覆盖五个设置值、解锁状态及事件倍率。UI 用 `LootNoticeModel` 将同一份最终槽位投影成分组，不再通过物品 ID 猜测它属于普通池还是贵重品池。

0.3.87 起，Tuning.TableFor 从 dumping_ground 的活动事件读取 junk_chance，先相加再换算为 Max(0, 1 + modifier/100)。LootCalculator 在基础配置与解锁门控之后乘垃圾权重，再统一归一化并计算 EV；抽样直接使用最终 Probs，不再二次乘事件倍率。ModLifecycle 订阅夜间事件开始/结束，刷新已打开的设置预览与告示板；过期事件、读档切换或倍率变化也会在下次读取时自动使缓存失效。

最终表始终保留潜在贵重品、箱子、钥匙卡槽位；未解锁或概率为零时权重为零。这样滑条变化不需要重建窗口，零概率也不会参与抽取。箱子和钥匙卡在告示板单独列出。

`LootFactory` 统一普通物品和奖品的生成、虚拟 ID、真伪、注射器状态与武器成色。只有主掉落经过 `NativeLootEvents`；奖品保持既有的奖励规则，不新增掉落事件。

1.0.1起，ScavengingExpansionBridge通过原生RollInternal接入可选拾荒拓展技能。RegionalRollScope只为当前同步抽取提供本区同类普通物品快照，不改全局表；预选结果消费后停止覆盖，finally恢复父作用域。参见[接入说明和验证边界](COMPATIBILITY-SCAVENGING-EXPANSION.md)。

## 状态与生命周期

拾荒客免扣客流由 ScavengerTrafficRules 统一判断：夜报结算查当天、次日客数查昨天的 afterhourState[dumping_ground].lastVisitDay，并核对开局、有效访问及真实外出标记。GetNormalCustomerCount 仍只在调用期间暂时隐藏 wentToAfterhoursLastNight，保持原生 Clamp 后恢复；夜报只跳过 HandleAfterhourDebuff。读档用同一判据清除原文或原生红色包装的旧误报，不改其它日志、不新增存档字段。clientStack 标有 ES3NonSerializable，读档不恢复顾客队列；20 声望奖励仍在 GenerateClient 的开门链路追加，原有读档 ResetCache 保留。

| 范围 | 状态 | 初始化/清理 |
|---|---|---|
| 全局配置 | 概率、地面、调试开关 | Tuning 与原生 ModConfig |
| 每局 | 声望、大奖窗口、命中日期与新闻日期 | 每次 Loaded 全量读取；NewGame 归零；Disable 清内存 |
| 每趟 | 选区、是否已中大奖 | 进入选择时初始化，离开时清选择，读档/禁用全量重置 |
| 每次调用 | 搜索深度、临时客流标记 | Prefix 建立；Finalizer 清理，恢复动作可重复调用 |
| 每个待保存请求 | 存档对象、runID、槽位 | DeferredSave 入队时绑定，切档/禁用时取消 |
| 每个窗口 | Unity 控件引用 | 窗口关闭/禁用时清理 |

新开局顺序由 `ModLifecycle.NewGame` 明确安排：取消旧保存 → 清理本趟 → 声望归零 → 大奖归零 → 开局初始声望 → 明确排队的调试值。模块不再分别订阅同一个新档事件来隐式决定顺序。

游戏退出菜单时 PlayerStore 可能仍存在，所以热启用只在 Emporium 已存在时同步当前档。正常读档事件则继续在游戏原有时机重读数据。

## 扩展约定

文案放在 `Localization/zh.csv` 与 `Localization/en.csv`，通过 `Copy.Get` 调用官方 `ModHelper.GetLocalized`。静态区域、事件、奖励和便签定义只存键名；Init/OnEnable 早于语言资产加载，取词须延迟到 provider、界面或游戏事件。热启用先恢复存档状态，同行服务文字由随后打开面板的原生钩子同步。设置分组保留稳定键，AddHeader 前缀只翻译本模组的标题；下拉框只修改显示文本，旧配置值及原生按下标应用/重置的流程保留，旧英文地面对齐值在初始化时映射回稳定值。

新增区域时从 Region/ScavLoot 开始，补充贵重品区域权重、箱卡映射与奖品模板，并运行规则检查。新增奖品/变体时同步 LootCatalog 估值与 LootFactory 生成路径。新增门槛与文案写在 PerkCatalog；业务用其常量，界面用其定义。

类型命名空间仍是 DumpingGroundMap；源码目录变动不改变 Harmony 类的类型名。模组 ID、开局 ID、旧存档键和配置键保留。


## 夜间同行与节点便签（0.3.83）

ScavengerOuting.Sync 将一个原生 StoreService 行放进 PlayerStore.storeService。关店面板 InitUI 前、服务费用刷新后和 OnGoingSleepEarly 都重新校正门槛与价格；不足 100 点时清掉选择，防止存档里的旧勾选绕过门槛。原版 EndNight 负责扣费；OnHandlingNightlyServicesEarly 此时位于支付循环之后，记录当天 +1 的受益日期、写晨报并取消勾选，不再自行扣款。

受益日期只存 scav_outing_day，不用静态缓存。在此日期内 GetMaxScavAttempts +2，两个受伤概率显示为零，两个实际受伤掷骰入口也跳过。没有改写原生伤势或永久免疫字段。禁用时移除本服务行，避免原生继续收费；重启用或读档时恢复行定义。

MilestoneNotes 集中维护三个阈值、收据键和便签文案；ScavengerMilestones 在开张前发放。MilestoneDelivery 负责「全部放下才记收据，失败回滚」。旧 scav_medal_given 只代表已收过信物，不代表已收到三个新节点的说明，所以升级后独立补发便签而不重复信物。


## 大奖概率模式（0.3.86）

Tuning 的 stash_window_enabled 是全局设置，默认 true。StashFind 的抽奖与告示板统一通过 JackpotChanceRules.Current 读取当前概率；关闭时直接返回当前生效上限，保存的日窗口只暂停、不清除。中奖的日期、新闻、每趟一次和后续奖励仍按原流程记入存档。0.6.1 起窗口增长改由 JackpotDailyState 按自然日推进，旧的 stash_chance / stash_phase 仍读写以兼容旧档，其中 phase 只作历史字段保留。

固定模式不额外抬高声望不足60时的上限；重新开启时继续原窗口，必要时按当前生效上限夹取。


## 八地点地图与美术（0.4.0）

Picker 保留原来的 Travel 拦截和每趟选区状态；预览区域、检查区域与已选区域分开，鼠标移动不改变目的地。点击仅预选，确认才继续 Travel/Scavenge。锁定区域不能写入 ChosenRegion。

OverviewMap 显示 dumping_ground_map_base 底图，以及 dumping_ground_landmarks 透明图集的八个 RawImage 帧。图集保持原 PNG 和 alpha，通过 uvRect 取帧，不在运行时裁切或创建纹理。MapArt 存放归一化帧与各地标宽度；MapLayout 使用同一尺寸定义有效的建筑和附近地面范围。坐标 Y 向上，Region.X/Y 为地标底部中心。前景地标后绘制；悬停缩放围绕底部发生，不改变命中范围。

MapPointerSurface 是单一透明射线接收层。只在指针进入地图时跟踪鼠标坐标，离开或禁用时清预览；使用 unscaledDeltaTime 平滑动画。X 标记和所有图片/标签不拦截射线。建筑点击把 X 放到该建筑地面锚点，附近地面点击保留原落点；点击围墙/远处空白清空选择。

0.4.0 初版新增八区；0.5.0 解锁改为0/20/40/60每批两区，保留区域顺序、id和存档键。三新区各有独立主表、储藏样板、小奖、箱卡映射、地点措辞和区域场景背景。贵重品受80声望门控，补给箱与钥匙卡受100声望门控。当前无交界处混合掉落。


## 声望基础收益校准（0.5.0）

LootBalance.Stage 将声望夹到0/1/2/3档；Target=12.24*(1+0.1*Stage)。LootCalculator 先混合专精表，再在每个类别内部对原权重乘 exp(slope*expectedValue/50)，以二分法寻找共同斜率并按类别原份额归一化。保持各类别份额，泵站储水类整体冻结内部权重，防止收益求解破坏瓶型钟形分布。垃圾单独固定基准0.5，再乘玩家垃圾倍率，最后沿用夜间事件的垃圾权重归一化。80稀有份额及大小奖不参加目标求解。

校准基于生成状态的期望估值：包装计内容物，枪械计原生成色，海德拉计原生耐久定价，泵站容器按91个水量档和六种水质枚举原生水价与外壳价值；不含市场、工具、议价及第三方替换。锁定区域的概率预览使用其开门声望档位。

LootTableCache 额外以基础声望档位为缓存键。Tuning.TableFor 从当前存档 Reputation.Points 传入；0/20/40/60跨档、降档或切档后会更新，80贵重品门控仍是独立键。BaseEv 固定直接读取未校准的 vanilla 表，避免被新目标反向修改。NativeSettings 仅以已开放区域计算均值。

## 区域日事件（0.6.0）

RegionalEventSchedule纯函数安排周日期；RegionalEvents以当前PlayerStore绑定modData中的周计划与种子，在读档、新档、开日和拾荒声望结算后同步。周日基于原生dayCounter % 7；中途解锁保留已有日期，新区从次日起加入余下容量，周中安装不补发过去日期。DeferredSave保存绑定原存档，禁用和切档清内存。

LootCalculator保持无区域事件的基础表。LootTableCache按区域事件是否活动更新该区表，RegionalEventRules.Apply仅重分配普通槽位；稀有槽位概率保持不变。EffectiveTable保存WithoutRegionalEvent与可选状态估值，MainExpectation将事件项定义为同日有/无区域事件的EV差值。泵站水量只在主掉落工厂显式传入事件标志，奖品沿用旧工厂默认值。

Picker与OverviewMap显示今日标识和详情；NativeSettings从当前控件生成的预览表同样应用活动区域事件；LootNoticeView显示实际槽位概率与四项主掉落期望。入口轻重伤后缀只乘0.8，零值仍为零，与同行免伤相容。

## 大小奖更新周期（0.6.1）

JackpotDailyState管理大奖窗口日期、周期起点、连续未中日与上限。StashFind.SyncDailyWindow在开日、读档、新档和读取大奖概率时幂等推进，日内搜索只记录动态中奖日期，次日统一减半；连续五个未中自然日线性到顶。固定模式只推进日期锚点，不补算暂停日期。上限变化先结算待处理的旧日结果，再以当前概率重建周期。

MinorChanceRules管理独立小奖AIMD：起点为Min(0.3, cap/10)，慢启动翻倍后线性+0.1，中奖减半。StashFind在每次有效OnScavenge末尾只调用一次，大奖占用的搜索视为小奖未中，0上限/无奖池/固定模式不推进。minor_chance与minor_phase随当前存档保存；低声望倍率在读取实际概率时应用。LootNoticeView使用当前概率而非上限，RewardExpectation继续计入大奖优先。预设同步两套窗口开关。
