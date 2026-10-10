using Yoake.Core.Media;
namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<double> FrameTimes=>_media?.FrameTimes??Array.Empty<double>();
    public IReadOnlyList<int> Keyframes=>_media?.Keyframes??Array.Empty<int>();
    public int CurrentFrame=>FrameNavigation.AtTime(FrameTimes,CurrentTimeSeconds);
    public string FramePositionDisplay=>FrameTimes.Count==0?TimeDisplay:$"#{CurrentFrame} · {TimeSpan.FromSeconds(FrameTimes[CurrentFrame]):hh\\:mm\\:ss\\.fff} / {DurationDisplay} "+(Keyframes.Contains(CurrentFrame)?"· keyframe":"");
    public string RelativeTimingDisplay=>SelectedEvent is {} line?FrameNavigation.Relative(FrameTimes.Count>0?FrameTimes[CurrentFrame]:CurrentTimeSeconds,line.StartMilliseconds??0,line.EndMilliseconds??0):"";
    private Task SeekFrameAsync(int frame)=>FrameTimes.Count==0?Task.CompletedTask:SeekPlaybackAsync(FrameTimes[Math.Clamp(frame,0,FrameTimes.Count-1)]);
}
