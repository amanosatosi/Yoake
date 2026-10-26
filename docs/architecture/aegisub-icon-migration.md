# Aegisub functional icon migration inventory

Working baseline: the local fork's src/libresrc/default_toolbar.json supplies command order; src/toolbar.cpp resolves commands and calls Command::Icon; src/command/command.h maps CMD_ICON to bitmap resources. src/bitmaps/manifest.respack and src/libresrc/libresrc.cpp supply light/dark resources. src/audio_box.cpp and src/video_box.cpp attach the audio, video and visual toolbars. src/visual_tool_vector_clip.cpp, src/visual_tool_drag.cpp and src/dialog_style_manager.cpp define additional controls.

Baseline: MainWindow.axaml has New/Open/Save and Undo/Redo icons and text-only Open media; other main actions are text-only menus. Visual/vector tools and style-order controls already use fork-derived SVGs. Audio selection and Stop have icons; karaoke uses K, zoom uses +/−, and timing/playback actions are text-only menus. Formatting uses B/I/U/S text and colors use live swatches.

## main

| Fork action | Definition | Fork resource | Yoake command | Baseline / target | Status |
|---|---|---|---|---|---|
| subtitle/new | src/command/subtitle.cpp:357 | docs/art-sources/buttons/new_toolbutton.svg | subtitle/new | existing icon; new.svg | existing |
| subtitle/open | src/command/subtitle.cpp:386 | docs/art-sources/buttons/open_toolbutton.svg | subtitle/open | existing icon; open.svg | existing |
| subtitle/save | src/command/subtitle.cpp:536 | docs/art-sources/buttons/save_toolbutton.svg | subtitle/save | existing icon; save.svg | existing |
| video/jump | src/command/video.cpp:643 | docs/art-sources/buttons/jumpto_button.svg | video/jump | no icon; video-jump.svg | migrated |
| video/zoom/in | src/command/video.cpp:929 | docs/art-sources/buttons/zoom_in_button.svg | video/zoom/in | no icon; zoom-in.svg | migrated |
| video/zoom/out | src/command/video.cpp:941 | docs/art-sources/buttons/zoom_out_button.svg | video/zoom/out | no icon; zoom-out.svg | migrated |
| video/jump/start | src/command/video.cpp:670 | docs/art-sources/buttons/video_to_substart.svg | timing/jump-start | no icon; video-to-start.svg | migrated |
| video/jump/end | src/command/video.cpp:657 | docs/art-sources/buttons/video_to_subend.svg | timing/jump-end | no icon; video-to-end.svg | migrated |
| time/snap/start_video | src/command/time.cpp:354 | docs/art-sources/buttons/substart_to_video.svg | time/snap/start_video | no icon; start-to-video.svg | migrated |
| time/snap/end_video | src/command/time.cpp:181 | docs/art-sources/buttons/subend_to_video.svg | time/snap/end_video | no icon; end-to-video.svg | migrated |
| subtitle/select/visible | src/command/subtitle.cpp:578 | docs/art-sources/buttons/select_visible_button.svg | subtitle/select/visible | no icon; select-visible.svg | migrated |
| time/snap/scene | src/command/time.cpp:193 | docs/art-sources/buttons/snap_subs_to_scene.svg | time/snap/scene | no icon; snap-scene.svg | migrated |
| time/frame/current | src/command/time.cpp:127 | docs/art-sources/buttons/shift_to_frame.svg | time/frame/current | no icon; shift-to-frame.svg | migrated |
| time/align | src/command/time.cpp:239 | src/bitmaps resource button_align | — | no command; — | not applicable: no equivalent |
| tool/style/manager | src/command/tool.cpp:219 | docs/art-sources/buttons/style_toolbutton.svg | styles/manage | no icon; styles-manager.svg | migrated |
| subtitle/properties | src/command/subtitle.cpp:453 | docs/art-sources/buttons/properties_toolbutton.svg | script-info/edit | no icon; script-properties.svg | migrated |
| subtitle/attachment | src/command/subtitle.cpp:198 | docs/art-sources/buttons/attach_button.svg | subtitle/attachment | no icon; attachments.svg | migrated |
| tool/font_collector | src/command/tool.cpp:102 | docs/art-sources/buttons/font_collector_button.svg | tool/font_collector | no icon; font-collector.svg | migrated |
| am/meta | src/command/automation.cpp:84 | docs/art-sources/buttons/automation_toolbutton.svg | automation/manager | no icon; automation-manager.svg | migrated: manager equivalent; macro dispatch remains in menu |
| tool/assdraw | src/command/tool.cpp:60 | docs/art-sources/buttons/assdraw.svg | tool/assdraw | text menu; no asset | intentionally different: source is character artwork; existing text action avoids importing identity artwork |
| time/shift | src/command/time.cpp:152 | docs/art-sources/buttons/shift_times_toolbutton.svg | time/shift | no icon; shift-times.svg | migrated |
| tool/style/assistant | src/command/tool.cpp:177 | docs/art-sources/buttons/styling_toolbutton.svg | — | no command; — | not applicable: no equivalent |
| tool/translation_assistant | src/command/tool.cpp:255 | docs/art-sources/buttons/translation_toolbutton.svg | — | no command; — | not applicable: no equivalent |
| tool/resampleres | src/command/tool.cpp:162 | docs/art-sources/buttons/resample_toolbutton.svg | tool/resampleres | no icon; resample.svg | migrated |
| tool/time/postprocess | src/command/tool.cpp:243 | docs/art-sources/buttons/timing_processor_toolbutton.svg | — | no command; — | not applicable: no equivalent |
| tool/time/kanji | src/command/tool.cpp:231 | docs/art-sources/buttons/kara_timing_copier.svg | — | no command; — | not applicable: no equivalent |
| subtitle/spellcheck | src/command/subtitle.cpp:610 | docs/art-sources/buttons/spellcheck_toolbutton.svg | — | no command; — | not applicable: no equivalent |
| app/options | src/command/app.cpp:198 | docs/art-sources/buttons/options_button.svg | app/options | no icon; options.svg | migrated |
| grid/tag/cycle_hiding | src/command/grid.cpp:301 | docs/art-sources/buttons/toggle_tag_hiding.svg | grid/tag/cycle_hiding | no icon; tag-display.svg | migrated |

## visual_tools

| Fork action | Definition | Fork resource | Yoake command | Baseline / target | Status |
|---|---|---|---|---|---|
| video/tool/cross | src/command/vis_tool.cpp:147 | docs/art-sources/buttons/visual_standard.svg | video/tool/crosshair | existing icon; visual-crosshair.svg | existing |
| video/tool/drag | src/command/vis_tool.cpp:155 | docs/art-sources/buttons/visual_move.svg | video/tool/position | existing icon; visual-position.svg | existing |
| video/tool/rotate/z | src/command/vis_tool.cpp:163 | docs/art-sources/buttons/visual_rotatez.svg | video/tool/rotatez | existing icon; visual-rotate-z.svg | existing |
| video/tool/rotate/xy | src/command/vis_tool.cpp:171 | docs/art-sources/buttons/visual_rotatexy.svg | video/tool/rotatexy | existing icon; visual-rotate-xy.svg | existing |
| video/tool/curved_text | src/command/vis_tool.cpp:187 | src/bitmaps resource visual_curved_text | — | no command; — | not applicable: no curved-text authoring |
| video/tool/perspective | src/command/vis_tool.cpp:179 | docs/art-sources/buttons/visual_perspective.svg | video/tool/distort | existing icon; visual-distort.svg | existing: Mangetsu four-corner mode |
| video/tool/scale | src/command/vis_tool.cpp:195 | docs/art-sources/buttons/visual_scale.svg | video/tool/scale | existing icon; visual-scale.svg | existing |
| video/tool/clip | src/command/vis_tool.cpp:203 | docs/art-sources/buttons/visual_clip.svg | video/tool/clip | existing icon; visual-rectangle.svg | existing |
| video/tool/vector_clip | src/command/vis_tool.cpp:211 | docs/art-sources/buttons/visual_vector_clip.svg | video/tool/vectorclip | existing icon; visual-vector.svg | existing |
| help/video | src/command/help.cpp:91 | docs/art-sources/buttons/visual_help.svg | — | no command; — | not applicable: no equivalent help command |
| video/tool/motion_track | src/command/vis_tool.cpp:219 | docs/art-sources/buttons/button_motion_track.svg | video/motion-track | no icon; motion-track.svg | migrated |

## audio

| Fork action | Definition | Fork resource | Yoake command | Baseline / target | Status |
|---|---|---|---|---|---|
| time/prev | src/command/time.cpp:378 | docs/art-sources/buttons/button_prev.svg | time/prev | no icon; timing-previous.svg | migrated |
| time/next | src/command/time.cpp:366 | docs/art-sources/buttons/button_next.svg | time/next | no icon; timing-next.svg | migrated |
| audio/play/selection | src/command/audio.cpp:230 | docs/art-sources/buttons/button_playsel.svg | audio/play/selection | existing icon; audio-play-selection.svg | existing |
| audio/play/line | src/command/audio.cpp:215 | docs/art-sources/buttons/button_playline.svg | audio/play/line | no icon; audio-play-line.svg | migrated |
| audio/stop | src/command/audio.cpp:260 | docs/art-sources/buttons/button_stop.svg | audio/stop | existing icon; stop.svg | existing |
| audio/play/selection/before | src/command/audio.cpp:279 | docs/art-sources/buttons/button_playfivehbefore.svg | audio/play/selection/before | no icon; audio-play-before.svg | migrated |
| audio/play/selection/after | src/command/audio.cpp:293 | docs/art-sources/buttons/button_playfivehafter.svg | audio/play/selection/after | no icon; audio-play-after.svg | migrated |
| audio/play/selection/begin | src/command/audio.cpp:321 | docs/art-sources/buttons/button_playfirstfiveh.svg | audio/play/selection/begin | no icon; audio-play-begin.svg | migrated |
| audio/play/selection/end | src/command/audio.cpp:307 | docs/art-sources/buttons/button_playlastfiveh.svg | audio/play/selection/end | no icon; audio-play-end.svg | migrated |
| audio/play/to_end | src/command/audio.cpp:337 | docs/art-sources/buttons/button_playtoend.svg | audio/play/to_end | no icon; audio-play-to-end.svg | migrated |
| time/lead/in | src/command/time.cpp:264 | docs/art-sources/buttons/button_leadin.svg | time/lead/in | no icon; lead-in.svg | migrated |
| time/lead/out | src/command/time.cpp:276 | docs/art-sources/buttons/button_leadout.svg | time/lead/out | no icon; lead-out.svg | migrated |
| audio/commit | src/command/audio.cpp:350 | docs/art-sources/buttons/button_audio_commit.svg | audio/commit | no icon; audio-commit.svg | migrated |
| audio/go_to | src/command/audio.cpp:409 | docs/art-sources/buttons/button_audio_goto.svg | audio/go_to | no icon; audio-go-to.svg | migrated |
| audio/opt/autocommit | src/command/audio.cpp:464 | docs/art-sources/buttons/toggle_audio_autocommit.svg | audio/opt/autocommit | no icon; audio-autocommit.svg | migrated |
| audio/opt/autonext | src/command/audio.cpp:481 | docs/art-sources/buttons/toggle_audio_nextcommit.svg | audio/opt/autonext | no icon; audio-autonext.svg | migrated |
| audio/opt/autoscroll | src/command/audio.cpp:447 | docs/art-sources/buttons/toggle_audio_autoscroll.svg | audio/opt/autoscroll | no icon; audio-autoscroll.svg | migrated |
| audio/opt/spectrum | src/command/audio.cpp:498 | docs/art-sources/buttons/toggle_audio_spectrum.svg | audio/opt/spectrum | no icon; audio-spectrum.svg | migrated |
| app/toggle/global_hotkeys | src/command/app.cpp:215 | docs/art-sources/buttons/toggle_audio_medusa.svg | — | no command; — | not applicable: no global override |
| audio/karaoke | src/command/audio.cpp:532 | docs/art-sources/buttons/kara_mode.svg | — | no command; — | not applicable: legacy karaoke mode differs |
| audio/karaoke/toshiki_ktiming | src/command/audio.cpp:548 | docs/art-sources/buttons/kara_spectrogram_timing.svg | audio/karaoke | no icon; karaoke-timing.svg | migrated |
| audio/karaoke/39 | src/command/audio.cpp:568 | docs/art-sources/buttons/kara_39_mode.svg | — | no command; — | not applicable: no 39 mode |

## video

| Fork action | Definition | Fork resource | Yoake command | Baseline / target | Status |
|---|---|---|---|---|---|
| video/play | src/command/video.cpp:752 | docs/art-sources/buttons/button_play.svg | video/play | no icon; video-play.svg | migrated |
| video/play/line | src/command/video.cpp:770 | docs/art-sources/buttons/button_playline.svg | video/play/line | no icon; audio-play-line.svg | migrated |
| video/stop | src/command/video.cpp:881 | docs/art-sources/buttons/button_pause.svg | video/stop | no icon; video-pause.svg | migrated |
| video/opt/autoscroll | src/command/video.cpp:724 | docs/art-sources/buttons/toggle_video_autoscroll.svg | video/autoseek | no icon; video-autoseek.svg | migrated |

## Additional workflow audit

- The following menu and editor actions were also traced through the fork's command declarations. Their baseline in Yoake is text-only unless noted.

| Yoake action | Fork definition and icon under docs/art-sources/buttons | Baseline / planned target |
|---|---|---|
| subtitle/save-as | subtitle.cpp:553, save_as_toolbutton.svg | migrated: save-as.svg |
| video/open; video/close | video.cpp:683/250, open_video_menu.svg; close_video_menu.svg | migrated: video-open.svg; video-close.svg |
| audio/open; audio/close | audio.cpp:76/64, open_audio_menu.svg; close_audio_menu.svg | migrated: audio-open.svg; audio-close.svg; audio/open/video has no separate Yoake command |
| timecode/open; timecode/save; timecode/close | timecode.cpp:67/82/50, open_timecodes_menu.svg; save_timecodes_menu.svg; close_timecodes_menu.svg | migrated: timecodes-open.svg; timecodes-save.svg; timecodes-close.svg |
| keyframe/open; keyframe/save; keyframe/close | keyframe.cpp:65/86/48, open_keyframes_menu.svg; save_keyframes_menu.svg; close_keyframes_menu.svg | migrated: keyframes-open.svg; keyframes-save.svg; keyframes-close.svg |
| edit/find; subtitle/find/next; edit/find_replace | subtitle.cpp:211/224 and edit.cpp:2664, find_button.svg; find_next_menu.svg; find_replace_menu.svg | migrated: find.svg; find-next.svg; replace.svg |
| grid/copy; grid/cut; grid/delete; grid/paste | edit.cpp:2727/2750/2767/3237, copy_button.svg; cut_button.svg; delete_button.svg; paste_button.svg | migrated: copy.svg; cut.svg; delete.svg; paste.svg |
| text/format/bold; italic; underline; strikeout; font | edit.cpp:2403/2415/2427/2439/2587, button_bold.svg; button_italics.svg; button_underline.svg; button_strikeout.svg; button_fontname.svg | text glyphs; migrated: format-bold.svg; format-italic.svg; format-underline.svg; format-strikeout.svg; format-font.svg |
| automation/export | tool.cpp:89, export_menu.svg | migrated: export.svg |
| audio/opt/vertical_link | audio.cpp:515, toggle_audio_link.svg | text; migrated: audio-link.svg |
| app/log; tool/line/select; grid/swap | app.cpp:174, about_menu.svg; tool.cpp:114, select_lines_button.svg; grid.cpp:430, arrow_sort.svg | text; migrated: information.svg; select-lines.svg; existing style-sort.svg |
| video/detach; video/details; video/open/dummy | video.cpp:309/329/699, detach_video_menu.svg; show_video_details_menu.svg; use_dummy_video_menu.svg | text; migrated: video-detach.svg; video-details.svg; video-dummy.svg |
| subtitle/close | subtitle.cpp:374, new_toolbutton.svg | intentionally different: existing close.svg identifies closing a Yoake tab more clearly than a new-document silhouette |
- Vector clip subtools are traced to src/visual_tool_vector_clip.cpp:45-66 and src/command/vis_tool.cpp:512-565; Yoake already uses the corresponding eight vector SVGs. Position/move conversion is traced to src/visual_tool_drag.cpp:64 and uses visual-move-toggle.svg. Style ordering is traced to src/dialog_style_manager.cpp:192-196 and already uses five fork-derived SVGs.
- Frame stepping has no CMD_ICON in the fork (src/command/video.cpp:402 and :469). Yoake now uses distinct original triangle/bar SVGs, intentionally different from Aegisub, for the existing previous/next frame controls.
- The combined Open video/audio command has no single fork equivalent and remains a text menu action; separate video and audio open actions now have their own matching icons. Mute, rest insertion, and numeric field labels are descriptive text, not placeholder icons.
- Styles ordering and vector subtools retain their existing derived assets. ASSDraw stays a text menu action: the source resource is a character illustration rather than an ordinary functional command symbol.
- The current color buttons are live ASS color/alpha swatches, not placeholder squares. Removing the swatches would hide the value being edited.
- Light/dark static SVG samples were inspected at 16 px; actual application hover, pressed, disabled, laptop layout and DPI verification remains pending.

## Counts and verification state

- The fork main toolbar contains 29 actions: 23 have Yoake equivalents, of which 22 have matching icons and one (ASSDraw) is intentionally text-only for the artwork reason above. Six actions have no implemented Yoake equivalent.
- The four audited default toolbars contain 66 actions (29 main, 11 visual, 22 audio, 4 video). Their existing Yoake mappings and exceptions are recorded row by row above.
- Across those four toolbars, 55 actions have Yoake equivalents: 41 newly wired
  in this pass, 13 already used matching icons, and ASSDraw remains
  intentionally text-only. The other 11 have no equivalent. No mapped action
  in this four-toolbar inventory remains unaccounted for.
- The working tree contains 77 Aegisub-derived added or replaced SVGs, including
  the earlier basic-action pass; two additional frame-step SVGs are original
  Yoake artwork. Ten symbol-only controls were replaced: four formatting
  buttons, two audio zoom buttons, two video frame-step buttons, the karaoke
  toggle, and the style-manager ellipsis.
- Additional matching menu and formatting icons are wired in the table above. Source-only checks cover icon generation, tooltips and distinct timing/playback command mappings; GitHub Actions and real application-state visual QA are required before declaring the migration complete.
