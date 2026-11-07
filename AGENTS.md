# Structure Editor · 结构工坊

- This is an independent Windows editor project. Do not edit the original MechProtocol mod to implement editor features.
- Product text and default workflows are generic. Mod-specific rules belong only in compatibility adapters.
- Never commit game assets, game DLLs, local settings, absolute user paths, generated binaries or migration backups.
- Build with `dotnet build src/StructureEditor/StructureEditor.csproj`; texture libraries are read from the user's game at runtime.
- Preserve document compatibility and default new palette assets to awake; pickup/copy keeps original state.
