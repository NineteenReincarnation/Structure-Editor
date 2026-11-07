# Structure Library / 结构工程库

此目录存放作者制作的 RimWorld 结构布局，按文件夹整理。

- `MechanoidSentryOutposts/`：机械哨戒阵地 I-A、I-B、I-C、I-D。
- `MechClusters/`：小镰铸巢等机械集群结构。
- `Tests/`：用于布局验证的测试工程。

每个结构的 `.structure.json` 为可编辑工程；同名 `.xml` 是游戏内结构定义。软件左侧结构库将两者合并为一项，并优先打开 JSON。

如有同名 `.png` 或 `_preview.png`，选中结构时会直接显示该图片。没有 PNG 时由软件按现有素材生成预览缓存。

保存新工程时优先使用该目录（除非在软件内主动指定其他位置）；在已打开文件上保存不会改变原文件路径。

此目录只存作者编写的布局文本，不放 RimWorld 或其他模组的贴图、游戏 DLL、私有设置或机器专属的绝对路径。
