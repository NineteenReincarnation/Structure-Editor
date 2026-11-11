# lilt motion adaptation

Source: https://github.com/chhongzh/lilt (branch `dev`, inspected 2026-11-10).

The C# motion code in `src/StructureEditor/UI/Wpf/Motion/MP_LiltPhysics.cs` adapts
lilt's `anim.Value`, spring presets, fixed-substep integration and
focus-cursor lead/trail model for WPF. The original lilt Go framework is not
embedded or invoked by Structure-Editor.

MIT License

Copyright (c) 2026 chhongzh

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Unifont

GNU Unifont is a separate font project, licensed under OFL 1.1 or GPL 2+
with a font embedding exception. It is not redistributed by Structure-Editor.

Install the official Unifont OpenType font on Windows:
https://unifoundry.com/unifont/index.html

The editor prefers `Unifont` where installed; it falls back to
`Microsoft YaHei UI` when unavailable. Font files should not be committed to
the project. Restart the editor after installing the font.

You may also place the official `unifont.otf` in `app/Fonts/` next to
`StructureEditor.exe`; the desktop app will load it privately for both WPF
and WinForms, without installing it system-wide. Restart after copying it.
Only the loader is included; this repository does not distribute font files.
