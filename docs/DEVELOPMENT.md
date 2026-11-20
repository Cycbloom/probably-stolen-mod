# 构建与发布

Windows 环境需要游戏 Managed DLL、.NET Framework 4.7.2 引用程序集及系统 csc。运行游戏扫描器推荐 PowerShell 7。绘制底图时另需 Python/Pillow；普通构建直接复制已提交的 PNG。

```powershell
./build.ps1 -Check
./tools/verify.ps1 -Scan
./build.ps1 -GameRoot 'D:/Steam/steamapps/common/Probably Stolen' -Check -Install
```

源码自动收集自 src/**/*.cs，测试不会编进 Mod。编译先写 obj/release，规则检查成功后才更新 dist；部署前检查游戏已关闭。发布内容为 DLL、清单、LICENSE、Localization 中的中英文 CSV 与 Textures/Paper（源码素材目录为 Paper）。ModLoader.LoadTextures 先检查 Textures 目录，再从其中的 Paper 子目录载入，不能把 Paper 直接放在模组根目录。构建输出版本、Git 提交和 DLL SHA-256；程序集 FileVersion/InformationalVersion 来自清单与提交，程序集绑定版本继续保持原来的 0.0.0.0。

游戏 ModScanner 在 Unity 外没有已初始化的游戏程序集目录，因此检查工具从实际 Managed 目录建立引用白名单。扫描器的 Pending 是受游戏运行时保护的 API 调用，不等于拒绝项；本项目要求拒绝项、未声明能力均为零。

## 提交与版本

- 用户要求：每次完成 MOD 修改并通过适用检查后，自动完成本地 Git 提交，并在汇报中提供提交号。只暂存当前任务的改动；原有无关改动和构建产物保留在提交之外。
- 首行格式 `<版本> <类型>: <主题>`，类型 feat/fix/tune/docs/build；正文写原因、影响范围和验证。
- 每次发布递增 manifest.xml 的 ModVersion；同版的整理与修复可分多次提交。
- 先运行构建和检查，再提交，在最后一个发布提交打 vX.Y.Z 标签。以该提交重新构建，保证 DLL 的提交标识对应发布提交。
- 关闭游戏后部署。首次重启后到 Mod 管理确认版本；若加载器提示本地文件更新，按它的正常流程处理。
- 当前玩法写 README，变更写 CHANGELOG，调查与旧记录放 docs。历史快照不当作当前规则来源。

## 可选地面扩展

原生配置 `ground_enabled` 默认 `false`，归入 `dgm_settings_section_ground`，使用原生布尔控件并标记 `RequiresRestart`。旧配置没有新键时保持关闭，不从已有宽高推断开启；`ground_width` / `ground_height` / `ground_align` 的既有值和尺寸预设均保留，预设不会自动打开扩展开关。此布尔控件由原生设置界面处理，不接入自定义滑条预览。

`GroundExpand.Apply` 与 `Relayout` 是统一入口，先判断模组启用及显式地面开关，再进入操作 Unity 对象的函数。`Relayout` 还要求当前 `EmporiumEntry` 是本模组实际完成扩展时记录的对象，焚化炉联动不再绕过归属判断。`SetupAfterhourInv` 仍清理本模组内存状态；拾荒作用域和溢出通知不依赖地面开关，保持独立。

关闭或改小尺寸时不在运行中的地面上强制缩形、重建或移动物品。设置界面明确要求应用后重启，由原生重新创建地面；第一次升级也需要重启以加载新 DLL。本修复不改 `localScale` 或鼠标到网格的坐标换算，不将默认关闭等同于修复任意第三方缩放。目标是关闭后不再干预对方的地面布局。

## 日志

统一通过 `ModLogger.Info`、`Warning`、`Error` 调用官方 `ModLog`，新代码不要直接调用 `Debug.Log*` 或自行添加 `[DumpingGroundMap]` 前缀。正式前缀来自清单的 `<Name>`；地面 `[DGM-GROUND]`、场景 `[DGM-ART]`、设置 `[DGM-SLIDER]` 等现有分类标记保留，方便沿用筛选方式。

加载器先执行 Harmony.PatchAll，再执行 Core.Init，因此 TargetMethod 中的早期日志使用持久 ID `local.dumping_ground_map`；Core.Init 首先传入真实清单，之后使用正式显示名。无需等待贴图或本地化加载，日志也不通过玩家语言 CSV 翻译。

性能日志继续使用设置里的 `perf_log` 开关，默认关闭；调用点应先检查 `PerfLog.Enabled`，再构造消息或计时，避免关闭时仍产生性能开销。开启后输出到同一个 ModLog，并带 `[perf]` 分类。

## 资源加载完成检查

静态掉落数据另见 [JSON 掉落表维护与加载](LOOT-TABLES.md)：修改 `LootTables/regions.json`，构建时生成后备数据；原生 `OnLootTablesLoaded` 后读取最终注册表快照。该事件与贴图/本地化的资源通知分开处理。

`ModLifecycle` 随启停订阅/取消 `ModHook.OnModAssetsLoaded`，回调交给 `AssetDiagnostics.OnLoaded`。此通知晚于 `Init/OnEnable`，加载器已经完成贴图、声音与本地化加载；回调过滤当前模组 ID 和启用状态，不访问存档、场景或游戏 UI。

检查使用 `LoadedMod.PaperSprites` 与 `Localization` 的内存快照，覆盖四张地图素材及八区背景、中英文语言表缺失/为空、空文本，以及双向键名差异。空的精灵图引用不算成功加载。附加语言不要求与中英文一致，当前也没有必需音频。此运行时检查不替代构建时的词条/占位符和文件哈希校验；它不枚举所有编译代码引用的词条。

结果统一输出为 `[assets]` 日志：按问题种类汇总数量、排序并最多列 5 个示例，最后列出成功载入的贴图数、两种语言各自的词条数和警告条数。发现缺失仅警告，不中断启动、不改写已加载资源；现有地图和语言回退逻辑继续负责显示。

## 原版音效反馈

`SoundFeedback` 只复用游戏已有标识，不需要新增 `Sounds/` 文件或注册自定义音频。选区使用 `click_vintage`，区域解锁使用 `module_insert`，大奖使用 `scanner_activate`；这些标识均已在本机游戏程序集的原生调用中核对。通过 `AudioManager.Play` / `Play3` 播放，沿用游戏的音源、音量和暂停控制，不创建自己的 `AudioSource`。

选区只在有效选择变化后播音，打开地图、悬停、锁区和重复点击保持静默。解锁只从 `Reputation.Add` 取得当前存档加载后的增长前后点数，跨越一个或多个区域门槛时播一次；`Reload`、`SetPoints`、新档和调试队列不调用它。没有额外音效收据或存档迁移：读回旧档后再次真实解锁时可以重新提示。

大奖只在 `TryAwardJackpot` 实际生成至少一件物品、记录中奖后播音；生成失败不消耗本趟机会、不排新闻、不提示中奖，原生新闻入队失败仍保留次日兜底。大奖走音源 3，避免被外层拾荒的音源 2 碎屑音覆盖；同次发奖带来的区域解锁走主音源。确认进入/拾荒的现有按钮不额外叠加音效。

播放前检查 `Core.Enabled` 和 `AudioManager.Exists`，没有音频管理器时不创建它。播放失败只输出 `[sound]` 警告，不中断选区、声望和发奖；正常播放输出 `event=selection` / `region-unlocked` / `jackpot` 及声音标识。沿用游戏音量，无额外 MOD 音量设置。

## F9 只读调试按钮

按官方指南的开发工具约定，在游戏可执行文件旁创建空的 `dev.txt` 并重启；进入一局游戏后按 F9，在 Special 列查找：

- `DGM Check Assets`：重新检查最近一次本模组 `OnModAssetsLoaded` 通知中的对象，并输出 `[assets]` 日志。它检查当前内存字典，不重新扫描磁盘或热加载文件；改了贴图或 CSV 后仍需重启游戏。未收到通知时仅提示 `not ready`。
- `DGM Dump State`：输出 `[debug] cached mod state`，包含资源通知是否已收到、整数声望、累计价值、当前区域 ID 和已解锁区域 ID。`assets_notified=True` 只表示收到过完成通知，资源有无缺失看 `[assets]` 警告。输出只反映本模组当前缓存，不重新读档、不保存、不发奖励，也不改变选区。

按 F8 查看日志，或查阅游戏 `Player.log`。按钮标签使用稳定的英文开发名称：注册发生在本地化资产载入之前，不依赖玩家语言。`DebugTools.Register` 防止重复注册，`Unregister` 只以本模组 ID 移除按钮；禁用同时释放资源引用，保留的回调在停用状态下直接返回。无开发工具的玩家不需要 `dev.txt`；此文件不随模组发布。

官方参考：[添加你自己的调试按钮](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md#添加你自己的调试按钮)。本机游戏已核对 `AddDebugFunction` / `RemoveDebugFunctions` 的接口签名；实际 F9 显示仍需游戏内验证。

## 清单版本声明

`<GameVersions>` 按 `<Version>` 子元素逐条读取：每条去首尾空白、丢弃空项，不支持逗号或分号分隔。匹配时先规范化（只保留数字和点，去掉末尾的点），然后比较相等，或任一方加一个点后作为另一方的前缀；任意一条命中即视为兼容。界面会逐条显示，命中的绿色、未命中的红色；列表非空且一条都不命中时才报版本问题。

因此只写 `<Version>1.0.0</Version>`：它覆盖 1.0 的全部修订（例如 `1.0.0.14` 由 `1.0.0.` 前缀命中），也不替未验证的修订背书。`0.46D` 会规范化成 `0.46`，在 1.0 客户端永不命中，只会多出一条红色标签；加载器也不用这个列表决定是否加载，写它不会让旧版本变得可用。要真正支持旧分支只能维护独立构建。

## 存档兼容

Core.Id 和 ScenarioId 是持久标识，移动文件时不改。声望仍使用 rep_total_value；大奖仍使用原有六个键。0.3.82 只给全局调试待办索引增加 dgm_debug_pending_run 和 dgm_debug_pending_slot，用来限定保存失败的重试归属。

主菜单明确设置的调试值仍表示「下一次加载/新开一局应用一次」。游戏内保存失败的待办只匹配原 runID 与槽位；加载其它档时保留该待办而不应用。禁用或换档会取消内存里的延迟保存对象。


0.3.83 新增当前存档键 scav_outing_day（受益日期），以及 scav_note_20_given / scav_note_40_given / scav_note_100_given（三张便签的独立收据）。原 scav_medal_given 保留作信物收据；已有信物不回收、不补发。原生服务行 ID 为 local.dumping_ground_map.scavenger_outing，只包含游戏自带 StoreService 字段，保存选择用原生存档机制。
