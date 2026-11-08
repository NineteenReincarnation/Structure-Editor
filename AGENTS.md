# Structure Editor · 结构工坊

- **Development branch: `main`.** Work directly against the newest `main` unless the maintainer explicitly requests a PR or feature branch.
- Architecture and module responsibilities: `docs/architecture.md`.
- Prioritize complete, user-visible functionality over repeated cosmetic changes and unnecessary refactors.
- Independent Windows editor: never edit MechProtocol to implement an editor feature. Generic flows remain independent; Mod-specific rules stay in `src/StructureEditor/Compatibility/`.
- The C# source is grouped under `src/StructureEditor/{App,Canvas,Catalog,Core,Formats,UI,Validation,Compatibility}/`. Preserve the `StructureEditor` namespaces and `partial MainForm`/canvas composition unless explicitly changing architecture.
- Do not commit game assets or DLLs, local settings, absolute paths, migration backups, or unlicensed third-party resources. The only allowed generated binary exception is the CI-produced `app/` runtime package, tracked by the Windows build workflow so GitHub Desktop pull refreshes the app. Do not hand-edit or manually stage `app/`.
- Build with `dotnet build src/StructureEditor/StructureEditor.csproj`; textures are loaded from the user's game at runtime.
- Preserve document compatibility. Newly placed assets default to awake when supported; picked or copied assets keep their original state.
- Keep README.md and README.en.md as the marketing landing pages; put engineering details in `docs/`.

- User-authored structure JSON and XML files live under root `structures/`; this is the first library source, paired by DefName. Keep user source documents editable and avoid machine-specific absolute XML export paths.
- Left library UI uses integrated tabs and PNG preview; do not re-introduce a modal structure picker or a redundant DefName expansion level.

- Renaming a library structure must update editable JSON, its linked XML output (including separate configured export directories), matching preview PNGs, and open/recent-document paths. Only edit matching XML Defs; back up sources and never overwrite unrelated files. Keep the `MP_` prefix for newly renamed DefNames.

- Document tabs use `MP_DocumentTabButton` with a per-tab close X; preserve unsaved-save confirmation and prevent X-click activation. Ctrl+G/H/J toggle explosion/detection/shield previews, Ctrl+D duplicates objects.
- Do not regress to native white WinForms tab-header gaps, light palette scrollbars or white combo arrow wells; keep the integrated `MP_SectionTabs` and the dark control surfaces.

- Repository cleanup: keep one canonical editable root `structures/` and root `docs/`. Do not recreate `examples/` or a launcher in the repository root. CI-tracked `app/` contains only runtime files; `build.ps1 -SelfContained` bundles the structure library and license for portable distribution. Run `app/StructureEditor.exe` directly. All validation code stays in `src/StructureEditor/Validation/`.

- **Canonical XML format:** Structure Framework only (`StructureFramework.StructureDef`). Normalize legacy profiles on load; do not reintroduce export format selection or separate MP dormancy toggles. Old PrefabDef and MechProtocol XML remain importable. Generate dormancy modifiers and dependencies automatically for explicit supported states. Keep MP cluster naming independent of export profile.
