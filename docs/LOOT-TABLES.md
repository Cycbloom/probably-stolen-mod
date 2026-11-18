# JSON 掉落数据

`LootTables/regions.json` 是垃圾场静态基础数据的唯一维护入口，使用[官方 JSON 掉落表格式](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md#用-json-文件编辑掉落表)，没有自定义 JSON 字段。游戏直接载入其中的 `tables` 与 `groups`；构建器从同一份文件生成启动前的后备数据，不再手工维护另一套 C# 权重。

## 表与分类

- `local.dumping_ground_map.baseline`：53 项原版参考分布，用于专精插值和基准 EV。
- `local.dumping_ground_map.region.<区域>.<词条键>`：39 张区域分类表，条目是物品 ID 与类内相对权重。例如入口材料表为 `local.dumping_ground_map.region.gate.category_materials`。
- `local.dumping_ground_map.region.<区域>`：八张区域表组，条目引用上述分类表，权重决定分类间的相对份额。例如入口的材料类默认 1625，垃圾类默认 5000，全组默认合计 10000。

区域 ID 为 `gate`、`scrap`、`refuse`、`med`、`weapons`、`electronics`、`vehicles`、`water`。分类表 ID 的最后一段直接作为告示板分类的本地化键，如 `category_materials` 或 `event_scrap_goods`，须同时存在于中英文 CSV。分类及物品的排列顺序沿用 JSON 中的顺序。

本模组只注册自有表与表组，不修改原版表，不把自有表加入 `dumpingGroundTG`。其他地点的分布、外来掉落读取的原版组，以及 `OnLootRolled` 现有原版来源表名均保留。

## 修改方式

修改同一分类表中某个条目的 `weight`，会改变其类内初始比例；修改区域表组中分类的 `weight`，会改变分类初始份额。权重是相对值，不要求加起来等于 10000。源文件要求数值为正且有限；移除物品应删除条目，同区不可在多个分类重复列出同一物品。保留垃圾和至少一种非垃圾物品。极端比例可能低于整数快照精度，构建校验会拒绝。

这是基础分布，最终掉落仍经过专精插值、声望的类内 EV 调整、配置开关、区域事件及大小奖规则。垃圾概率仍由 50% 基准和设置/事件计算；贵重品、箱卡及奖品窗口也仍由现有规则控制。因此 JSON 权重不是最终面板百分比，调高单件初始权重后，声望平衡仍可能再次分配类内比例。

部分物品 ID 是本模组已有的半份容器、仿品等内部 token。它们仍由 `LootFactory` / `LootVariants` 转换为真实物品并设置状态；注册为原生表不会自动给这些 token 添加原生生成工厂。可用 `loot-list <表 ID>` 检查原始登记内容，生成验证应走游戏里的垃圾场拾荒流程。

仓库修改后运行 `./build.ps1 -Check -Install`；关闭游戏再安装，重新启动后生效。直接修改安装目录里的 JSON 也需重启，且会被后续部署覆盖；开发时优先改仓库源文件。

## 加载与回退

`Init/OnEnable` 阶段使用构建生成的后备表，不读文件。游戏原生加载器读取 JSON；`ModHook.OnLootTablesLoaded` 在原版表及模组编辑应用后触发，`ScavLoot` 通过官方 `GetLootEntries/GetLootGroupEntries` 读取最终状态，一次构建并原子替换八区及参考表快照。

分类份额先归一为 10000，再按类内权重分配整数，四舍五入差额补给权重最大项。这沿用原先的取整规则；本次默认数据与迁移前逐项一致。参考表保留可表达的原始整数权重，小数权重转换为一百万份精度。

快照变更会更新代次、清理有效概率表及基准 EV 缓存，并重建外来掉落的本模组物品集合。缺表、空组、非有限权重、跨分类重复项等无效数据会输出一条 `[loot]` 警告并整体使用构建时的 JSON 后备表；不会混用上一场景的部分表。停用时恢复后备表并解除事件订阅。运行时代码不自行读取磁盘，不需要文件能力。

进入存档后应看到：

```text
[ModLoader] 垃圾场拓展 / Scrapyard Expansion: 1 loot file(s): 40 table(s), 8 group(s), 0 patch(es) loaded
[垃圾场拓展 / Scrapyard Expansion]: [loot] snapshot ready: regions=8, baseline_entries=53, source=registry
```

第二行的 `source=registry` 表示使用了原生注册表；`source=bundled` 表示发生回退，查找其前面的警告。测试在独立 CLR 中验证游戏 DTO、条目转换和注册表操作日志，原生 Unity JSON 解析与实际文件加载日志仍需游戏内确认。
