# 工坊图片上传

新增四张实机功能展示图，统一1920×1200。作者选择了本地排版并加简洁功能介绍标题；界面文字、概率、声望和解锁状态保留原图，没有用AI重绘。

## 建议上传顺序

1. `screenshots/upload/01-eight-area-map.jpg`：中文八地点总览，裁去窗口上下多余部分，保留全部地图和当前声望状态。
2. `screenshots/upload/02-reputation-progression.jpg`：突出拾荒客声望栏与原截图中的100声望奖励说明，右侧补充20/40/60、80、100三个阶段的功能说明。
3. `screenshots/upload/03-loot-probability-and-value.jpg`：放大原告示牌截图，展示当前地点的概率与基础、贵重、箱卡、事件和大小奖期望拆分。数值是截图中声望10时的入口空地示例，不表示所有地点、设置的固定收益。
4. `screenshots/upload/04-english-interface.jpg`：保留完整英文选区窗口，显示地图、英文说明与确认按钮。

四张JPG为方便上传的压缩版；`screenshots/png/`是同版式高清PNG，`screenshots/contact-sheet.jpg`是四图预览。`screenshots/latest-archive.txt`记录最新四图ZIP的位置，ZIP内只含四张JPG。

原截图单独存放于`screenshots/originals/`，`sources.json`记录来源及SHA-256。没有修改D:/Screenshots中的源文件。

## 添加到已有工坊条目

在Steam工坊条目页面，使用作者工具中的图片/视频编辑入口选择上述四张JPG。它们是图库图片，不替换主封面，也不需要修改模组DLL、版本或游戏资源。SteamCMD当前上传配置继续保留已有条目ID；本次没有执行上传或重新生成VDF。

原有`01-pump-station-art.png`、`02-electronics-art.png`、`03-vehicle-yard-art.png`可作为后续附图。这三张是区域场景原画，和四张实机截图的性质不同，上传时应以场景展示介绍。

## 重新导出

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\Prepare-Screenshots.ps1
```

在workshop目录运行。脚本读取保存的四张原图，输出JPG、PNG、预览拼图和新的上传ZIP；不会启动游戏或调用Steam上传。
