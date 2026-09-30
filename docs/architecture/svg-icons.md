# SVG icon system

Functional icon **source of truth is real SVG** under `assets/icons/functional`. M0 intentionally supports only a narrow monochrome subset: a `0 0 24 24` viewBox containing path geometry with no transforms, strokes, explicit fill/clip rules, raster images, circles/rectangles, or semantic color layers. `tools/generate-icons.py` extracts the path data into `IconGeometries.g.cs`. Each generated geometry is lazily created on first use, so the app does not parse every icon at startup. The app never parses SVG/XML at runtime and does not need a WebView or SVG dependency.

Avalonia `PathIcon.Foreground` supplies semantic color. Light/Dark dictionaries provide `IconForegroundBrush`; active tools can use `IconAccentBrush`; disabled controls inherit control disabled visuals. Functional icon geometry is theme-neutral: do not create separate light/dark copies merely to change color. One SVG source should recolor through semantic theme resources. Warning/error artwork may later use explicitly documented semantic layers.

This is **not a general SVG renderer**. Future reuse of Aegisub or other sophisticated SVG artwork must either normalize that artwork into the supported subset or deliberately expand the generator with tests for transforms, strokes, fill rules, primitive elements, and multi-layer recoloring. Sol/Luna should never infer “all SVG is supported” from this pipeline.

The application identity SVG is separate under `assets/icons/app`. On Windows, `tools/generate-app-icon.ps1` rasterizes that SVG master into a multi-resolution `.ico` before build; MSBuild uses it as the executable icon and Avalonia includes the same generated icon as the default window icon. Functional icons remain SVG-only.
