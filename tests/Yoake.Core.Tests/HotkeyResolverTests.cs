using Yoake.Core.Hotkeys;

namespace Yoake.Core.Tests;

public sealed class HotkeyResolverTests
{
    [Fact]
    public void MostSpecificActiveContextWins()
    {
        var resolver = new HotkeyResolver();
        resolver.Add(new("video/play", HotkeyContext.Default, new("Space")));
        resolver.Add(new("audio/play/selection", HotkeyContext.Audio, new("Space")));
        var match = resolver.Resolve(new("space"), [HotkeyContext.Default, HotkeyContext.Audio]);
        Assert.Equal("audio/play/selection", match?.CommandId);
    }

    [Fact]
    public void SameGestureInSameContextIsRejected()
    {
        var resolver = new HotkeyResolver();
        resolver.Add(new("one", HotkeyContext.Video, new("J", KeyModifiers.Control)));
        Assert.Throws<InvalidOperationException>(() => resolver.Add(new("two", HotkeyContext.Video, new("j", KeyModifiers.Control))));
    }
}
