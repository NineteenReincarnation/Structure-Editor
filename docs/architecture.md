# Structure Editor — Architecture and UI invariants

## Purpose and ownership

The editor creates and edits RimWorld structures. New work must retain
Structure Framework XML and existing JSON compatibility; imported historical
formats are handled by the existing codecs. **Never couple the domain model to
the visible UI.** MP_ prefixes remain mandatory for new project-specific Defs.

| Layer | Directory | Responsibility |
| --- | --- | --- |
| Application | `App/` | Startup, settings, branding and command-line dispatch |
| Domain | `Core/Models/`, `Core/Services/` | Structures, spatial indexing, safe rename |
| Canvas | `Canvas/Editing/`, `Canvas/Rendering/` | Placement, selection, drawing and preview |
| Catalog | `Catalog/{Discovery,Collections,Textures,Caching,Metadata}/` | Game defs, queries, textures, cache and metadata |
| Import/export | `Formats/{Codecs,Export}/` | Structure Framework XML, legacy imports, dependencies |
| Workspace | `UI/Workspace/` | MainForm partial controllers, menus, sessions, saves, shortcuts |
| WinForms bridge | `UI/Controls/` | Native controls and WPF page host |
| WPF presentation | `UI/Wpf/{Navigation,Libraries,Inspector,Motion,Primitives}/` | Visible tabs, lists, inspectors and motion |
| UI services | `UI/{Audio,Theme,Motion,Help}/` | Audio mixer, pure sound synthesis, theme and helpers |
| Infrastructure | `Infrastructure/Paths/` | Shared app data locations, avoiding reverse dependencies on Program |
| Compatibility | `Compatibility/` | Optional MP integration, cluster naming and CP |
| Regression | `Validation/{Core,Interaction,Structures,Workspace,Performance,Smoke}/` | Offline and RimWorld-backed checks |

`MainForm` remains a partial bridge for the WinForms canvas and commands.
Do not duplicate document/catalog state inside WPF views. WPF receives data
and callbacks and must not write structure JSON/XML directly.

## Installed program folder

```text
app/
└── StructureEditor.exe   # double-click to start (requires .NET 10 Desktop Runtime)
```

The executable is a framework-dependent .NET single-file publish. Managed
dependencies (including NAudio), `.deps.json` and `.runtimeconfig.json` are
bundled into it. The runtime must still be installed on Windows. Do not
confuse these bundled dependencies with RimWorld's external game files;
game assets continue to be read from the local installation as before.

`scripts/build.ps1` removes only known old generated files, not the whole
user-visible `app/` directory. The repository publishes no loose DLLs and
does not add a second launcher, ZIP or dist directory. CI runs
`app/StructureEditor.exe --ui-smoke-test` and
`app/StructureEditor.exe --catalog-cache-test` directly.

Existing untracked files in a user's local `app/` directory cannot be
deleted through GitHub Desktop pull. After pulling the updated main, run
`pwsh ./scripts/organize-app.ps1` once to move known old documents to
`docs/app-legacy/` and local icon sources to `branding/`, and remove known
obsolete runtime files. The script never deletes unknown user files.
Preview changes first with `-WhatIf`.

## Repository structure and module rules

```text
Structure-Editor/
├── .github/workflows/      CI build and tracked app/ sync
├── app/                    Generated executable; never edit manually
├── assets/                 README illustrations, not game textures
├── docs/                   Architecture, design standards, UI and dependency notes
├── scripts/                Build, module-boundary check, game-data verification
├── src/StructureEditor/
│   ├── App/                Program and branding
│   ├── Canvas/             Editing/ and Rendering/
│   ├── Catalog/            Discovery/, Collections/, Textures/, Caching/, Metadata/
│   ├── Compatibility/      Optional integration with other mods
│   ├── Infrastructure/     Shared environment and filesystem paths
│   ├── Core/               Models/ and Services/
│   ├── Formats/            Codecs/ and Export/
│   ├── UI/
│   │   ├── Workspace/      MainForm and controller partial classes
│   │   ├── Controls/       Native widgets and hybrid hosting
│   │   ├── Wpf/            Navigation/, Libraries/, Inspector/, Motion/, Primitives/
│   │   ├── Audio/          Mixer and independent gamepad sound synthesis
│   │   └── Theme/, Motion/, Help/
│   └── Validation/         Core/, Interaction/, Structures/, Workspace/,
│                           Performance/, Smoke/
└── structures/             Only canonical editable structure JSON/XML library
```

The boundary is **Domain → Workspace → Presentation**: Core, Catalog,
Formats and Compatibility never store Form controls, event handlers or WPF
view state. The Canvas owns interactive editing and rendering, but the
Workspace owns the active document session, undo stacks and persistence.
WPF views receive data and callbacks rather than writing structure files.

`CatalogCollections` now contains only the collection model; the former
favorites, source-dialog and filtering controls are in
`UI/Workspace/MP_PaletteCollectionCommands.cs`. `MP_GamepadSoundDesign`
produces deterministic sound samples without audio-device side effects;
`MP_UiSound` separately owns the NAudio mixer, sound settings and fallback.
The offline UI smoke checks moved from the entry point to
`Validation/Smoke/MP_UiSmokeChecks.cs`.

### Internal refactor: application and workspace responsibilities

The public editor behavior is unchanged; these are **source ownership**
changes within the same `StructureEditor` assembly and namespace:

- `App/Program.cs`: startup dispatch and error reporting only.
- `App/MP_ProgramLocations.cs`: existing directory lookup and Steam/RimWorld
  discovery methods (unchanged lookup order and fallback).
- `App/MP_ProgramSettings.cs` / `App/StudioPaths.cs`: existing settings
  persistence and JSON-compatible settings model, separate from startup.
- `UI/Workspace/MainForm.cs`: main window fields, constructor, event hookups
  and common widget factory only.
- `UI/Workspace/MP_PaletteCollectionCommands.cs`: collection filters,
  material palette drawing, sources, favorites and brush activation.
- `UI/Workspace/MP_InspectorController.cs`: selected-object inspector.
- `UI/Workspace/MP_DocumentStateController.cs`: undo/redo, metadata and title.
- `UI/Workspace/MP_DocumentDialogs.cs`: native file dialogs and user file
  actions, separate from document state.
- `UI/Workspace/MP_WorkspaceDocument.cs`: one document tab's session fields,
  separate from `WorkspaceDocuments.cs` session coordination.

These remain `partial MainForm` and `partial Program` declarations, not
new data stores or a second controller implementation. Methods and their
original expressions have been moved verbatim. The module validation step
checks ownership to prevent incidental re-coupling. This does **not** change
window layout, keyboard navigation, audio samples, timers, XML/JSON data,
RimWorld asset loading, save paths or the single-file `app/` distribution.

SDK recursive compilation includes all module directories without namespace
or assembly changes. No structure DefName, JSON/XML schema, export profile,
`structures/` layout, resource filename or existing external runtime file
should change in an architectural refactor. The generated `app/` remains
the sole CI-managed output, not a manually edited source directory.

Verify module placement with `scripts/check-architecture.ps1`; CI then
runs Windows publish, `--ui-smoke-test` and `--catalog-cache-test` before
synchronizing the executable into `main:app/`.

## Cohesion and dependency boundaries

The editor retains the same single Windows assembly and observable behavior,
but cross-layer logic has been extracted into data-only services.

- `Core/Services/MP_DocumentHistory.cs` owns snapshot copying, bounded
  history, undo/redo and per-document stack export/restore. `MainForm`
  passes the active document but no longer owns raw undo/redo stacks.
  Workspace tab changes preserve the original top-of-stack-first arrays.
- `Catalog/Collections/MP_CatalogPaletteQuery.cs` orders and filters plain
  catalog items, receiving scope/favorites/recent IDs explicitly rather than
  looking up `Program.Paths` or reading controls. Existing category/pack
  search and selection semantics are unchanged.
- `Formats/Export/MP_StructureStorage.cs` owns JSON/XML persistence,
  duplicate checks, XML merging and backups. The controller in
  `UI/Workspace/WorkspaceSaving.cs` only coordinates user dialogs/state.
- `Infrastructure/Paths/MP_DataDirectories.cs` supplies the same
  LocalApplicationData directory to both storage and the entry point;
  `Program.DataDirectory` remains an alias for existing callers.
- `App/WorkspaceReference.cs` is a pure persisted-settings DTO, separate
  from `UI/Workspace/WorkspaceDocuments.cs`. Names and JSON fields remain
  unchanged.

`Validation/Core/MP_DocumentHistoryChecks.cs` and
`Validation/Core/MP_CatalogPaletteQueryChecks.cs` run in the existing
offline smoke test. They check history ordering and limits, session recovery
and catalog source/favorite/recent filtering without installing RimWorld.
The architecture script also rejects accidental Program/UI references in the
newly isolated services.

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
