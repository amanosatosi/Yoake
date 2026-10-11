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
    private int _verificationFinished;
    private int _startupVerified;
    public bool VerificationFinished => Volatile.Read(ref _verificationFinished) != 0;
    public bool StartupVerified => Volatile.Read(ref _startupVerified) != 0;

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions { NonInteractive = args.Contains("--verify-editor") || args.Contains("--verify-ui-startup") || args.Contains("--verify-ui-hotkeys") || args.Contains("--verify-ui-timing") || args.Contains("--verify-ui-timecodes") };
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--verify-ui-startup" or "--verify-ui-hotkeys" or "--verify-ui-timing" or "--verify-ui-timecodes" or "--profile-directory" or "--diagnostics-directory" or "--verification-media")) continue;
            var flag = args[i];
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException($"{flag} requires a path.");
            var path = Path.GetFullPath(args[i]);
            switch (flag)
            {
                case "--verification-media":options.VerificationMedia=path;break;
                case "--verify-ui-startup": options.VerificationReport = path; break;
                case "--verify-ui-hotkeys": options.VerificationReport=path; options.HotkeyAcceptance=true; break;
                case "--verify-ui-timing": options.VerificationReport=path;options.TimingAcceptance=true;break;
                case "--verify-ui-timecodes":options.VerificationReport=path;options.TimecodeAcceptance=true;break;
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
