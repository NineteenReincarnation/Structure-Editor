# Structure Editor · 当前架构规范

本文件只记录**当前生效**的模块职责、兼容边界和验证方式，不记录已完成版本的开发过程。机械集群 CP 规则见 [STANDARDS.md](STANDARDS.md)，界面与快捷键规则见 [ui-style.md](ui-style.md)，第三方许可及字体说明见 [third-party/lilt.md](third-party/lilt.md)。

## 目录与依赖边界

```text
Structure-Editor/
├── app/                         CI 生成的 StructureEditor.exe
├── assets/                      README 展示素材
├── docs/                        现行规范与第三方许可
├── scripts/                     构建、架构检查、游戏数据验证
├── structures/                  唯一可编辑结构库（JSON/XML）
└── src/StructureEditor/
    ├── App/                     启动、设置及文档会话引用
    ├── Infrastructure/Paths/    应用数据路径
    ├── Core/                    结构模型、索引、文档历史及重命名
    ├── Catalog/                 游戏 Def 扫描、筛选、贴图和缓存
    ├── Formats/                 Framework 编解码、导出、备份和存储
    ├── Canvas/                  画布编辑与渲染
    ├── Compatibility/           可选 Mod 命名与 CP 集成
    ├── UI/
    │   ├── Workspace/           MainForm 工作区、命令与文档会话
    │   ├── Controls/            WinForms 控件及 WPF 宿主
    │   ├── Wpf/                 页签、素材库、检查器和动效
    │   └── Audio, Theme, Motion, Help/
    └── Validation/              独立回归与离线启动检查
```

- **领域层不引用视图。** `Core`、`Catalog`、`Formats`、`Infrastructure`、`Compatibility` 不持有 `MainForm`、WinForms 控件或 WPF 状态。
- **Workspace 是唯一协调层。** `MainForm` 采用 partial 文件划分工作区职责，不复制第二份文档、撤销或素材选中状态。WPF 控件接收数据和回调，不负责结构文件写入。
- **功能逻辑按职责归属。** `MP_DocumentHistory` 管理历史栈及文档切换后的恢复，`MP_CatalogPaletteQuery` 接收明确的筛选数据，`MP_StructureStorage` 管理 XML/JSON 和备份，`MP_DataDirectories` 管理应用数据路径。接口变更需同步所有调用点与验证代码。
- **可扩展且不重复。** WPF 素材库和结构库是唯一可见列表；不能重新启用重复的原生树形列表或独立结构选择器。动效、选中填充和缩略图缓存需遵守 [UI 规范](ui-style.md)。

## 数据和发布兼容

- 新导出的 XML 只使用 `StructureFramework.StructureDef`，旧 PrefabDef、MechProtocol XML 仍需可导入。项目 JSON 与旧有 DefName、休眠状态、旋转和物料语义保持兼容；不恢复已移除的结构分类字段。
- `structures/` 是用户工程库。不得借重构删除、迁移、重命名其中的 JSON/XML，或写入机器专属绝对路径。导出不得覆盖无关 Def，重命名须同步关联文件并保留必要备份。
- `app/` 只发布单文件 `StructureEditor.exe`。该程序依赖 Windows 上安装的 .NET 10 Desktop Runtime；无需散列 DLL、另外的启动器、ZIP 或 dist 包。 `app/` 仅由 CI 生成，代码改动不得直接提交二进制。
- 不把游戏资源、用户设置、字体文件、临时文件、备份或历史版本更新记录纳入源码仓库。
- 对第三方派生实现保留必要的归属及许可说明；尤其不得删除 [lilt MIT 声明](third-party/lilt.md)。

## 稳定的界面与性能约束

- 文档标签的关闭按钮须保留脏数据保存确认；`Ctrl+G/H/J` 分别控制相应范围叠加显示；`Ctrl+D` 复制对象。
- 素材流程为搜索 → 集合 → 类型 → 分类 → 来源；选中行背景不得越界，标签栏不覆盖工具栏。画布、面板、检查器与 CP 摘要须兼顾 DPI 与窄窗口。
- 贴图库建立目录和直属子项索引，加载后查找方向贴图及材质外观不反复遍历全部资源；必须保持原本的首个匹配优先级和 Mod 覆盖规则。
- 已存在且有效的结构预览 PNG 应直接异步读取；只有缓存失效或缺失时才创建 STA 渲染线程，仍按原时间戳规则刷新。
- 纹理解码结果共享缓存，缩略图异步批量刷新；预览任务可取消，过期结果不可覆盖当前选中；尊重减少动效设置。具体样式以 `MP_StudioTheme` 和 [UI 规范](ui-style.md) 为准。

## 验证

- `scripts/check-architecture.ps1`：检查目录与模块边界。
- `scripts/build.ps1`：Windows 发布单文件 EXE。
- `--ui-smoke-test`：离线验证界面宿主、快捷键、序列化、文档历史和素材筛选。
- `--catalog-cache-test`：离线验证素材缓存。
- `scripts/verify.ps1 -GameRoot <RimWorld 目录>`：需要游戏文件的完整验证；CI 不具备用户的本地 RimWorld 数据。

CI 运行离线检查并在成功后更新 `main:app/`，其他分支不自动处理。编译与离线测试不能替代实际 Windows 下不同 DPI、连续切换页签及快速滚动的人工交互检查。
