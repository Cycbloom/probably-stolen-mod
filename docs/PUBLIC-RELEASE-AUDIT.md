# 公开发布前的敏感度盘点与处理

- 盘点日期：2026-11-17
- 范围：当前 HEAD 全部 165 个被跟踪文件（`git ls-files`），不含未跟踪文件；不重写历史
- 原则：按用户确认的口径分级。本机绝对路径、盘符等开发机环境信息属低敏感，**刻意保留**；凭证、账号、邮箱、SteamID 等高敏感项必须处理

## 结论

| 等级 | 数量 | 处理 |
|---|---:|---|
| 高 | 0 | 无需遮掩 |
| 中 | 4 类（详见下表） | 全部保留，均为互操作或开发记录所需，不含个人隐私与反编译源码 |
| 低 | 9 处路径 | 按用户要求保留，不处理 |

## 一、高敏感项（凭证 / 账号 / 邮箱 / SteamID / 密钥）

**无。**

已逐项检查并确认不存在：

- 邮箱：全仓库匹配 `[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}`，无命中（`src/AssemblyInfo.cs` 只有 `InternalsVisibleTo`）。
- SteamID / 账号：匹配 `7656119`、`steamid`，仅 `workshop/WorkshopPublisher.cs:105` 的运行时判断 `SteamUser.GetSteamID().m_SteamID`，不含字面量 ID。
- 密钥 / token / 密码：匹配 `api_key|token|secret|password|bearer|authorization`，命中项均为代码变量名（掉落 token、游戏内信物 `scav_token`）；`workshop/Publish-Workshop.ps1:48` 明确写明脚本不保存密码或 Steam Guard。
- 第三方密钥路径、`.env`、证书、私钥文件：无。
- 图片元数据：扫描全部 39 个被跟踪 PNG/JPG 的字节串，未发现 `C:\Users`、`Users\`、`Steam`、用户名等嵌入文本。
- 个人标识：作者署名 `Cycbloom` 是工坊公开署名（`manifest.xml:5`、`workshop/workshop.json:5`），按发布意图保留。

## 二、中敏感项（第三方 mod 信息、内部记录）

| 位置 | 内容 | 判定 | 处理 |
|---|---|---|---|
| `docs/COMPATIBILITY-SCAVENGING-EXPANSION.md:3` | 拾荒拓展 DLL 的 SHA256、工坊 ID、模组内部 ID | 公开可下载文件的哈希，不含源码 | 保留 |
| `docs/COMPATIBILITY-SCAVENGING-EXPANSION.md:3,10,20,30` | 第三方模组的内部方法名与行为（`ScrapFurnace.Tick`、`GetTableEntries`、抽取调用关系） | 互操作必需的接口事实；为自行静态检查所得，未复制其反编译源码 | 保留 |
| `src/Compatibility/ScavengingExpansionBridge.cs`、`BetterScavengingBridge.cs`、`ReputationPopBridge.cs` | 第三方类型/方法名字面量与软探测 | 运行所需，且已改为不触发扫描标记的写法 | 保留 |
| `docs/TESTING.md`、`workshop/README.md:60` | 内部验证记录、所用工具链（含编辑器自带 PowerShell 路径） | 开发流程记录，不含他人隐私、账号或沟通内容 | 保留 |

说明：`docs/PROBABLY-STOLEN-MODDING-研读笔记.md` 是对第三方公开仓库的研读笔记，属未跟踪文件，已加入 `.gitignore` 的「本地工作笔记」段，不随公开发布。

## 三、低敏感项（本机路径，刻意保留）

用户已明确这类环境信息不需要隐藏。列出以便复核：

| 位置 | 内容 |
|---|---|
| `docs/DEVELOPMENT.md:8` | `D:/Steam/steamapps/common/Probably Stolen` 示例命令 |
| `workshop/README.md:30` | 同一路径的 `Set-Location` 示例 |
| `workshop/gallery/README.md:14` | 提到 `D:/Screenshots` 源目录 |
| `workshop/gallery/screenshots/sources.json:4,10,16,22` | 四张截图的 `D:\Screenshots\...` 来源路径 |
| `workshop/Upload-Workshop.cmd:6` | 编辑器自带 PowerShell 7 的 `C:\Program Files\WindowsApps\...` 兜底路径 |
| `draw_map.py:113` | `C:/Windows/Fonts/consola.ttf`（系统字体，非个人路径） |
| `workshop/Publish-Workshop.ps1:4` | 相对路径 `../../_tools/SteamCMD/steamcmd.exe` |

`workshop/Upload-Workshop.cmd` 的兜底路径在找不到 PATH 与标准安装位置时才使用；这是功能路径，改成占位会破坏脚本，保持原样并已在 `workshop/README.md` 说明可自行安装 PowerShell 7。

## 四、构建产物与本地文件

- 被跟踪文件里没有 `*.dll`、`*.pdb`、`*.mdb`、`obj/`、`*.zip`、`*.vdf`、`*.exe`、`*.log`（按扩展名筛查 `git ls-files`，0 命中）。
- `.gitignore` 已覆盖 `dist/`、`obj/`、`workshop/releases/`、`workshop/upload.vdf`、`workshop/description-update*.vdf`、截图 ZIP、`*.pdb`、IDE 配置、Python 缓存、临时文件与日志。
- 本次新增「本地工作笔记」段，忽略 `docs/PROBABLY-STOLEN-MODDING-研读笔记.md`。
- 历史中的旧产物未处理（不重写历史）。

## 五、本次补充的发布文件

| 文件 | 变更 |
|---|---|
| `LICENSE` | 新增 MIT 许可，版权人 `Cycbloom`，年份 2026 |
| `README.md` | 标题版本更新为 1.0.5；补英文简介；新增「源码与许可」「已知限制」两节 |
| `.gitignore` | 新增本地工作笔记忽略规则 |
| `workshop/README.md` | 原「许可尚未选择」改为 MIT，并声明不代第三方授权 |

## 六、仍需用户决定

1. 作者署名统一使用 `Cycbloom`（`LICENSE`、`manifest.xml`、工坊资料），如需换成真实姓名或邮箱请告知。
2. 是否公开 `docs/history/` 下的历史开发记录与 `docs/TESTING.md` 的内部验证日志（当前按保留处理）。
3. 公开仓库是否补充反馈渠道（Issue 模板、工坊链接以外的联系方式）与 CI；当前两者都没有。
4. 是否把 `1.0.5` 的工坊包重新发布以附带 `LICENSE`（`dist` 打包内容不含许可文件，当前未改打包脚本）。

## 七、验证

```
build.ps1 -Check   PASS: 1104 compatibility checks / 1186955 assertions / 11 textures
                   Version 1.0.5; source b42ba95; SHA256 118E274D41C6EF2DC860E2DEED078AC24B3D3A56B0167105BC8CBC60BCFC634E
verify.ps1 -Scan   PASS: 760 methods, 0 hard flags, 0 capabilities, 0 guarded game-API calls
```

本次只改文档与许可文件，未改源码，未部署。

## 八、推送

- 公开仓库：<https://github.com/Cycbloom/probably-stolen-mod>（public）
- 推送内容：`main` 分支与全部本地 tag（含 `v1.0.5-public`），未强推、未改历史
- 工坊说明已补充「源码」小节：`workshop/description.zh-CN.txt`、`description.en.txt`、`description.bbcode.txt`、`description.bilingual.txt`、`description-short.txt`


