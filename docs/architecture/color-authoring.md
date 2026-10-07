# Canonical subtitle color authoring

AssColorDialog is shared by main-editor formatting and style fields. Its HSV/H
spectrum uses two composited gradients and a draggable crosshair, with a hue
strip and checkerboard transparency strip. RGB, HSV and HSL numeric values,
ASS &HAABBGGRR and HTML #RRGGBB all synchronize through Core ColorSpace.
HTML edits preserve independent ASS transparency: 00 opaque, FF transparent.
Original/current previews, fixed palette, exact clipboard copy/paste and OK/Cancel
remain part of the same dialog. Invalid exact input cannot be accepted.

RecentColorStore persists up to 32 accepted colors, deduplicating exact RGBA
identity and ordering most recent first. Alpha distinguishes colors. History
uses a separate atomically replaced text file so dialog writes cannot overwrite
workspace settings. No reflection serializer or dependency is introduced.

WindowsScreenColor owns screen DC and cursor APIs and releases every DC.
Temporary transparent per-screen capture windows consume the selection click;
physical cursor coordinates support negative monitor origins and DPI scaling.
Escape/right-click cancel without changing the chosen color; sampling preserves
alpha. Non-Windows platforms disable the screen action. Capture/compositor and
mixed-DPI behavior still require manual Windows verification.

FontPicker caches asynchronous installed-family enumeration. Its visible dropdown
opens a virtualized full list immediately, independently of the search prefix.
Typing still searches and accepts an exact family. Unavailable names are marked
without substituting their stored value.
