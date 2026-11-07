# Structure Editor 项目架构

本文记录实际目录职责与数据流。仅用于维护和扩展，不替代用户侧 [使用说明](./使用说明.md) 或 [工程格式说明](./project-format.md)。

## 目录结构

```text
Structure-Editor/
├─ .github/workflows/          # Windows 持续集成与打包
├─ assets/                     # 本项目自制的 README 展示素材
├─ docs/
│  ├─ architecture.md          # 本文
│  ├─ project-format.md        # JSON / XML 结构协议
│  ├─ 使用说明.md               # 用户操作说明
│  └─ changelog/               # 历史版本记录
├─ examples/                   # 可复用的无游戏资源结构工程
├─ scripts/                    # 构建、打包、验证入口
├─ src/StructureEditor/
│  ├─ App/                     # 程序启动、应用品牌与全局运行设置
│  ├─ Canvas/                  # 画布绘制、选取、交互、粘贴预览
│  ├─ Catalog/                 # 游戏/XML/运行时素材读取与分类、贴图解码
│  ├─ Core/                    # 结构工程对象、占地与索引
│  ├─ Formats/                 # JSON/XML 等结构格式的读写与导出依赖
│  ├─ UI/                      # WinForms 主窗口、菜单、工作区、结构库、保存流
│  ├─ Compatibility/           # 仅存特定 Mod 的兼容逻辑、共享命名规则
│  ├─ Validation/              # 原有内置自检及功能回归检查
│  ├─ StructureEditor.csproj   # 固定项目入口，不改变构建路径
│  └─ app.manifest
├─ Tools/Tests/                # 特殊验证工具/从项目文件链接编译的检查代码
├─ AGENTS.md                   # 开发约束
├─ README.md                   # 默认中文项目介绍
└─ README.en.md                # 英文项目介绍
```

## 模块职责及协作

1. **App → UI**：`Program` 负责启动和配置，`MainForm` 组织 WinForms 工作区及用户操作；主窗体的 `partial` 文件仍保留在同一命名空间 `StructureEditor`。
2. **Catalog → Canvas / UI**：`ModSources` 识别素材来源，`Catalog` 解析游戏与 Mod 的 XML，`RuntimeCatalog` 在有有效游戏运行时快照时覆盖对应条目；`TextureLibrary` 从用户本机游戏按需加载预览。目录数据不进入用户工程文件。
3. **Core → Canvas / Formats**：`StructureDocument` 保存建筑、单位、地形、元数据；`StructureIndex` 提供空间索引和占地检查。绘制及格式处理共用同一个结构模型。
4. **UI → Formats**：`WorkspaceDocuments` 管理多标签与结构库，`WorkspaceSaving` 处理存盘，`WorkspaceExport` 选择格式；`FrameworkCodec`、`PrefabCodec` 与 `ExportDependencies` 负责具体导入导出逻辑。
5. **Compatibility**：MechProtocol 的名称和专用导出规则只能放在兼容层；默认编辑流程须可独立用于 RimWorld 和其他 Mod。

以上是现有依赖的职责归属，不表示已强制建立跨程序集或复杂依赖注入架构。本次目录整理只移动源文件，不修改类名、公开行为、文件协议或游戏端逻辑。

## 工程与构建约束

- 目标平台：Windows x64、.NET 10、WinForms；RimWorld 1.7 的素材在运行时从用户的本机安装读取。
- 固定构建入口：`dotnet build src/StructureEditor/StructureEditor.csproj`。
- 发布打包：`./scripts/build.ps1 -SelfContained` 生成完整独立安装包，CI 上传到 Artifacts。
- **Git 拉取即可运行**：每次 `main` 有新提交，Windows CI 另行生成精简的、依赖 .NET 10 Windows Desktop Runtime 的 `app/`，将程序文件版本化提交回 `main`（当前也同步到旧默认分支 `codex/structure-editor`）。用户在 GitHub Desktop 拉取后即可运行 `app/StructureEditor.exe`，不必亲自编译。
- `app/` 是唯一允许由 CI 自动纳入版本控制的生成目录；`dist/`、`bin/`、`obj/` 仍不提交。首次从旧的忽略目录迁移时，请先关闭程序并移走本地未被追踪的旧 `app/`，避免 Git 无法覆盖未跟踪文件。
- 已有内部验证由 `Program --self-test` 入口运行；`Validation/` 中的检查继续随主程序集编译，以避免改变现有验证行为。
- 不提交 RimWorld 游戏贴图、DLL、个人路径或配置、迁移备份。第三方资源许可见 `THIRD_PARTY_NOTICES.md`。
- 修改默认工程格式时保持旧版文件可读取；遇到不支持的数据要明确报错，不能静默丢失布局信息。

## 维护策略

以 `main` 为唯一新功能开发主线。优先实现功能、修复确定的问题；不要因为目录整理就改动射击逻辑、布局坐标、素材解析结果、UI 样式或输出格式。新增源代码按职责放到对应目录，而非继续在项目根目录堆放文件。
