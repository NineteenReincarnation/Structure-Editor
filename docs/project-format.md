# Structure Editor · Structure Framework 统一结构标准

从本版本起，编辑器**只导出一种 XML**：`StructureFramework.StructureDef`。不再区分原版 PrefabDef、MechProtocol、Framework 或混合导出。JSON 工程 (`*.structure.json`) 仍然是唯一可编辑源文件。

## 统一输出契约

- 根元素固定 `Defs / StructureFramework.StructureDef`，当前 `schemaVersion=1`。
- 支持字段：`defName`、`label`、`description`、可选的 `placement`、`tags`、`requiredPackages`。
- 对象使用 `things`、`pawns`、`terrain` 列表，记录 `position=(x,0,z)`；非地形对象可记录 `rotation`，建筑可记录 `stuffDef`；对象修饰器写入 `modifiers`。
- 所有新工程和打开的旧工程，`ExportProfile` 统一为 `Framework`。旧四模式的配置值兼容读取，但不再决定生成格式。
- `AutoExportOnSave` 默认开启；保存 JSON 后同步更新 Framework XML。输出目录由 `XmlExportDirectory` 决定，为空时跟随 JSON 工程目录。
- 对支持休眠的对象，如果具有明确的逐对象启动状态，或开启结构默认休眠，自动添加 `StructureFramework.Integrations.MPDormancyModifier`，并自动声明 `Anno.MechProtocol` 依赖；无这些状态的基础结构不依赖 MP。
- 独立 Mod 内容包的 `About.xml` 记录实际必要的 Framework、DLC、模组依赖；导出本身不会让结构自动出现在地图或事件里。

## 旧内容迁移

- 支持导入历史 `PrefabDef`、`MechProtocol.MP_StructureDef`、`StructureEditor.StructureDef` 和 Framework XML。历史格式仅用于读取，不再作为导出选项。
- 编辑器导出到已有 XML 时按 `defName` 更换自身定义类型，保留其他结构和内容；覆盖之前进行备份。旧通用模式生成的条件伴生 Def，仅在匹配其专用标记时清理。
- 无法无损编辑的 Framework 扩展（例如未建模的屋顶、锚点、未知扩展字段）仍拒绝有损导入。
- 不再区分机械集群与通用结构类别；新 JSON/XML 均不存储结构分类。历史文件里的 `Category`、`category` 和 `structureCategory` 可被读取并忽略，不影响建筑、单位、地形及休眠状态。
- 机械集群命名作为可展开工具保留，通过 `MP_Cluster_` DefName 识别集群专用命名规范，普通工程不受限制。
