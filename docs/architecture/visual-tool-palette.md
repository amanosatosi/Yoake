# Visual tool palette

Yoake uses three semantic colors for the visual toolbar and video canvas:
primary (geometry), detail (handles and guides), warning (remove/inverse and
active canvas points). The default Dawn palette uses orange, sky blue and
sunset red. Syntax highlighting, audio colors and the global app accent are
independent.

On first launch Yoake creates `themes/visual-tools.json` beside the active
`settings.json` profile. Edit the file with any text editor and restart Yoake.
Light and dark colors are selected automatically by the current app theme.
Use opaque six-digit RGB hex colors. Missing or invalid entries retain their
defaults; malformed files are never overwritten. Delete the file to regenerate
the defaults. There is no arbitrary XAML execution or new dependency.

```json
{
  "light": {
    "primary": "#B65B15",
    "detail": "#2379B3",
    "warning": "#C43D47"
  },
  "dark": {
    "primary": "#F5AD62",
    "detail": "#62B5EF",
    "warning": "#EE7272"
  }
}
```
