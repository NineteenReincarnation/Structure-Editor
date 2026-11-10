# Structure Editor — lilt reference visual specification

## Source and target

- Original: https://github.com/chhongzh/lilt (MIT).
- The original lilt default `ui.CurrentTheme` is monochrome black/white
  (`gfx.Black` and `gfx.White`), with an inverted focus cursor when the
  display driver supports inversion.
- For this project the user-provided PNG/GIF is the **visual reference**:
  dominant background `#080A0E`, dominant foreground / inverse fill
  `#C8E6FF`, low-relief outer surface `#0D1117`.
- Color tokens come exclusively from `src/StructureEditor/UI/MP_StudioTheme.cs`.
  Do not reintroduce a custom blue-grey gradient palette.

## Uniform surfaces

- Window, menu, toolbar, navigator, side panels and CP footer: `#080A0E`.
- Canvas and inset preview well: `#0D1117`, to preserve sprite contrast.
- Text, interactive outlines and selected row backgrounds: `#C8E6FF`.
- Selected text: `#080A0E`; secondary metadata gets one dim foreground.
- Borders are crisp 1px lines, radius 0 or at most one pixel where appropriate.
  No glossy gradients, multi-color shadows, neon cyan glow or tinted glass.
- Hover uses translucent foreground, never a new saturated background.
- Selected fill belongs to the selected item. The spring cursor is an
  independent, transparent travelling outline and cannot mask other rows.

## Typography and motion

- Unifont is preferred for all labels, including Chinese where glyphs exist.
  Users can install it or place `unifont.otf` in `app/Fonts/`.
- Use `MP_LiltTheme` for WPF/WinForms font selection and pixel snap.
- Preserve lilt-style lead/trail springs, press/release feedback and inertial
  scrolling, but never let motion obscure neighboring rows.
- Buttons follow lilt's outline/fill-inversion model. Avoid a native beveled
  button look, especially on the inspector.
- At 96 DPI keep 40px header bands and consistent 10px panel insets.
- Text must remain readable at common 125%, 150% and 200% Windows scaling.

## UI checks

- Build the main editor and run Windows startup smoke checks.
- Confirm the theme token contract and selected-row contrast.
- Inspect normal and narrow windows: no tab obstruction, labels clamped,
  no scrollbar overlap, focus visible and no native white control areas.
- Only a real Windows UI screenshot validates the *rendered* look; CI
  compilation alone is not pixel-level visual acceptance.
