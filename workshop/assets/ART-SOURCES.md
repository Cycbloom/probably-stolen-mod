# 当前发布素材

当前封面在既有场景上加入模组标题、副标题和作者署名。

- 底图来源：`../../Paper/dumping_ground_electronics.png`，本模组既有AI辅助场景美术。
- `preview-titled.png`：内置imagegen以既有封面为编辑目标，加入「垃圾场拓展」「分区拾荒 · 声望成长」及Cycbloom署名；输出1774×887。提示词见`COVER-PROMPT-TITLED.txt`。不是实机截图。
- `preview.png`：带标题成品的逐字节复制，作为打包入口。
- `preview.jpg`：同一成品以JPEG质量90导出，仅用于压缩上传，413,919字节，小于1MB。使用Windows System.Drawing编码器；未调用Python作图像编辑。
- `COVER-PROMPT-DRAFT.txt`：本次内置imagegen尝试的提示词草稿；请求长期未返回后停止等待，未用作最终封面。
- `../gallery/`的三张图片逐字节复制自对应的water、electronics、vehicles场景原画，也是素材展示而非实机截图。
- `../gallery/screenshots/`：2026-11-17作者提供的四张实际游戏截图。按作者选择使用本地System.Drawing裁剪、缩放、压缩及外部功能标题排版，未用生成模型重绘游戏界面。输出1920×1200；输入、哈希及原始图片分别保存在sources.json和originals目录。

本次未将游戏或其他模组的DLL、资源表或反编译源码纳入发布包。
