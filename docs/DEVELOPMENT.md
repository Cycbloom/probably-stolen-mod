# 构建与发布

Windows 环境需要游戏 Managed DLL、.NET Framework 4.7.2 引用程序集及系统 csc。运行游戏扫描器推荐 PowerShell 7。绘制底图时另需 Python/Pillow；普通构建直接复制已提交的 PNG。

```powershell
./build.ps1 -Check
./tools/verify.ps1 -Scan
./build.ps1 -GameRoot 'D:/Steam/steamapps/common/Probably Stolen' -Check -Install
```

源码自动收集自 src/**/*.cs，测试不会编进 Mod。编译先写 obj/release，规则检查成功后才更新 dist；部署前检查游戏已关闭。发布内容为 DLL、清单与 Textures/Paper（源码素材目录为 Paper）。ModLoader.LoadTextures 先检查 Textures 目录，再从其中的 Paper 子目录载入，不能把 Paper 直接放在模组根目录。构建输出版本、Git 提交和 DLL SHA-256；程序集 FileVersion/InformationalVersion 来自清单与提交，程序集绑定版本继续保持原来的 0.0.0.0。

游戏 ModScanner 在 Unity 外没有已初始化的游戏程序集目录，因此检查工具从实际 Managed 目录建立引用白名单。扫描器的 Pending 是受游戏运行时保护的 API 调用，不等于拒绝项；本项目要求拒绝项、未声明能力均为零。

## 提交与版本

- 首行格式 `<版本> <类型>: <主题>`，类型 feat/fix/tune/docs/build；正文写原因、影响范围和验证。
- 每次发布递增 manifest.xml 的 ModVersion；同版的整理与修复可分多次提交。
- 先运行构建和检查，再提交，在最后一个发布提交打 vX.Y.Z 标签。以该提交重新构建，保证 DLL 的提交标识对应发布提交。
- 关闭游戏后部署。首次重启后到 Mod 管理确认版本；若加载器提示本地文件更新，按它的正常流程处理。
- 当前玩法写 README，变更写 CHANGELOG，调查与旧记录放 docs。历史快照不当作当前规则来源。

## 存档兼容

Core.Id 和 ScenarioId 是持久标识，移动文件时不改。声望仍使用 rep_total_value；大奖仍使用原有六个键。0.3.82 只给全局调试待办索引增加 dgm_debug_pending_run 和 dgm_debug_pending_slot，用来限定保存失败的重试归属。

主菜单明确设置的调试值仍表示「下一次加载/新开一局应用一次」。游戏内保存失败的待办只匹配原 runID 与槽位；加载其它档时保留该待办而不应用。禁用或换档会取消内存里的延迟保存对象。


0.3.83 新增当前存档键 scav_outing_day（受益日期），以及 scav_note_20_given / scav_note_40_given / scav_note_100_given（三张便签的独立收据）。原 scav_medal_given 保留作信物收据；已有信物不回收、不补发。原生服务行 ID 为 local.dumping_ground_map.scavenger_outing，只包含游戏自带 StoreService 字段，保存选择用原生存档机制。
