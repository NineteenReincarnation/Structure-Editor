# Structure Editor · 结构工坊

- **Development branch: `main`.** Work directly against the newest `main` unless the maintainer explicitly requests a PR or feature branch.
- Architecture and module responsibilities: `docs/architecture.md`.
- Prioritize complete, user-visible functionality over repeated cosmetic changes and unnecessary refactors.
- Independent Windows editor: never edit MechProtocol to implement an editor feature. Generic flows remain independent; Mod-specific rules stay in `src/StructureEditor/Compatibility/`.
- The C# source is grouped under `src/StructureEditor/{App,Canvas,Catalog,Core,Formats,UI,Validation,Compatibility}/`. Preserve the `StructureEditor` namespaces and `partial MainForm`/canvas composition unless explicitly changing architecture.
- Never commit game assets or DLLs, local settings, absolute paths, generated binaries, migration backups, or third-party resources without suitable permission.
- Build with `dotnet build src/StructureEditor/StructureEditor.csproj`; textures are loaded from the user's game at runtime.
- Preserve document compatibility. Newly placed assets default to awake when supported; picked or copied assets keep their original state.
- Keep README.md and README.en.md as the marketing landing pages; put engineering details in `docs/`.
