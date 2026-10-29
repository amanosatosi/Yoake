// Capture behavior adapted from Aegisub Toshi-ban timing39_match.cpp and
// timing39_session.cpp. Copyright (c) 2026 JibunSenyou contributors.
// Permission to use, copy, modify, and/or distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
// OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
// CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

namespace Yoake.Core.Subtitles;

public enum Timing39State { Countdown, Ready, Capturing, Results, Discarded }
public enum Timing39Key { F, J, D, K }
public readonly record struct Timing39Block(long Start, long End, bool Gap);
public sealed record Timing39Partition(IReadOnlyList<Timing39Block> Blocks,IReadOnlyList<int> RawIndices,bool NeedsReview,bool Invalid);

public static class Timing39Partitioner
{
    // Partition copies only; the original performance is never rewritten. A
    // sung block belongs only when strictly more than half is in the line.
    public static Timing39Partition Partition(IReadOnlyList<Timing39Block> raw,long start,long end)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if(start<0||end<=start)throw new ArgumentOutOfRangeException(nameof(end));
        var blocks=new List<Timing39Block>();var indices=new List<int>();var review=false;var invalid=false;var sung=false;
        for(var i=0;i<raw.Count;i++)
        {
            var block=raw[i];
            if(block.End<=block.Start){if(!block.Gap&&block.Start>=start&&block.Start<end)invalid=true;continue;}
            if(block.End<=start||block.Start>=end)continue;
            var from=Math.Max(start,block.Start);var to=Math.Min(end,block.End);
            if(!block.Gap && (to-from)<=((block.End-block.Start)/2))continue;
            if(from!=block.Start||to!=block.End)review|=!block.Gap;
            blocks.Add(new(from,to,block.Gap));indices.Add(i);sung|=!block.Gap;
        }
        if(!sung){blocks.Clear();indices.Clear();}
        return new(blocks.ToArray(),indices.ToArray(),review,invalid);
    }
}

/// <summary>Session-local input only. Times are absolute milliseconds from the caller's media timeline.</summary>
public sealed class Timing39Capture
{
    private sealed class Lane
    {
        private readonly bool[] _held = new bool[2];
        private readonly List<Timing39Block> _blocks = [];
        private int _owner = -1;
        private long _gapStart;
        public IReadOnlyList<Timing39Block> Snapshot() => _blocks.ToArray();
        public void Begin(long time) { _blocks.Clear(); Array.Clear(_held); _owner=-1; _gapStart=time; }
        public bool Down(int key, long time)
        {
            if(_held[key])return false;
            if(_blocks.Count>0 && time<_blocks[^1].End)return false;
            if(_owner>=0) _blocks[^1]=_blocks[^1] with { End=time };
            else if(time>_gapStart) _blocks.Add(new(_gapStart,time,true));
            _held[key]=true;_owner=key;_blocks.Add(new(time,time,false));return true;
        }
        public bool Up(int key, long time)
        {
            var wasHeld=_held[key];_held[key]=false;
            if(_owner!=key)return wasHeld;
            var block=_blocks[^1];_blocks[^1]=block with { End=Math.Max(block.Start,time) };
            _gapStart=_blocks[^1].End;_owner=-1;return true;
        }
        public void Finish(long time)
        {
            if(_owner>=0){var block=_blocks[^1];_blocks[^1]=block with { End=Math.Max(block.Start,time) };}
            else if(_blocks.Count>0&&time>_gapStart)_blocks.Add(new(_gapStart,time,true));
            Array.Clear(_held);_owner=-1;
        }
    }
    private readonly Lane[] _lanes=[new(),new()];
    private long _lastTime;
    public long Start { get; }
    public long End { get; }
    public int Countdown { get; private set; }=3;
    public Timing39State State { get; private set; }=Timing39State.Countdown;
    public Timing39Capture(long start,long end)
    {
        if(start<0||end<=start)throw new ArgumentOutOfRangeException(nameof(end),"39 mode requires a positive media interval.");
        Start=start;End=end;_lastTime=start;
    }
    public bool TickCountdown()
    {
        if(State!=Timing39State.Countdown)return false;
        if(--Countdown>0)return false;
        State=Timing39State.Ready;return true;
    }
    public bool Begin(long mediaTime)
    {
        if(State!=Timing39State.Ready||mediaTime<Start||mediaTime>=End)return false;
        _lastTime=mediaTime;foreach(var lane in _lanes)lane.Begin(mediaTime);
        State=Timing39State.Capturing;return true;
    }
    public bool Key(Timing39Key key,bool down,long mediaTime)
    {
        if(State!=Timing39State.Capturing||mediaTime<_lastTime||mediaTime>=End)return false;
        var (lane,index)=key switch { Timing39Key.F=>(0,0),Timing39Key.J=>(0,1),Timing39Key.D=>(1,0),Timing39Key.K=>(1,1),_ => (-1,-1) };
        if(lane<0)return false;
        _lastTime=mediaTime;return down?_lanes[lane].Down(index,mediaTime):_lanes[lane].Up(index,mediaTime);
    }
    public bool Stop(long mediaTime)
    {
        if(State!=Timing39State.Capturing)return false;
        var end=Math.Clamp(mediaTime,_lastTime,End);foreach(var lane in _lanes)lane.Finish(end);
        _lastTime=end;State=Timing39State.Results;return true;
    }
    public void Discard() { State=Timing39State.Discarded;foreach(var lane in _lanes)lane.Begin(Start); }
    public IReadOnlyList<Timing39Block> Blocks(int lane) => lane is 0 or 1?_lanes[lane].Snapshot():throw new ArgumentOutOfRangeException(nameof(lane));
}
