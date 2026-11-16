Self-authored synthetic, redistributable fixture (no external media).
Generated using FFmpeg 7.1.5 with a 16×16 black three-frame FFV1 video,
source.ass and source.srt as uncompressed ASS and UTF-8 subtitle tracks:

    ffmpeg -f lavfi -i color=c=black:s=16x16:r=1:d=3 -i source.ass -i source.srt -map 0:v -map 1:s -map 2:s -c:v ffv1 -c:s:0 ass -c:s:1 srt -metadata:s:s:0 language=jpn -metadata:s:s:0 title='Japanese ASS' -metadata:s:s:1 language=mya -metadata:s:s:1 title='Burmese translation' episode.mkv

ASS packets are muxed in time order, while ReadOrder retains script order.
The header, comma-containing text, opaque override, actors, margins, mixed
Japanese/Burmese and combining mark are intentional import checks.
