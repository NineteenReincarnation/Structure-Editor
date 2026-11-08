<h1 align="center">Structure Editor</h1>

<p align="center"><strong>A Visual Structure Editor for RimWorld</strong></p>

<p align="center">
  <a href="./README.md"><img src="https://img.shields.io/badge/%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87-%E5%88%87%E6%8D%A2-0969da?style=for-the-badge" alt="切换到简体中文"></a>
  <a href="./README.en.md"><img src="https://img.shields.io/badge/English-Current-238636?style=for-the-badge" alt="English (current)"></a>
</p>

<p align="center">
  <a href="./LICENSE"><img src="https://img.shields.io/badge/License-MIT-2ea043?style=flat-square" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/RimWorld-1.7-986f4b?style=flat-square" alt="RimWorld 1.7">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-3776ab?style=flat-square" alt="Windows x64">
  <a href="https://github.com/NineteenReincarnation/Structure-Editor/stargazers"><img src="https://img.shields.io/github/stars/NineteenReincarnation/Structure-Editor?style=flat-square&color=e3b341" alt="GitHub Stars"></a>
</p>

---

Still writing XML by hand to design buildings, mechanoid clusters, and special event scenes in RimWorld?

**Structure Editor** is a visual editing tool for RimWorld that makes designing complex map structures more intuitive and convenient.

With its grid-based editor, you can place buildings, adjust their positions, plan defensive outposts, and see the entire layout as you work—much like building directly in the game.

## Launch and folders

Open **[`app/StructureEditor.exe`](./app/StructureEditor.exe)** from the repository. After pulling `main`, CI updates this executable directly. No separate root-level launcher is needed. You may create a personal Windows shortcut targeting `app/StructureEditor.exe` (local `.lnk` files are not tracked).

- `app/`: generated runtime files only; do not edit or store structures here.
- `structures/`: the single editable JSON/XML structure library.
- `docs/`: user and engineering documentation; `src/`: code; `scripts/`: build and validation.
- No ZIP or standalone archives are generated or uploaded. Windows CI updates only `main/app/` with the executable and runtime files; pull `main` and run `app/StructureEditor.exe` from the repository.

## Key Features

- **Visual structure editing**: Place, move, rotate, and remove objects on an intuitive grid-based canvas.
- **Game asset library**: Browse and search building assets, with filters for collections, types, categories, and sources.
- **Multiple structures**: Work on different layouts using tabs.
- **Convenient editing tools**: Use selection boxes, panning, zooming, and keyboard shortcuts to speed up your workflow.
- **Structure properties**: Customize structure identifiers, display names, categories, and save directories.
- **Structure export**: Save projects as JSON and export structure XML for RimWorld.

Whether you're building a mechanoid cluster, an enemy outpost, a unique building complex, or a special event scene, you can plan everything in a standalone editor without repeatedly launching the game to make adjustments.

**From tedious coordinate editing to intuitive visual design.**

Structure Editor aims to make RimWorld mod structure creation simpler and more efficient, giving creators more time to focus on the design itself.

---

<div align="center">
  <p>If this project is useful to you, consider giving it a <strong>Star</strong> to support development.</p>
  <a href="https://github.com/NineteenReincarnation/Structure-Editor">
    <img src="./assets/star-prompt.gif" width="360" alt="Star Structure Editor on GitHub">
  </a>
</div>
