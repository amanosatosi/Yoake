# Aegisub Toshi-ban migration inventory

This inventory is based on the inspected local repository at
`C:\aegisub source\Aegisub_Toshi-ban-ichi-tsuiseki`. It prevents useful
Toshi-ban behavior from disappearing merely because the old implementation is
wx-coupled.

## Foundation-critical

| Capability | Reference areas | Yoake direction |
| --- | --- | --- |
| ASS data and compatibility | `ass_*`, `subtitle_format_ass.*`, `libaegisub/ass/*` | Extract a Qt/wx-free core behind `ass::Document`; retain extradata, attachments, project metadata, and edge cases. |
| Independent state | `context.h`, `subs_controller.*`, `selection_controller.*` | One `DocumentContext` per tab with stable IDs and no view pointers. |
| Mangetsu | `subtitles_provider_libassmod.*`, `async_video_provider.*`, `docs/mangetsu.md` | Mangetsu-only explicit session per document; port RGBA, tag images, attachments, font state, and metadata invalidation. |
| Syntax-aware editing | `libaegisub/ass/dialogue_parser.*`, `subs_edit_ctrl.*`, `ass_font_tag_selection.*` | One syntax service with UTF-16/source mapping, permissive future tags, semantic parameters, and intelligent value spans. |
| Themes | `themes/*.json`, `theme_preset.*` | Named atomic semantic palettes; never copy colors into scattered global options. |
| Responsive media | provider/controller/render files | Strong per-document source leases, explicit generations, background analysis, GUI-only presentation. |

The responsive-media row now has a concrete Yoake implementation: FFMS2 5.0
owns indexed video/audio sources; `FrameTimeMap` owns VFR timecodes; persistent
per-document video, playback, waveform, and spectrum lanes isolate expensive
work; Qt Quick paints only accepted frames/visualization images; and Qt's media
API is limited to final PCM output. The fork's
FFMS2 cache-validation and monotonic-request lessons were retained without
porting its wx/provider object graph.

## First usable milestone

- normal file/new/save/save-as/close and unsaved-document workflows;
- grid selection, insert/delete/duplicate/join/split, editable line fields, styles,
  comments, find/replace, and per-document undo;
- video/media open, exact frame/time mapping, seek/step/play, keyframes/timecodes,
  and Mangetsu preview;
- audio waveform/spectrum, playback, seek, timing ranges, and timing commands;
- Original K-Timing as a separate policy/session;
- Toshiki K-Timing, logical cuts versus explicit boundaries, partial timing,
  immediate drag, reset/commit, tag selection, split audition, and existing-k
  preservation;
- Toshiki Auto Cut using the current Unicode/codepoint behavior while retaining
  override/tag anchors;
- Better View `\N` display with one shared raw/display offset mapping;
- selection-aware tag/color insertion, transform scopes, `LOCK` semantics,
  preserve-timing karaoke cuts, and actor/effect workflows; and
- useful command coverage represented by File/Edit/Subtitle/Timing/Video/Audio/
  View/Help, without recreating wx menu structure blindly.

## Later migration

- Mangetsu gradient editor and fixed-frame `\pgrd` placement;
- Fast Naming, actor MRU, carry-over, and Nanashi navigation;
- Japanese bracket insertion helpers;
- Join Next/Join Last timing workflows;
- style import conflict/resolution handling;
- relative-time insertion and Force Zoom;
- playback-speed UI and multi-audio metadata;
- alignment picker, motion tracking, line folding, perspective tools, video
  panning, and stereo-audio refinements;
- file-in-use warning and the Toshi-ban save-completion workflow; and
- spelling, translation/styling assistants, export, attachments, and font
  collection after their model/UI boundaries are clean.

## Deliberately excluded

- OCR and Image-to-Text;
- Automation and Lua scripting; and
- a replacement plugin scripting system during foundation work.

## K-Timing migration rules

The proposed Toshiki mode will share Qt/wx-free parsing primitives with the
implemented Original `KaraokeSession`, but it will remain a separate policy
rather than a mode flag inside the Original controller.

The future Toshiki session will own an immutable original line, stable logical-slot IDs, rich text
and override anchors, optional explicit boundaries, selected slot, preferred
karaoke type/spelling, and dirty state. UI markers are immutable DTOs addressed
by stable IDs; a mouse press returns the ID that should begin dragging.

Compatibility rules carried from current Toshi-ban include:

- Original cuts divide duration by Unicode character count while conserving the
  exact total; boundaries are neighbor-clamped and Ctrl-drag can move a suffix.
- Toshiki logical splits never invent time boundaries. Only an ordered prefix of
  explicit boundaries is visible; later slots remain logically untimed.
- adding a split to existing karaoke can create a coincident zero-duration
  boundary without redistributing surrounding timing;
- a final empty slot represents remaining silence;
- single ordinary untagged slots commit as a no-op;
- selector types are `\k`, `\K`, `\kf`, and `\ko`, with source spelling kept
  separately from semantic type; and
- reset discards the edit session, while commit performs one document mutation
  containing all text and timing changes.

Auto Cut must port the codepoint tables and song-oriented behavior for small
kana, sokuon, long vowels, o-row plus `う/ウ`, halfwidth kana, spaces/fullwidth
spaces, brackets, CJK, Roman runs, `#`/fullwidth `＃`, and `||` rests. Unlike the
current implementation, rebuilding logical slots must not discard non-karaoke
override tags.

## Risks tracked from the reference

- provider replacement can destroy audio data before callback users release raw
  pointers;
- old video events have no session generation and can cross a media switch;
- synchronous subtitle export bypasses the video worker;
- renderer startup can deadlock when a worker synchronously waits on the main
  thread during main-thread teardown;
- waveform analysis executes in painting, and a zoom change can outgrow its
  initial sample buffer;
- mutable Mangetsu library state is shared across providers without a proven
  multi-session contract;
- global theme application is non-atomic;
- static dialogue ID generation is not safe for concurrent document loads; and
- Toshiki Auto Cut currently loses non-karaoke override tags when rebuilding
  slots.

Each migrated subsystem needs tests that make these failures impossible rather
than merely reproducing its existing class structure.
