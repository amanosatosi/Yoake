namespace Yoake.App;

internal sealed class StartupOptions
{
    public string? VerificationReport { get; private set; }
    public string? VerificationMedia {get;private set;}
    public string? ProfileDirectory { get; private set; }
    public string? DiagnosticsDirectory { get; private set; }
    public bool NonInteractive { get; private set; }
    public bool HotkeyAcceptance { get; private set; }
    public bool TimingAcceptance {get;private set;}
    public bool TimecodeAcceptance {get;private set;}
    public bool AudioDisplayAcceptance {get;private set;}
    public bool SelectionAcceptance {get;private set;}
    public bool AudioClipAcceptance {get;private set;}
    public bool AudioPlaybackAcceptance {get;private set;}
    public bool MediaOwnershipAcceptance {get;private set;}
    public bool VideoNavigationAcceptance {get;private set;}
    public bool VideoAcceptance {get;private set;}
    public bool VideoUtilitiesAcceptance {get;private set;}
    public bool VideoPresentationAcceptance {get;private set;}
    public bool OriginalAndPasteAcceptance {get;private set;}
    public bool RecoveryCapture {get;private set;}
    public bool RecoveryAcceptance {get;private set;}
    public bool AttachmentCapture {get;private set;}
    public bool AttachmentRecovery {get;private set;}
    public bool AudioTimingAcceptance {get;private set;}
    private int _verificationFinished;
    private int _startupVerified;
    public bool VerificationFinished => Volatile.Read(ref _verificationFinished) != 0;
    public bool StartupVerified => Volatile.Read(ref _startupVerified) != 0;

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions { NonInteractive = args.Contains("--verify-editor") || args.Contains("--verify-ui-startup") || args.Contains("--verify-ui-hotkeys") || args.Contains("--verify-ui-timing") || args.Contains("--verify-ui-timecodes") || args.Contains("--verify-ui-audio-display") || args.Contains("--verify-ui-selection") || args.Contains("--verify-ui-audio-clip") || args.Contains("--verify-ui-audio-playback") || args.Contains("--verify-ui-audio-timing") || args.Contains("--verify-ui-video") || args.Contains("--verify-ui-video-navigation") || args.Contains("--verify-ui-media-ownership") || args.Contains("--verify-ui-video-utilities") || args.Contains("--verify-ui-video-presentation") || args.Contains("--verify-ui-original-paste") || args.Contains("--verify-ui-recovery") || args.Contains("--verify-ui-recovery-capture") || args.Contains("--verify-ui-attachments-capture") || args.Contains("--verify-ui-attachments-recovery") };
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--verify-ui-startup" or "--verify-ui-hotkeys" or "--verify-ui-timing" or "--verify-ui-timecodes" or "--verify-ui-audio-display" or "--verify-ui-selection" or "--verify-ui-audio-clip" or "--verify-ui-audio-playback" or "--verify-ui-audio-timing" or "--verify-ui-video" or "--verify-ui-video-navigation" or "--verify-ui-media-ownership" or "--verify-ui-video-utilities" or "--verify-ui-video-presentation" or "--verify-ui-original-paste" or "--verify-ui-recovery" or "--verify-ui-recovery-capture" or "--verify-ui-attachments-capture" or "--verify-ui-attachments-recovery" or "--profile-directory" or "--diagnostics-directory" or "--verification-media")) continue;
            var flag = args[i];
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException($"{flag} requires a path.");
            var path = Path.GetFullPath(args[i]);
            switch (flag)
            {
                case "--verify-ui-attachments-capture":options.VerificationReport=path;options.AttachmentCapture=true;break;
                case "--verify-ui-attachments-recovery":options.VerificationReport=path;options.AttachmentRecovery=true;break;
                case "--verify-ui-recovery-capture":options.VerificationReport=path;options.RecoveryCapture=true;break;
                case "--verify-ui-recovery":options.VerificationReport=path;options.RecoveryAcceptance=true;break;
                case "--verification-media":options.VerificationMedia=path;break;
                case "--verify-ui-startup": options.VerificationReport = path; break;
                case "--verify-ui-hotkeys": options.VerificationReport=path; options.HotkeyAcceptance=true; break;
                case "--verify-ui-timing": options.VerificationReport=path;options.TimingAcceptance=true;break;
                case "--verify-ui-timecodes":options.VerificationReport=path;options.TimecodeAcceptance=true;break;
                case "--verify-ui-audio-display":options.VerificationReport=path;options.AudioDisplayAcceptance=true;break;
                case "--verify-ui-selection":options.VerificationReport=path;options.SelectionAcceptance=true;break;
                case "--verify-ui-audio-clip":options.VerificationReport=path;options.AudioClipAcceptance=true;break;
                case "--verify-ui-audio-playback":options.VerificationReport=path;options.AudioPlaybackAcceptance=true;break;
                case "--verify-ui-video-presentation":options.VerificationReport=path;options.VideoPresentationAcceptance=true;break;
                case "--verify-ui-original-paste":options.VerificationReport=path;options.OriginalAndPasteAcceptance=true;break;
                case "--verify-ui-video-utilities":options.VerificationReport=path;options.VideoUtilitiesAcceptance=true;break;
                case "--verify-ui-media-ownership":options.VerificationReport=path;options.MediaOwnershipAcceptance=true;break;
                case "--verify-ui-video-navigation":options.VerificationReport=path;options.VideoNavigationAcceptance=true;break;
                case "--verify-ui-video":options.VerificationReport=path;options.VideoAcceptance=true;break;
                case "--verify-ui-audio-timing":options.VerificationReport=path;options.AudioTimingAcceptance=true;break;
                case "--profile-directory": options.ProfileDirectory = path; break;
                case "--diagnostics-directory": options.DiagnosticsDirectory = path; break;
            }
        }
        return options;
    }

    public bool MarkStartupVerified() => Interlocked.Exchange(ref _startupVerified, 1) == 0;

    public void FinishVerification(string report)
    {
        if (VerificationReport is null || Interlocked.Exchange(ref _verificationFinished, 1) != 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(VerificationReport)!);
        File.WriteAllText(VerificationReport, report);
    }
}
