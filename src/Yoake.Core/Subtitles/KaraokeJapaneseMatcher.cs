// Adapted from Aegisub v3.2.2 libaegisub/common/karaoke_matcher.cpp.
// Copyright (c) 2013, Thomas Goyne <plorkyeran@aegisub.org>
// Permission to use, copy, modify, and distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
// ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
// OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
using System.Globalization;

namespace Yoake.Core.Subtitles;

public readonly record struct KaraokeMatchSuggestion(int SourceCount,int DestinationCount);

// A suggestion never accepts a group or changes subtitle text. Destination
// units come from the alignment's grapheme/ASS-escape-safe source map.
public static class KaraokeJapaneseMatcher
{
    public static KaraokeMatchSuggestion Suggest(IReadOnlyList<string> source,IReadOnlyList<string> destination)
    {
        if(source.Count==0)return default;
        var sourceCount=1;var position=0;var input=source[0].ToLowerInvariant();
        bool Whitespace()
        {
            input=input.TrimStart();
            while(position<destination.Count&&string.IsNullOrWhiteSpace(destination[position]))position++;
            if(position==destination.Count){sourceCount=source.Count;return true;}
            return input.Length==0;
        }
        KaraokeMatchSuggestion Result()=>new(sourceCount,position);
        if(Whitespace())return Result();
        while(input.Length>0)
        {
            var first=StringInfo.GetNextTextElement(input);
            if(CultureInfo.InvariantCulture.CompareInfo.Compare(first,destination[position],CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace|CompareOptions.IgnoreWidth|CompareOptions.IgnoreKanaType)==0)
            {
                position++;input=input[first.Length..];if(Whitespace())return Result();continue;
            }
            var matched=false;
            foreach(var pair in JapaneseKanaTable.ForRomaji(input))
            {
                var kana="";var count=0;
                while(position+count<destination.Count&&kana.Length<pair.Kana.Length)kana+=destination[position+count++];
                if(kana!=pair.Kana)continue;
                input=input[pair.Romaji.Length..];position+=count;
                if(Whitespace())return Result();matched=true;break;
            }
            if(!matched)break;
        }
        if(destination.Count-position==1)return new(source.Count,position+1);
        for(var lookahead=1;lookahead<=3&&position+lookahead<destination.Count;lookahead++)
        {
            var at=position+lookahead;
            var readings=JapaneseKanaTable.ForKana(destination[at]);
            if(at+1<destination.Count)readings=[..JapaneseKanaTable.ForKana(destination[at]+destination[at+1]),..readings];
            var sourceLookahead=0;
            foreach(var syllable in source)
            {
                if(string.IsNullOrWhiteSpace(syllable))continue;
                if(++sourceLookahead==1)continue;
                if(sourceLookahead>lookahead*5)break;
                var lower=syllable.ToLowerInvariant();
                if(!syllable.StartsWith(destination[at],StringComparison.Ordinal)&&!readings.Any(r=>lower.StartsWith(r,StringComparison.Ordinal)))continue;
                if(sourceLookahead==2)return new(sourceCount,position+lookahead);
                return new(Math.Clamp((int)((sourceLookahead-1d)/lookahead+.5),1,source.Count),position+1);
            }
        }
        return new(sourceCount,Math.Max(position,1));
    }
}
