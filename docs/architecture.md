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
├─ structures/                 # 用户维护的结构工程库，优先读取，JSON/XML 成对保存
├─ scripts/                    # 构建、打包、验证入口
├─ app/                        # CI 更新的精简运行程序（不再复制数据/文档）
├─ src/StructureEditor/
│  ├─ App/                     # 程序启动、应用品牌与全局运行设置
│  ├─ Canvas/                  # 画布绘制、选取、交互、粘贴预览
│  ├─ Catalog/                 # 游戏/XML/运行时素材读取与分类、贴图解码
│  ├─ Core/                    # 结构工程对象、占地与索引
│  ├─ Formats/                 # JSON/XML 等结构格式的读写与导出依赖
│  ├─ UI/                      # WinForms 主窗口、菜单、工作区、结构库、保存流
│  ├─ Compatibility/           # 仅存特定 Mod 的兼容逻辑、共享命名规则
│  ├─ Validation/              # 所有自检及功能回归检查（含 TurretExplosionChecks）
│  ├─ StructureEditor.csproj   # 固定项目入口，不改变构建路径
│  └─ app.manifest
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
- 不提交 RimWorld 游戏贴图、DLL、个人路径或配置、迁移备份。项目自身的 MIT 许可保留在根目录 `LICENSE`，独立发布包也包含该文件。
- 修改默认工程格式时保持旧版文件可读取；遇到不支持的数据要明确报错，不能静默丢失布局信息。

## 维护策略

以 `main` 为唯一新功能开发主线。优先实现功能、修复确定的问题；不要因为目录整理就改动射击逻辑、布局坐标、素材解析结果、UI 样式或输出格式。新增源代码按职责放到对应目录，而非继续在项目根目录堆放文件。

## 项目结构库（structures/）

- 仓库根目录的 `structures/` 是内置结构库的第一读取来源，按照子文件夹整理布局；与 `app/` 同级，GitHub Desktop 拉取后即可被识别。
- 左侧提供「素材库 / 结构库」标签，不再弹出单独的文件选择窗口。选中结构将优先显示同名 PNG（`<DefName>.png` / `<DefName>_preview.png`），无图时使用游戏素材渲染 PNG 到本地预览缓存；双击结构直接打开。
- JSON 工程和同名 XML 属于同一份结构，左侧只显示一条记录，打开时优先使用可编辑的 `.structure.json`，避免重复或误改导出的 XML。
- 首次保存新工程默认使用仓库结构库（除非用户选择过另一个保存位置）。编辑已有工程时继续在原有位置保存；可通过「保存位置」修改。
- `scripts/build.ps1 -SelfContained` 只为独立发布包复制一套 `structures/`，因此单独解压也可读取；普通 `app/` 开发版不再复制结构库，在完整仓库里直接读取根目录唯一的 `structures/`。
- 不应在 JSON 中保存作者电脑的绝对游戏路径。仓库中的 `structures/Tests/` 是调试布局，`structures/MechanoidSentryOutposts/` 为哨戒阵地。

### 结构重命名

`Core/MP_StructureRenamer.cs` 负责重命名预检查与文件操作，`UI/WorkspaceLibraryTree.cs` 在左侧树中提供「右键重命名 / F2」并更新工作栏。根据选中工程及关联的 XML 输出目录跟踪文件；只修改与原 DefName 匹配的 XML 节点，不覆盖其他结构定义。文件操作先备份，预检查目标路径冲突，失败尝试恢复。对应轻量回归在 `Validation/MP_StructureRenameChecks.cs`。

手动指定的 XML 导出路径记录于本机 `StudioPaths.MP_XmlExportPaths`，由 `WorkspaceExport.MP_RecordExportTarget` 维护，避免将用户绝对路径写进仓库的共享工程。右键重命名将本地记录的路径与发现的同名 XML 一起解析并在成功后更新映射。


## 深色玻璃界面和护盾兼容

- UI/MP_WindowGlass.cs：启用受系统支持的深色标题栏与 Desktop Acrylic 背景以及玻璃质感工具栏；保留系统窗口的最大化、拖动等原生行为。
- Catalog/MP_ShieldRanges.cs：读取投射物拦截器及名称明确的 Shield/Barrier/ForceField 组件中的静态半径，由 CatalogItem.ShieldRadii、RuntimeCatalog 和画布的范围绘制使用，不修改游戏内部护盾功能。
- 顶部重复结构库和关闭按钮已删；右上角新增殉爆、探测、护盾范围开关；Tab 负责切换编辑图层，文件关闭仍可使用 Ctrl+W 或文件菜单。

## 单行工作区工具栏与视觉过渡

- 主菜单下方仅保留一条工具栏：左侧新建、打开及工作文档标签，右侧殉爆/探测/护盾范围以及保存、保存位置、导出预览；删除上下叠放的第二层标题/操作栏。
- `MP_GlassLayout` 承载同一行的两个工具区域，右侧自适应宽度，左侧文档标签在空间不足时使用原有溢出机制；不让标签挤占范围与保存按钮。
- `MP_SectionTabs` 完整替代原生 TabControl 的页签条，保留原有页面内容与操作，避免任何白色未使用页签区域。
- 调低工具栏渐变的亮暗差，采用近似的深蓝灰背景与细分隔线，并将左右分栏间隙由 6 调到 3 像素，避免面板像独立卡片一样割裂。


## 标签栏与配色处理

- `UI/MP_DocumentTabButton.cs`：每个文档标签右侧有独立命中区域 ×，关闭时沿用既有 `CloseWorkspaceDocument` 保存确认。关闭区点击不会触发误切换。
- `UI/MP_SectionTabs.cs`：替换原生 TabControl 的亮白未使用页签区域，以统一深色标签栏组织左侧素材/结构库及右侧属性四个页面。
- `UI/MP_DarkControls.cs`：覆绘原生 ComboBox 的亮色箭头区，并在素材 ListBox 的系统滚动条上覆盖可拖动的深色滚动轨道，同时保留原始列表选择、滚轮及键盘行为。
- `UI/WorkspaceCommands.cs`：Ctrl+G/H/J 分别控制殉爆、探测、护盾范围；原 Ctrl+J 复制重映射为 Ctrl+D，菜单与帮助同步更新。


## 编辑图层与性能调整

- 编辑工具栏移除快捷键字母后缀；按键处理仍由 `WorkspaceCommands.HandleModeShortcut` 负责。
- `WorkspaceUi` 将一级「显示图层」菜单移到编辑图层旁边，直接设置 `StructureCanvas.VisibleLayers` 与 `ShowGrid`，并在打开时同步快捷键切换产生的网格状态。
- 对象朝向字段根据 `CatalogItem.Rotatable` 及当前选中对象是否有可旋转部分决定可见性；无此能力的单个对象不显示朝向。
- `MainForm.FilterPalette` 重用未变化的筛选结果，避免反复重建 WinForms 素材列表；`UpdateSummary` 对结构摘要做缓存；`StructureCanvas.SelectItems` 避免相同选区产生重复事件。
- `WorkspaceDocuments.LibraryDefinitions` 不再为结构列表预览第二次反序列化完整 JSON；`WorkspaceStartup` 不在首次完成加载前同步解码固定的八个缩略图，继续通过按需异步请求读取贴图。


## 默认原版可解析 XML 合同

- 新建 `StructureDocument.ExportProfile` 默认 `VanillaPrefab`，输出 `PrefabDef`，避免无 MP 兼容时仍因默认 `StructureFramework.StructureDef` 而在纯原版中出现未知 Def 类型。
- 为不回归旧工程，`StructureDocument.Load` 对缺少 `ExportProfile` 字段的历史 JSON 显式恢复旧的 `Framework` 默认；明确选过格式的工程完全保持原样。
- `ExportDependencies.Xml` 仅允许明确的三种目标类型，不再把未知格式默默转为框架 XML。
- `PrefabCodec.Export` 遇到 Pawn、专用修饰器、初始休眠、MP 扩展开关、框架生成参数或框架标签时拒绝有损导出；保存 JSON 仍然安全。
- 原版 `RimWorld.PrefabDef` 的 `size`、`rotations`、`things`、`terrain`、`edgeOnly` 字段，以及 `PrefabThingData` 的 `positions`、`stuff`、`relativeRotation` 和 `PrefabTerrainData.rects`，可与 RimWorld 1.7 原版反编译定义对应。解析成功 ≠ 实际资产存在或地图自动生成。


## 导出检查和原版条件兼容

- 新的 \`Formats/MP_ExportReview.cs\` 提供统一输出分析，区分 **Error**（有损、不可解析、格式不支持）和 **Warning**（本地缺少引用或依赖、通用原版回退省略扩展对象）。\`StructureStorage.ExportXml\` 在覆盖输出前调用分析，只有确定错误阻止导出。JSON 永远在 XML 之前保存，导出失败仍保留可编辑项目。
- \`ExportDependencies.Xml(...,"Universal")\` 生成 \`PrefabDef\`（仅 Core 建筑+地形）和有 \`MayRequire\` 的 \`StructureFramework.StructureDef\`。增强定义的 DefName 使用 \`MP_EnhancedName\` 统一生成，避免与原版基础定义重名；不是游戏运行时自动选择替代版本。
- 原版 \`VanillaPrefab\` 在存在外部依赖时，于**整个 Def** 设置 \`MayRequire\`，避免把未安装素材的引用直接交给原版解析；不对未经证实支持条件加载的子字段做不安全的细粒度条件标记。
- 普通 XML 的依赖列表仅作检查及 XML 注释；导出完整 Mod 包时，原有格式继续用 \`About.xml/modDependencies\` 声明必需依赖；\`Universal\` 的扩展依赖只生成可选 \`loadAfter\`，与原版基础 Def 的独立加载能力保持一致。
- UI 的「兼容拓展」嵌入「导出」页，自动给出适用性推荐但不自动更改手动勾选状态；格式切换只清除已不适用的开关，不抹除对象的原始属性。
- \`Validation/StudioExportChecks.cs\` 覆盖原版/框架/MP/通用输出、告警分级、条件依赖及共享 XML 安全更新；Windows 编译不代表已经在真实游戏中完成端到端加载验证。

## 根目录与生成文件整洁约定

- 仓库**唯一启动目标**是 `app/StructureEditor.exe`。移除旧根目录 `启动结构工坊.cmd`；如果旧的个人 Windows 快捷方式原先指向该 CMD，请把目标改成 `app/StructureEditor.exe`。
- **运行时 app/** 是最小可拉取程序：仅 exe/dll/运行配置及生成标记。不再复制根目录数据和文档，避免重复文件。
- **独立 standalone.zip** 保留其自带的 `structures/`、使用说明和项目 `LICENSE`；它属于离线发行件，不是仓库内部第二份可编辑源文件。
- 原 `Tools/Tests/TurretExplosionChecks.cs` 移到 `src/StructureEditor/Validation/TurretExplosionChecks.cs`，删除 .csproj 手工链入路径，恢复单一的验证代码归属。
- `dist/`、`branding/`、`Directory.Build.props`、`*.lnk` 及历史迁移目录由 Git 忽略；Git Pull 不会替用户清理本地文件。

## 启动崩溃防回归检查

- 自定义深色标签容器 `MP_SectionTabs` 的 `TabPage` 必须由 WinForms `TabControl.TabPages` 承载，不能放进普通 `Panel.Controls`；外层按钮只负责可见的深色标题栏。
- `Program --ui-smoke-test` 在 Windows 无需加载 RimWorld 游戏目录，即可实例化主窗体并核对全部标签页的父容器；Windows CI 在更新 `app/` 前执行此检查，避免只编译成功却启动即报错。

## 启动性能与缓存

- `MP_CatalogSnapshotCache`：基于已选游戏与 Mod 目录的 XML、翻译、配置及 RuntimeCatalog 文件路径、大小与更新时间生成指纹；未变化时直接加载分类、标签、结构索引和材质快照，减少每次启动重复解析 Def 和语言翻译。变更模组或文件后自动失效重建；损坏或不可访问的缓存自动回退到原始 XML。
- `TextureLibrary`：建立 ZIP 内图片清单后不再为所有条目提前读取本地偏移；第一次真正解码该 ZIP 中贴图时按需生成偏移索引，避免启动阶段不必要的随机磁盘 I/O。
- `WorkspaceStartup`：仍在后台加载，不阻塞 UI 线程；`%LOCALAPPDATA%/StructureEditor/startup.log` 记录缓存命中、指纹检查、贴图索引、Def 解析及端到端加载耗时。
- `--catalog-cache-test`：无需真实游戏安装，验证冷/热启动、修改 XML 自动失效、重新命中以及贴图 ZIP 偏移懒加载，Windows CI 必须通过后才能同步 `app/`。
- JSON 结构工程、输出 XML、已安装 Mod 和本机设置均不写入该缓存，也不会因缓存失效被修改或清理。

## 2026-11-08 UI 可用性修正

- 主画布操作只保留热键：移除原「选择 / 放置 / 矩形 / 擦除 / 平移 / 缩放 / 旋转 / 居中」整排按钮，保留层级选择及范围控制。工具切换由 `ProcessCmdKey` 和画布事件控制，不改变操作语义。
- 右侧「结构」「导出」合并为单一「工程」页，内部连续显示结构信息、XML 导出、兼容检查；删除独立「操作」页，帮助菜单中的深色键盘模拟窗集中展示快捷键。
- 帮助窗口与关于页面使用 `MP_HelpWindows`，替代白色 MessageBox；项目主页链接是 `https://github.com/NineteenReincarnation/Structure-Editor`，不是作者个人主页。窗口尺寸受当前显示器工作区约束，内容可滚动。
- `MP_BorderlessTabHost` 保持 TabPage 合法托管于 TabControl，重写显示区域和 `TCM_ADJUSTRECT` 以消除默认浅色边框，不再将 TabPage 放到 Panel（之前曾导致启动崩溃）。
- 素材列表使用带深色覆盖滚动条的裁剪容器；`MP_DarkTreeView` 禁止原生水平滚动条并请求 Windows 深色主题，避免长文件名引入大片白色滑块。
- 画布的 `SceneTexture` 按需加载期间返回 null 时，绘制纯色低细节几何占位而不是对象 Label 或 DefName。贴图到达后原有 `TextureReady` 触发重绘，避免拖动/缩放瞬间出现“文字化单位”。
- `--ui-smoke-test` 检查工作区总共四个 TabPage（左右各二），全部由 TabControl 承载，确认「工程」页存在且不再构建「操作 / 结构 / 导出」三页。

## 标签页与属性的按需展示

- 顶部主工具栏只放已打开结构标签（含关闭 ×）及必要的范围开关、保存。采用对称三列布局：右侧操作宽度在左侧留出等宽平衡列，结构标签在中间独立宿主内水平居中；标签过多时由原生 ToolStrip 溢出处理，调整窗口大小后重新计算。
- 「新建」「打开…」从结构标签条移到画布上方操作行最左侧，紧邻「编辑图层」；不再同时显示两份。移除顶部重复的保存目录与 PNG 预览快捷入口，相关操作留在「工程」页或菜单中。
- 对象属性的「材质」标题和下拉框只在当前对象 `Catalog.MaterialsFor(item)` 存在可用选项时显示；无对象时也隐藏。方向、MP 休眠初态各自按对象能力及导出兼容状态显隐，避免无意义的空输入框。删除右上角与底部重复的「未选择」提示。
- Windows 启动检查会验证新建快捷动作没有混入居中的结构标签条，其他对应的布局和素材选择回归检查位于 `Validation/StudioUsabilityChecks.cs`。

## 许可与目录清理

- 项目不再另设 `THIRD_PARTY_NOTICES.md`。原内容仅是资源来源说明，不是第三方许可证的全文，也不属于程序运行依赖；本项目的 MIT `LICENSE` 必须保留，独立发布包同样带上。
- 旧版 `migration/` 不受版本控制，也不参与启动、编译或结构读写；确认不需要历史备份后，可以由用户在本机删除。
- 示例结构目录已从仓库移除；正式结构工程仅保存在 `structures/`，独立发行包也不再复制示例目录。

## 2026-11-08 结构与材质可用性精简

- `MaterialItem.Texture` 由素材 XML 的 `graphicData/texPath`（必要时回退 `uiIconPath`）解析；材质下拉项同时展示按需解码的小贴图和中英名称，未提供或暂未读出的纹理用该材料的真实 `Color` 色块代替；贴图到达后刷新该下拉控件，避免同步解码阻塞。
- `MechClusterCatalog` 新增「防御设施」类别：原版 `Wall` 和 `Barricade`，同时继续保留单独统计的 22 种 Royalty 机械集群专用建筑；不将普通门/家具混入机械集群。缓存 schema 从 2 升到 3，避免旧缓存保留旧分组和缺失材质缩略图字段。
- 结构库树改为紧凑深色绘制、无多余 Unicode 前缀，避免原生白色边框；重新筛选保留已展开目录及可保留的选中项。未选择文件时不显示说明段落，单选预览、双击打开、右键/ F2 重命名逻辑保持不变。
- 工程页保留名称、DefName、类别、适用的休眠选项、XML 目标、自动导出、检查状态和主要导出按钮。两个路径、依赖详细信息及其他导出操作全部折叠到「高级设置」；导出错误时仍直接提示，错误未修复则禁止 XML 导出，不会抹除 JSON 工程。
- 未选择对象时清空属性解释文字；无兼容状态时隐藏扩展说明，默认不显示自动导出的历史路径。帮助/关于子窗口统一支持 `Esc` 关闭。
