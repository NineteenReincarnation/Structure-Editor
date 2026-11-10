# Structure Editor · Unified UI style

## Intent
One continuous dark instrument-panel interface, not three independent themes.
Use lilt's spring motion and Unifont's pixel identity without reducing the
readability of RimWorld textures, Chinese labels, inspector values or DefNames.

## Canonical implementation
- Colors and spacing: `src/StructureEditor/UI/MP_StudioTheme.cs`.
- Fonts and pixel snapping: `UI/Wpf/MP_LiltTheme.cs`.
- Spring animation: `UI/Wpf/MP_LiltPhysics.cs`.
- WPF and WinForms should derive their surface colors from **the same** tokens.
  Do not add another local black/blue palette to new panels.

## Surface hierarchy
- Window / toolbar: one continuous restrained dark strip
- Navigation / inspector / CP panel: shared `Panel` surface
- Group headers / previews: `PanelRaised`, separated by 1px `Divider`
- Form fields: `Input`, same border and arrow-well treatment
- Canvas: slightly warmer `Canvas` surface to preserve tile contrast
- Highlight: `Active` fill belongs to **the selected row only**, with
  `ActiveText` foreground; the traveling lilt cursor draws a fine outline
  rather than an opaque panel across adjacent rows

## Rhythm
- Header bands: `HeaderHeight` (40 px at 96 DPI)
- Panel content inset: `PanelInset` (10 px at 96 DPI)
- Fields: `FieldHeight` (30 px at 96 DPI), with about 7 px between rows
- Main UI font: Unifont when supplied, GDI 10 pt and WPF 14–16 DIP for primary
  interactive text; smaller sizes are reserved for secondary metadata
- Pixel-painted glyphs: short ASCII readouts (structure index, CP totals), not
  long Chinese descriptions

## Layout and interaction invariants
1. Menu/document header, left/right tab rows and canvas tools align along
   common horizontal bands; no absolute-position overlap.
2. Inspector, structure library and CP footer use matching divider and padding.
3. A list's scrollbar has its own gutter; selecting a row never hides its text.
4. The active document remains visible with many tabs or a resized window.
5. Continuous spring transitions may retarget; reduce-motion users receive
   immediate updates without delaying commands.
6. Maintain the build/startup smoke checks for hosted UI, safe stacking and
   tab-fit layout. Manual Windows screenshot review is still necessary for
   real high-DPI visual acceptance.
