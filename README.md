# Structure Editor — RimWorld 可视化结构编辑器

还在为 RimWorld 的建筑布局、机械集群阵地和特殊事件场景手动编写 XML 吗？

**Structure Editor** 是一款面向 RimWorld 的可视化结构编辑工具，旨在让复杂的地图结构设计变得更加直观、便捷。

通过网格化编辑界面，你可以像在游戏中建造一样，自由摆放建筑、调整位置、规划阵地，并实时查看整体布局效果。

## 主要功能

- **可视化结构编辑**：通过直观的网格画布放置、移动、旋转和删除对象。
- **游戏素材库**：浏览和搜索建筑等游戏素材，支持按集合、类型、分类及来源筛选。
- **多结构编辑**：通过标签页同时管理不同的结构设计方案。
- **便捷编辑操作**：支持框选、平移、缩放及常用快捷键，提高布局效率。
- **结构属性管理**：自定义结构标识、显示名称、结构类别及保存目录。
- **结构导出**：支持保存 JSON 工程，并导出用于 RimWorld 的结构 XML。

无论是设计机械集群防御阵地、敌方据点，还是制作特殊建筑群与事件场景，都可以在独立编辑器中完成布局规划，无须反复进入游戏进行调整。

**从繁琐的坐标编辑，到直观的可视化设计。**

Structure Editor，希望让 RimWorld Mod 的结构制作更加简单、高效，也让创作者能够把更多精力放在设计本身。

## 更多功能与兼容细节

- 建筑、单位、地形三类图层；支持基础游戏、已安装 DLC 及手动添加的模组素材来源。
- 素材按类型、来源和 Wiki 建造分类组合筛选；提供常用、收藏、最近使用与机械集群专题集合。
- 后台加载游戏数据，预览按需解码；支持每显示器 DPI 缩放与自适应筛选布局。
- 笔刷放置、矩形铺设、框选、整组移动/旋转、复制粘贴、吸取、撤销重做。
- 墙体连接预览、材质与方向；MP 兼容模式支持单个对象的初始休眠状态，新放置对象默认唤醒。
- 空间索引、可见区域绘制和地形分块缓存，避免铺设大片地板后反复重画整个场景。
- 独立 JSON 工程、Structure Framework 主格式、原版 PrefabDef 和 PNG 预览；保留 MechProtocol 兼容格式。
- 自动识别玩家启用的本地/创意工坊模组，按模组名称和 packageId 独立分类；支持游戏内实际 Def 目录同步。
- 素材查找、组合筛选与兼容材质使用索引和缓存；大型同步目录完整校验后一次替换，加载顺序不匹配时回退到 XML。
- 导出独立内容包并声明所需框架、DLC 和模组，不复制其他模组的资产。

## 运行

当前支持 Windows x64、RimWorld **1.7** 的本地素材结构。普通构建需要 .NET 10 Desktop Runtime；使用 `-SelfContained` 构建可附带 .NET 运行时。程序不会启动游戏，也不会修改素材来源。

本机已构建版本位于 `app/StructureEditor.exe`。首次运行会尝试识别 Steam 游戏目录；未识别时通过 **编辑 → 首选项 → 游戏目录** 设置。额外模组通过同一菜单下的 **素材来源** 管理。

设置、最近记录、自动备份和诊断日志保存于 `%LOCALAPPDATA%/StructureEditor`，新工程默认保存在系统“文档”下的 `Structure Editor` 文件夹。它们不随源码发布。

## 工程与游戏部署

**保存**生成 `.structure.json` 工程；右侧“结构”页选择目标格式。默认 **Structure Framework**，导出由独立框架读取的 `StructureFramework.StructureDef`。**文件 → 导出 → 独立结构内容包** 会创建 About.xml、布局 Def 和依赖清单。

原版 PrefabDef 是兼容格式，适合建筑与地形；PawnKind 编组和框架对象修饰器不能直接写入原版格式，遇到不支持的数据会阻止导出。逐对象休眠属于 **MP 专用扩展**，需主动启用，并声明 MechProtocol 依赖。

现有 MechProtocol 内容仍可使用 **文件 → 导出 → MechProtocol 兼容 XML** 更新原结构 Def；该格式保留原集群命名规则。导入旧 XML 后保存会生成独立工程，不自动改写原 XML 或肃清协议配置。

原版 PrefabDef 和框架定义都需要游戏端调用入口才会部署。框架自带开发者部署菜单、地图生成、事件、任务和公共 C# API；框架项目的 README 与 Docs 说明具体接入方式。

详见 [使用说明](docs/使用说明.md)、[工程格式](docs/project-format.md)、[素材与第三方说明](THIRD_PARTY_NOTICES.md)。

## 开发

安装 .NET 10 SDK 后：

```powershell
dotnet build src/StructureEditor/StructureEditor.csproj
./scripts/build.ps1
# 可选：打包包含 .NET 运行时的版本
./scripts/build.ps1 -SelfContained
```

输出为 `app/` 下的程序与 `dist/` 下的 ZIP。构建不依赖游戏 DLL，不需要安装 MechProtocol。素材解码在运行时读取用户本地游戏自带的库。

本机的金色魔方应用图标从异象 DLC 生成，位于不提交 Git 的 `branding/`。需要重新生成时，先编译，再运行：

```powershell
dotnet src/StructureEditor/bin/Debug/net10.0-windows/StructureEditor.dll --prepare-branding ./branding --game '你的 RimWorld 游戏根目录'
./scripts/build.ps1
```

构建会自动使用本地 ICO 和 PNG。没有这些文件仍可构建，窗口在加载本机异象素材后显示金色魔方。

本机具备游戏时，可运行素材、交互和离屏界面检查：

```powershell
./scripts/verify.ps1 -GameRoot '你的 RimWorld 游戏根目录'
```

检查不会显示或操作桌面窗口；结果写入 `%LOCALAPPDATA%/StructureEditor/Validation`。GitHub Actions 只构建和打包，不使用游戏素材运行这些检查。

## 目录

```text
src/StructureEditor/     独立 C# / WinForms 工程
  Compatibility/        特定模组的导出兼容规则
docs/                   使用和格式文档
examples/               仅含布局数据的示例
scripts/                本地构建、打包与验证
.github/workflows/      Windows 构建工作流
```

`app/`、`dist/`、`migration/`、游戏素材、DLL 和本机设置均不应提交。`migration/` 仅用于本次迁移的本地备份。

## 许可证

本项目的原创源代码采用 [MIT License](LICENSE) 开源。许可不覆盖 RimWorld、DLC、其他模组或第三方组件的素材与代码；相关权利与说明见 [素材与第三方说明](THIRD_PARTY_NOTICES.md)。

维护者：[NineteenReincarnation](https://github.com/NineteenReincarnation)。
