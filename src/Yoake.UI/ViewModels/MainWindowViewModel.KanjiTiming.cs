using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record KanjiTimingGroup(KanjiTimingSession Session,int SourceCount,int DestinationCount);
public sealed record KanjiTimingStart(KanjiTimingSession Session,string SourceStyle,string DestinationStyle);
public sealed partial class MainWindowViewModel
{
    public KanjiTimingSession BeginKanjiTiming()
    {
        if(!CommitDraft())throw new InvalidOperationException(SubtitleStatus);return new(this);
    }
    internal void RefreshKanjiDraft()=>ReloadDraft();
    private void RegisterKanjiTimingCommands()
    {
        _registry.Register(new(new("tool/time/kanji","Kanji Timing Copier…","Match source karaoke syllables to destination characters","Tools"),async (_,token)=>
        { if(Dialogs is {} dialogs&&_activeId is {} owner&&CommitDraft())await dialogs.ShowKanjiTimingAsync(this,owner,token); },_=>ActiveEditor is not null&&!HasGesture));
        Actions["tool/time/kanji"]=new RegistryCommand(_registry,"tool/time/kanji",CurrentContext,hotkeys:()=>HotkeyProfile);
        foreach(var operation in new[] { "start","suggest","link","unlink","accept","skip-source","skip-destination","previous","apply" })
        {
            var action=operation;
            _registry.Register(new(new("tool/time/kanji/"+action,action+" karaoke match",action,"Tools"),(invocation,token)=>
            {
                token.ThrowIfCancellationRequested();
                var session=invocation.Parameter switch { KanjiTimingStart start=>start.Session,KanjiTimingGroup group=>group.Session,KanjiTimingSession value=>value,_=>throw new ArgumentException("Use this action in its matching session.") };
                if(!ReferenceEquals(session.Model,this)||session.DocumentId!=_activeId)throw new ArgumentException("The matching session belongs to another document.");
                switch(action)
                {
                    case "start":var start=invocation.Parameter as KanjiTimingStart??throw new ArgumentException("Choose source and destination styles.");session.Restart(start.SourceStyle,start.DestinationStyle);break;
                    case "suggest":session.Suggest();break;
                    case "link":var group=invocation.Parameter as KanjiTimingGroup??throw new ArgumentException("Choose source and destination counts.");session.Link(group.SourceCount,group.DestinationCount);break;
                    case "unlink":session.Unlink();break;
                    case "accept":session.Accept();break;
                    case "skip-source":session.Skip(true);break;
                    case "skip-destination":session.Skip(false);break;
                    case "previous":session.Previous();break;
                    case "apply":session.Apply();break;
                }
                return ValueTask.CompletedTask;
            },context=>ActiveEditor is not null&&!HasGesture&&(context.DocumentId is null||context.DocumentId==_activeId)));
        }
    }
}
