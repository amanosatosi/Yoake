namespace Yoake.App;

internal sealed class StartupOptions
{
    public string? VerificationReport { get; private set; }
    public string? VerificationMedia {get;private set;}
    public string? ProfileDirectory { get; private set; }
    public string? DiagnosticsDirectory { get; private set; }
    public bool NonInteractive { get; private set; }
    private int _verificationFinished;
    public bool VerificationFinished => Volatile.Read(ref _verificationFinished) != 0;

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions { NonInteractive = args.Contains("--verify-editor") || args.Contains("--verify-ui-startup") };
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--verify-ui-startup" or "--profile-directory" or "--diagnostics-directory" or "--verification-media")) continue;
            var flag = args[i];
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException($"{flag} requires a path.");
            var path = Path.GetFullPath(args[i]);
            switch (flag)
            {
                case "--verification-media":options.VerificationMedia=path;break;
                case "--verify-ui-startup": options.VerificationReport = path; break;
                case "--profile-directory": options.ProfileDirectory = path; break;
                case "--diagnostics-directory": options.DiagnosticsDirectory = path; break;
            }
        }
        return options;
    }

    public void FinishVerification(string report)
    {
        if (VerificationReport is null || Interlocked.Exchange(ref _verificationFinished, 1) != 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(VerificationReport)!);
        File.WriteAllText(VerificationReport, report);
    }
}
