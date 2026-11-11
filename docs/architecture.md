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
| Compatibility | `Compatibility/` | Optional MP integration, cluster naming and CP |
| Regression | `Validation/{Core,Interaction,Structures,Workspace,Performance,Smoke}/` | Offline and RimWorld-backed checks |

`MainForm` remains a partial bridge for the WinForms canvas and commands.
Do not duplicate document/catalog state inside WPF views. WPF receives data
and callbacks and must not write structure JSON/XML directly.

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

SDK recursive compilation includes all module directories without namespace
or assembly changes. No structure DefName, JSON/XML schema, export profile,
`structures/` layout, resource filename or existing external runtime file
should change in an architectural refactor. The generated `app/` remains
the sole CI-managed output, not a manually edited source directory.

Verify module placement with `scripts/check-architecture.ps1`; CI then
runs Windows publish, `--ui-smoke-test` and `--catalog-cache-test` before
synchronizing the executable into `main:app/`.

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
