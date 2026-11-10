# Structure Editor — Architecture and UI invariants

## Purpose and ownership

The editor creates and edits RimWorld structures. New work must retain
Structure Framework XML and existing JSON compatibility; imported historical
formats are handled by the existing codecs. **Never couple the domain model to
the visible UI.** MP_ prefixes remain mandatory for new project-specific Defs.

| Layer | Responsible code | Owns |
| --- | --- | --- |
| Application | `App/Program.cs` | Startup, paths and CI smoke checks |
| Domain | `Core/StructureDocument.cs`, `Core/StructureIndex.cs` | Objects, validation, copy, JSON serialization |
| Import/export | `Formats/` | Canonical XML, imported formats and export dependencies |
| Catalog | `Catalog/` | Definitions, searchable collections and texture cache |
| Editing | `Canvas/` | Selection, placement, drawing, undo/redo and preview rendering |
| Workspace | `UI/MainForm.cs`, `UI/Workspace*.cs` | Document sessions, commands, saving and UI coordination |
| Presentation | `UI/Wpf/` and `UI/MP_InspectorLayout.cs` | Appearance, lists, navigation, animation and responsive fields |
| Compatibility | `Compatibility/` | Optional MechProtocol rules, naming and CP model |
| Regression | `Validation/` | Behavior, persistence, layout and performance checks |

`MainForm` remains a partial bridge for the WinForms canvas and commands.
Do not duplicate document/catalog state inside WPF views. WPF receives data
and callbacks and must not write structure JSON/XML directly.

## Human-facing information hierarchy

1. **Application menu** — file/edit/object/view/window/help, always available
2. **Open documents** — active document clearly distinguished; a separate
   close target retains the existing unsaved-change confirmation
3. **Library / canvas / properties** — choose an asset or structure on the
   left, perform editing in the middle, inspect selection on the right
4. **Canvas controls** — New/Open, current editing layer and layer visibility;
   keep these separate from document tabs
5. **CP footer** — secondary contextual feedback below the object inspector;
   cannot consume the entire inspector at narrow sizes

Library workflow: search → scope → type → category → source → favorites/reset.
Avoid repeating the active page heading in its content. Folder rows need
distinct icon/disclosure/count and files need a different icon and indentation.
Long labels have accessible tooltips rather than spilling into adjacent columns.

Inspector workflow: selected object's name/DefName → origin/footprint →
material (if eligible) → orientation (if eligible) → dormancy (if eligible).
Only show fields applicable to the selected object. Project/export settings
remain in the separate **工程** tab.

## Layout ownership

- `MP_StudioTheme`: only source for dark palette and common spacing
- `MP_SectionTabs`: permanent WinForms page host and DPI-correct WPF header
- `MP_InspectorLayout`: inspector page/row composition and CP footer sizing
- `MP_FluidTabBar`: fits labels to the WPF viewport; no drawing over sibling
  controls
- `MP_FluidStructureLibrary`: folder/file hierarchy and selection
- `MP_FluidAssetPalette`, `MP_CpMotionView`: virtualized catalog lists
- `MP_FluidCpSummary`: display of the compatibility CP calculation only
- `MP_FluidDocumentBar`: document tabs; does not own document persistence

Never place a WPF header in a fixed-size WinForms row without DPI conversion.
Viewport rows retain independent bounds. Fields must accommodate font height.
CP footer height is capped so editing always has priority.

## Rendering/performance boundaries

- Decoded textures are cached and shared, not re-encoded per scroll event
- Coalesce bursts of thumbnail refreshes to a single dispatcher pass
- Generate expensive structure previews on a cancellable STA worker; a stale
  selection must never replace a newer preview
- Do not destroy/rebuild whole side pages on every tab switch
- Keep selected-row fills within the selected row. Animate bounded edges
  without causing overlapping hit targets, text, or thumbnail artifacts
- Honor reduced motion; instantaneous selection must remain functional

## Validation

Automatic Windows CI runs the release publish, `--ui-smoke-test`
(including offline JSON/XML and deep-copy contracts) and
`--catalog-cache-test`. It checks visible page ownership, toolbar/page
boundaries and common DPI/window sizes without requiring the user's game.

The full game-data suite is `--self-test --game <RimWorld-path>` and exercises
materials, editing, undo/redo, document sessions, naming and export.
It requires real RimWorld files and is not covered by the offline CI.

**Scope of assurance:** automated checks cannot prove there are no errors or
substitute for Windows 100–200% DPI visual/interaction testing. Investigate
actual overlapping rectangles, rapid tab switches and GPU paint artifacts
rather than attempting to mask errors with palette or animation changes.
