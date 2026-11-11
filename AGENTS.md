# Structure Editor · 结构工坊

- **Development branch: `main`.** Work directly against the newest `main` unless the maintainer explicitly requests a PR or feature branch.
- Architecture and module responsibilities: `docs/architecture.md`.
- Prioritize complete, user-visible functionality over repeated cosmetic changes and unnecessary refactors.
- Independent Windows editor: never edit MechProtocol to implement an editor feature. Generic flows remain independent; Mod-specific rules stay in `src/StructureEditor/Compatibility/`.
- The code is organized by module under `src/StructureEditor/{App,Canvas,Catalog,Core,Formats,UI,Validation,Compatibility}/` (see `docs/architecture.md`). `UI/Workspace/` owns all `partial MainForm` controllers. `UI/Wpf/{Libraries,Navigation,Inspector,Motion,Primitives}/` owns WPF views. Shared WinForms controls, audio, themes and animation live under `UI/{Controls,Audio,Theme,Motion,Help}/`. Never place MainForm/UI logic in Core, Catalog, Formats or Compatibility; keep the document model independent of presentation. Validation includes `Validation/Smoke/MP_UiSmokeChecks.cs`, dispatched by `App/Program.cs`.
- Do not commit game assets or DLLs, local settings, absolute paths, migration backups, or unlicensed third-party resources. The only allowed generated binary exception is the CI-produced `app/` runtime package, tracked by the Windows build workflow so GitHub Desktop pull refreshes the app. Do not hand-edit or manually stage `app/`.
- Build with `dotnet build src/StructureEditor/StructureEditor.csproj`; textures are loaded from the user's game at runtime.
- Preserve document compatibility. Newly placed assets default to awake when supported; picked or copied assets keep their original state.
- Keep README.md and README.en.md as the marketing landing pages; put engineering details in `docs/`.

- User-authored structure JSON and XML files live under root `structures/`; this is the first library source, paired by DefName. Keep user source documents editable and avoid machine-specific absolute XML export paths.
- Left library UI uses integrated tabs and PNG preview; do not re-introduce a modal structure picker or a redundant DefName expansion level.

- Renaming a library structure must update editable JSON, its linked XML output (including separate configured export directories), matching preview PNGs, and open/recent-document paths. Only edit matching XML Defs; back up sources and never overwrite unrelated files. Keep the `MP_` prefix for newly renamed DefNames.

- Document tabs use `MP_DocumentTabButton` with a per-tab close X; preserve unsaved-save confirmation and prevent X-click activation. Ctrl+G/H/J toggle explosion/detection/shield previews, Ctrl+D duplicates objects.
- Do not regress to native white WinForms tab-header gaps, light palette scrollbars or white combo arrow wells. `MP_SectionTabs` now uses a WPF animated navigation header; WPF owns the visible document, asset, structure-library and CP presentation. Do not reintroduce invisible duplicate WinForms tree/list views or a second code path for placing assets.
- **Motion language:** port lilt's response/damping spring model for travelling outlines, press feedback and inertial scrolling. Never animate opaque selection fill across neighbouring rows or folder headers; each selected row owns its own fill. Bound wave displacement below row margins, clip overscroll within ScrollViewer content, and reserve a 12-DIP scrollbar gutter.
- **Pixel presentation:** use the shared palette and spacing in `MP_StudioTheme` (details in `docs/ui-style.md`) for WinForms and WPF alike; avoid independently hardcoded backgrounds, doubled borders, gradient/gloss, and mismatched font scales. Keep lilt inverted selected rows and the travelling outline. Prefer installed Unifont or user-provided `app/Fonts/unifont.otf`; use aliased pixel-snapped typography, with original pixel readouts for short ASCII counters. Preserve keyboard support and reduced motion settings.

- Repository cleanup: keep one canonical editable root `structures/` and root `docs/`. Do not recreate `examples/` or a launcher in the repository root. Publish the framework-dependent **single-file** `app/StructureEditor.exe` only; runtime DLLs, deps.json, runtimeconfig.json, marker files, documentation and icon sources must not clutter the visible `app/`. The Windows CI runs the executable itself for offline tests and commits only generated `app/` output. Never delete the entire local app folder or erase untracked user content. Use `scripts/organize-app.ps1` once after pulling the new main to safely migrate old local app docs/icon sources. No ZIPs or extra launchers. All validation stays in `src/StructureEditor/Validation/`.

- **Canonical XML format:** Structure Framework only (`StructureFramework.StructureDef`). Normalize legacy profiles on load; do not reintroduce export format selection or separate MP dormancy toggles. Old PrefabDef and MechProtocol XML remain importable. Generate dormancy modifiers and dependencies automatically for explicit supported states. Keep MP cluster naming independent of export profile.

- Branch cleanup: keep development and CI commits on `main` only; do not push, synchronize, delete, or rename any other branch. The repository owner handles other branches manually.

- Do not reintroduce the obsolete document category selector. New JSON and Framework XML omit Category/category/structureCategory; legacy files containing them remain readable. MP cluster naming is a collapsible optional tool recognized from the MP_Cluster_ DefName prefix, not a general document type.

- Connected fabric graphics: AnimalFlap uses neighbor-aware frame seam selection when directly adjacent, without treating its ordinary directional sprite as a 4x4 wall atlas. Test both adjacent and separated placement; do not crop isolated curtains or change XML semantics.
