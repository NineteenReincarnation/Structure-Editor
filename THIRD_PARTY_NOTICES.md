# 素材与第三方组件

Structure Editor 的源码和默认构建包不附带 RimWorld 游戏素材、DLC 素材、其他模组的贴图、游戏程序集或存档。使用者需自行安装相应游戏和素材来源。界面中的素材预览从使用者选择的本地目录只读加载。

本地可选的应用图标基于异象 DLC 的金色魔方（`Things/Item/Special/GoldenCube`），当前版本加大主体并加粗黑色轮廓。`branding/` 中的 PNG 与 ICO 不提交到源码仓库；采用该图标的本地构建会在程序和窗口中使用它。未嵌入该素材时，窗口可从已安装的游戏中读取原版金色魔方。

RimWorld 及其游戏素材属于各自权利人。本项目是独立工具，不代表 Ludeon Studios。

针对 RimWorld 1.7 的贴图读取，在运行时绑定游戏目录自带的 Waylith、AssetRipper.TextureDecoder 和 ZstdSharp 解码实现；这些库不复制到源码或发布包中，其权利和许可由各自项目及游戏分发版本规定。

机械集群专题的分类名称与别名参考[灰机 Wiki：机械集群](https://rimworld.huijiwiki.com/wiki/%E6%9C%BA%E6%A2%B0%E9%9B%86%E7%BE%A4)。未打包 Wiki 页面或图片。贴图和 DefName 来自本机安装内容；额外模组条目单独按来源显示。

MechProtocol 兼容命名规则保留在 `Compatibility/`，它不构成游戏端功能或对该模组的硬依赖。
