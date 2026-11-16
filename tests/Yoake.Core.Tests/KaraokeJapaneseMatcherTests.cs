// Copyright (c) 2013, Thomas Goyne <plorkyeran@aegisub.org>
//
// Permission to use, copy, modify, and distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
//
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
// ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
// OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
// Test vectors ported from Aegisub v3.2.2.
using System.Globalization;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class KaraokeJapaneseMatcherTests
{
    public static IEnumerable<object[]> ReferenceCases()
    {
        yield return [new string[] {},"",0,0];
        yield return [new string[] {},"a",0,0];
        yield return [new string[] {"a","b"},"",2,0];
        yield return [new string[] {"","b"},"cc",1,0];
        yield return [new string[] {"a","b"},"cc",1,1];
        yield return [new string[] {"abc","de"},"abcde",1,3];
        yield return [new string[] {"abc","de"},"ABCDE",1,3];
        yield return [new string[] {"ABC","DE"},"abcde",1,3];
        yield return [new string[] {" abc","de"},"abcde",1,3];
        yield return [new string[] {"abc ","de"},"abcde",1,3];
        yield return [new string[] {"abc ","de"}," abcde",1,4];
        yield return [new string[] {"abc ","de"},"abc de",1,4];
        yield return [new string[] {"abc ","de"},"ab c de",1,5];
        yield return [new string[] {"∫∫","de"},"∫∫a",1,2];
        yield return [new string[] {"∫∫","de"},"∫∫a",1,2];
        yield return [new string[] {"rode"},"ろ",1,1];
        yield return [new string[] {"rode"},"ロ",1,1];
        yield return [new string[] {"romajide"},"ろまじ",1,3];
        yield return [new string[] {"kya","e"},"きゃe",1,2];
        yield return [new string[] {"kya"},"きゃ",1,2];
        yield return [new string[] {"ro ma jide"},"ろまじ",1,3];
        yield return [new string[] {"r om aj ide"},"ろまじ",1,1];
        yield return [new string[] {"a","b","c"},"ろ",3,1];
        yield return [new string[] {"ab","cd"},"ａｂc",1,2];
        yield return [new string[] {"ab","ro"},"eeろ",1,2];
        yield return [new string[] {"ab","","ro"},"eeろ",1,2];
        yield return [new string[] {"ab","","","ro"},"eeろ",1,2];
        yield return [new string[] {"abc","","ro"},"eeeろ",1,3];
        yield return [new string[] {"abcd","","ro"},"eeeeろ",1,1];
        yield return [new string[] {"a","b","ro"},"eeろ",1,1];
        yield return [new string[] {"a","b","c","ro"},"eeろ",2,1];
        yield return [new string[] {"a","b","c","d","ro"},"eeろ",2,1];
        yield return [new string[] {"a","b","c","d","f","ro"},"eeろ",3,1];
        yield return [new string[] {"a","b","c","d","f","ro"}," eeろ",3,2];
        yield return [new string[] {"aa","kya"},"eeきゃ",1,2];
        yield return [new string[] {"Bo","ku","wa"},"僕は",2,1];
        yield return [new string[] {"Bo","ku","e"},"僕へ",2,1];
        yield return [new string[] {"shi","tta",""},"知った",1,1];
        yield return [new string[] {"tta",""},"った",2,2];
        yield return [new string[] {"Oh... ","Nan","ka ","ta","ri","nai"},"Oh…なんか足りない",1,3];
    }
    private static string[] Elements(string text)
    {
        var cuts=StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        return Enumerable.Range(0,cuts.Length-1).Select(i=>text[cuts[i]..cuts[i+1]]).ToArray();
    }
    [Theory]
    [MemberData(nameof(ReferenceCases))]
    public void Aegisub322ReferenceSuggestions(string[] source,string destination,int expectedSource,int expectedDestination)
        =>Assert.Equal(new KaraokeMatchSuggestion(expectedSource,expectedDestination),KaraokeJapaneseMatcher.Suggest(source,Elements(destination)));
    [Fact]
    public void CombiningMarksBurmeseAndEmojiAreWholeDestinationUnits()
    {
        Assert.Equal(new KaraokeMatchSuggestion(1,1),KaraokeJapaneseMatcher.Suggest(["e"],Elements("éx")));
        var elements=Elements("👩‍👩‍👧‍👦မြန်မာ");Assert.Equal("👩‍👩‍👧‍👦",elements[0]);
        Assert.Equal(new KaraokeMatchSuggestion(1,1),KaraokeJapaneseMatcher.Suggest(["👩‍👩‍👧‍👦"],elements));
    }
}
