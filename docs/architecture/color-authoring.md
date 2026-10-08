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
ScreenColorDropper captures pointer input inside the dialog and displays 7×7
physical source pixels at 8× magnification. Drag release or a latched click
accepts the center; neighboring frozen pixels remain clickable. Physical cursor
coordinates support negative monitor origins and DPI scaling. Escape/right-click
or capture loss cancels sampling without changing the chosen color; sampling
preserves alpha. Non-Windows platforms disable the screen action. Mixed-DPI and
real platform capture behavior still require manual Windows verification.
Hue and alpha gradients use ColorStrip's thin black/white marker, direct pointer
input and arrow/Home/End keys rather than a templated Slider thumb. Spectrum/strip
input keeps its continuous HSV state while deriving 8-bit RGB; a round-trip
through quantized RGB never moves the active hue marker or loses hue on gray.

FontPicker caches asynchronous installed-family enumeration. Its visible dropdown
opens a virtualized full list immediately, independently of the search prefix.
Typing still searches and accepts an exact family. Unavailable names are marked
without substituting their stored value.

Both style colors and selection formatting use the same history file beside the active settings profile, including portable profiles. Collection/list splitter proportions are stored through the settings command, and collection editing stays collapsed until needed.
