# 工程与导出格式

## 原生工程 v1

UTF-8 JSON，推荐扩展名 `.structure.json`。标识允许英文字母、数字和下划线，不能以数字开头，不强制 `MP_` 前缀。

```json
{
  "FormatVersion": 1,
  "DefName": "SE_ExampleRoom",
  "Label": "示例房间",
  "Category": "General",
  "StartDormant": false,
  "Items": [
    { "Kind": "Building", "DefName": "Wall", "Stuff": "Steel", "X": 0, "Z": 0, "Rotation": 0, "StartDormant": null }
  ]
}
```

`Kind` 为 `Building`、`Pawn` 或 `Terrain`。对象引用游戏中的 `ThingDef`、`PawnKindDef` 或 `TerrainDef`；`Stuff` 是可空的材质 ThingDef。`Rotation` 为北/东/南/西对应的 0/1/2/3。X 向右、Z 向上；(0,0) 是结构原点，部署时相对目标位置平移。

`AutoExportOnSave` 默认为 `true`，控制保存 JSON 时是否同步导出所选 `ExportProfile` 的 XML。`XmlExportDirectory` 可指定独立 XML 目录；为空时跟随工程目录。这两项仅用于编辑器保存流程，不写入游戏布局 XML。旧 v1 工程缺少字段时仍可读取，默认自动导出开启。

`StartDormant` 为 `true`（初始休眠）、`false`（初始唤醒）或 `null`（继承结构默认）。只有支持休眠的对象才应设显式初态。`Category` 当前为 `General` 或 `MechCluster`，是布局用途元数据，不改变对象战斗逻辑。

缺失的素材引用仍保存在工程中。导出时检查缺失 Def、材质、占地重叠和状态支持情况。工程文件不包含游戏贴图或运行时代码。

## 主格式：Structure Framework XML

根为 `Defs/StructureFramework.StructureDef`，包含 `defName`、`label`、`schemaVersion=1`、`category`、可选 `placement`、`tags`、`requiredPackages` 及 `things`、`pawns`、`terrain` 列表。

- 建筑：`things/li/thingDef`、可选 `stuffDef`、`position`、`rotation`。
- 单位：`pawns/li/pawnKind`、`position`、`rotation`。
- 地形：`terrain/li/terrainDef`、`position`。
- 对象扩展：`modifiers/li Class="其他模组的公开修饰器类"`。

`position` 为 `(x,0,z)`。框架的游戏端类负责读取和部署。JSON 工程的 `ExportProfile` 为 `Framework`、`VanillaPrefab` 或 `MechProtocol`，默认 Framework；`EnableMPCompatibility` 显式启用 MP 专用状态扩展。

框架还支持共享调色板、符号布局、嵌套、标记、屋顶、事件和生成策略，完整契约在框架项目的 Docs/XML.md 与 Docs/API.md。当前桌面编辑器会拒绝不能无损编辑的字段，不会静默丢弃。

旧的 `StructureEditor.StructureDef` 仅作为历史交换格式继续导入，不再作为对外主格式导出。

## 原版 PrefabDef 兼容

输出 `Defs/PrefabDef`，将建筑按 DefName、材质、方向分组，使用 `positions` 与 `relativeRotation`，地形转换为 `rects`。`size` 为整个占地范围，坐标归一到模板范围；编辑器用 XML 注释记录原点偏移，游戏会忽略该注释，编辑器据此无损还原原坐标。

原版不支持 PawnKind 编组或本框架修饰器，遇到这些内容会阻止导出。MP 休眠不写入原版 XML，仍保留在工程中。内容包的 About.xml 根据实际引用声明 DLC、模组与框架依赖；使用自定义模组建筑仍然需要相应模组。

## MechProtocol 兼容适配

适配器输出 `Defs/MechProtocol.MP_StructureDef`，保持既有字段契约。导入这类文件后会以原生工程另存，不覆盖原文件。导出时可选择原 XML 作为目标。

兼容导出要求 `MP_` 前缀。机械集群名称遵守 `[核心名称]·[I–V]-[A…]`，标识遵守 `MP_Cluster_[Core]_T[01–05]_V[01–99]`。该限制只应用于兼容导出，通用工程和通用 XML 不受此限制。编辑器不修改部署逻辑，也不自动绑定任何事件。
