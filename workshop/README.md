# 垃圾场拓展 · Steam创意工坊发布

作者：Cycbloom。当前模组版本1.0.1，清单目标游戏版本0.46D。保留内部ID `local.dumping_ground_map`；不因发布改动已有存档标识。

## 已准备的资料

- `description.zh-CN.txt`和`description.en.txt`：扩展后的中文版、英文版介绍，可分别粘贴到工坊对应语言栏。
- `description.bilingual.txt`和`description.bbcode.txt`：中英合并归档备选；发布时使用独立语言文件，均使用真实换行。`description-short.txt`保留为旧版简短备选。
- `title.zh-CN.txt`和`title.en.txt`：对应的中英文标题。
- `assets/preview.png`：以电子垃圾坡场景为底图，加入「垃圾场拓展」「分区拾荒 · 声望成长」和Cycbloom署名的封面，同时放进模组内容作为离线预览。
- `assets/preview.jpg`：小于1MB的上传封面，与PNG内容相同，仅做格式压缩。
- `assets/COVER-PROMPT-DRAFT.txt`：带标题封面的预留提示词。内置imagegen本次长时间未返回，按作者选择改用现有场景原画；此提示词不代表已生成成品。
- `assets/ART-SOURCES.md`：当前封面来源和导出记录。
- `assets/preview-titled.png`及`assets/COVER-PROMPT-TITLED.txt`：带标题封面的原图和内置imagegen编辑提示词。
- `PLAYER-README.txt`：随包的安装、版本、玩法和存档说明。
- `gallery/`：四张以作者实机截图制作的功能展示图，以及三张区域场景原画。上传文件与推荐顺序见`gallery/README.md`；可在工坊网页另行添加。
- `workshop.json`：已发布条目ID3840982832及标题、署名和更新说明，随源码提交。
- `upload.vdf`：生成的本机上传配置，不入库；优先沿用已保存ID，首次新建才使用ID0和隐藏可见性。
- `Upload-Workshop.cmd`：本机上传启动器，自动寻找PowerShell 7，仅为本次进程设置执行策略，解决未签名脚本无法启动的问题。
- `releases/`：按时间生成的独立发布目录，包含内容文件夹、手动安装ZIP和校验记录。
- `latest-release.txt`：最近准备的发布目录路径。

构建/规则检查和ModScanner通过不代表实机验证通过。按用户约定，本次未启动游戏。工坊订阅下载、游戏内启用和表现待作者核对；介绍中已如实说明。

## 重新准备发布包

在PowerShell 7中运行：

```powershell
Set-Location 'D:\Steam\steamapps\common\Probably Stolen\_moddev\DumpingGroundMap\workshop'
.\Prepare-Workshop.ps1
```

脚本构建并运行规则检查和扫描，按明确文件列表打包：本模组DLL、manifest、12张贴图、封面及玩家说明。不会把src、obj、.git、备份、个人配置、游戏DLL或旧根目录Paper带入上传目录。每次生成新目录，不清空旧发布包。记录每个内容文件的SHA-256。

## 已有条目的推荐发布方式

Steam客户端登录作者账号后，使用游戏自带Steamworks.NET接口上传，无需再次输入账号密码：

```powershell
.\Prepare-Workshop.ps1
.\Update-WorkshopMetadata.ps1 -Mode Publish
```

Publish先校验干净发布包的文件列表及SHA-256，再上传模组内容、封面、分类标签，以及分别设置为english和schinese的标题与介绍。两次提交都确认EResultOK后，从Steam禁用缓存读回两种语言，逐字核对文案与标签，并核对可见性未改变。每次发布前后的快照和回执保存在release的steam-client-receipt目录。不会启动游戏或修改本地安装。上传前检查当前账号确为该工坊条目的作者。

只查询，不写入：`.\Update-WorkshopMetadata.ps1 -Mode Query`。
只更新两种语言的标题、介绍和分类标签，不上传文件：`.\Update-WorkshopMetadata.ps1 -Mode Metadata`。

## 首次隐藏上传

SteamCMD官方工具的说明：[Valve SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD)。使用拥有游戏的Steam账号。在普通交互式终端执行，不把密码写进脚本或聊天：

在当前workshop文件夹中运行：

```powershell
.\Upload-Workshop.cmd -Account '你的Steam登录名'
```

也可以双击`Upload-Workshop.cmd`，随后输入Account（Steam登录名）。启动器优先使用PATH或标准安装位置中的PowerShell 7；本机未独立安装时使用Codex附带的PowerShell 7。`-ExecutionPolicy Bypass`只传给新启动的进程，不修改CurrentUser或LocalMachine设置，不需要管理员权限。如果Codex更新后内置路径变动，安装标准PowerShell 7或更新启动器路径即可。

仅检查启动与发布包、不登录和上传：

```powershell
.\Upload-Workshop.cmd -CheckOnly -Account '你的Steam登录名'
```

SteamCMD自行提示密码或Steam Guard验证。脚本会在上传前核对内容文件列表及哈希；不会自动启动游戏。上传后打开脚本打印的工坊链接，按Steam提示接受工坊协议，并查看上传日志确认内容提交成功。`upload.vdf`中有条目ID不等于这次内容一定上传成功。

新建条目默认隐藏；已有条目的更新不提交visibility字段，保留工坊网页当前的可见性。你确认内容后可在工坊网页设置公开。展示图片需在工坊网页单独添加，SteamCMD上传配置只指定封面。

自己订阅核对时，先将游戏Mods目录内的本地DumpingGroundMap文件夹移到Mods以外的备份位置，再启动游戏启用工坊副本。本地同ID副本会优先于工坊版；不移开就可能一直在核对本地版。不要直接删除备份。

## 后续更新

描述使用实际换行，不要把换行替换成字面字符`\n`。保留原条目ID，上传时按独立语言文件写入。仅修改本地文件不会立即修改网页。

只修改介绍时，在原工坊条目进入「编辑标题与描述」即可，无需重传DLL或增加ModVersion。语言选择简体中文，复制description.zh-CN.txt并保存；再选择英语，复制description.en.txt并保存，标题也有对应文件。也可只用description.bilingual.txt作为单栏的中英对照介绍。现有条目ID为3840982832，继续沿用，不要改成0或创建新条目。

现有条目的SteamCMD配置不再包含title/description字段，以免覆盖已维护的语言栏。新建条目使用英文初始文案；后续通过Update-WorkshopMetadata.ps1分别更新english和schinese。标签在workshop.json的tags中维护。

也可以通过命令只更新描述。在workshop目录运行：

```powershell
.\Upload-Workshop.cmd -DescriptionOnly -Account '你的Steam登录名'
```

DescriptionOnly模式转交Update-WorkshopMetadata.ps1 -Mode Metadata，读取独立中英文标题及介绍，分别上传并读回核验，同时同步tags；不重传模组文件、封面，也不设置可见性。需Steam客户端已登录作者账号。此模式不再写入中英混排介绍。

只检查这个命令，不登录或上传：

```powershell
.\Upload-Workshop.cmd -CheckOnly -DescriptionOnly -Account '你的Steam登录名'
```

修改代码后递增manifest的版本，并更新`workshop.json`的更新说明、玩家说明及介绍中的版本文字；运行Prepare脚本，再使用同一上传脚本。Prepare会读取并保留现有`upload.vdf`的`publishedfileid`，沿用原条目。不要将已发布的ID改回0，否则会创建新条目。

upload.vdf不存在时，Prepare从已提交的workshop.json读取publishedFileId；两者不一致会停止，避免更新错条目。生成的VDF、ZIP、latest路径指针和releases目录不入库；封面、截图原件与最终展示图、脚本和文案均入库。

SteamCMD回填后的VDF还会备份到对应release目录。丢失主VDF时，先从`upload-after-steamcmd.vdf`恢复，再准备下一版。

## 资料来源

- [开发者审核的游戏发布说明](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md#steam-创意工坊)
- [Valve的SteamCMD工坊接口说明](https://partner.steamgames.com/doc/features/workshop/implementation#SteamCmd_Integration)
- [游戏模组社区规范](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md)

代码和素材的开源/再分发许可尚未另行选择；本包不代作者指定开源协议，也不把其他模组或游戏的DLL一起分发。
