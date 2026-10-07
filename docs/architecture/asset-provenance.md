# Asset provenance

## Yoake-owned assets in this milestone

All SVG files committed under `assets/icons/app` and `assets/icons/functional` in this foundation were newly authored for this repository. They are not 1:1 copies of Aegisub or Kainote artwork.

Functional icons intentionally use a single theme-neutral SVG geometry per action. Avalonia supplies color dynamically through theme resources (`IconForegroundBrush`, `IconAccentBrush`, disabled-state visuals, and future semantic brushes). Do not create parallel light/dark raster icon packs.

## Aegisub visual reference

Aegisub Toshi-ban is used as a visual/function-language reference for familiar subtitle-editor actions. Its top-level Aegisub license is the 3-clause BSD-style license and states that files without their own license header are covered unless a directory/file has another license. This milestone deliberately imports **no Aegisub icon file** and does not raster-trace its light/dark bitmap sets.

If a future icon is actually derived from or adapted from an Aegisub asset, record the source repository, exact path + commit, applicable license, modifications, and required attribution here. Normalize the result into Yoake's theme-aware SVG system rather than carrying separate light/dark copies. Functional-icon reference or reuse does not permit using Aegisub's application logo as Yoake identity. Kainote artwork must not be copied without explicit compatible licensing confirmation.

The compact numeric stepper uses original Yoake `step-up.svg` and `step-down.svg` geometry through the same generated catalog and theme foregrounds. Its template follows the native Avalonia 12.1.3 named-part contract; Avalonia’s MIT notice remains in the existing license bundle.
# Style-list movement icons (UX correction pass)

The `style-{up,down,top,bottom,sort}.svg` sources adapt actual Aegisub/Toshi-ban
paths. Exact paths, source commit, license, attribution and explicit removal of
decorative strokes are recorded in THIRD_PARTY_NOTICES.md. Coordinates are
normalized at authoring time; runtime recoloring uses `IconForegroundBrush`.
The path-only generator requires no broader runtime SVG support for these assets.
