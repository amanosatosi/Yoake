using Yoake.Core.Logging;

namespace Yoake.Core.Tests;

public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void FatalReportWorksBeforeNormalLoggerAndIncludesInnerExceptionAndEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), "Yoake-crash-test-" + Guid.NewGuid());
        try
        {
            StartupDiagnostics.Initialize("test+commit123", root);
            StartupDiagnostics.Checkpoint("before settings");
            Exception failure;
            try { throw new InvalidOperationException("outer", new ArgumentException("inner")); }
            catch (Exception exception) { failure = exception; }
            var path = StartupDiagnostics.ReportFatal("test startup", failure);
            var report = File.ReadAllText(path);
            Assert.Contains("test+commit123", report);
            Assert.Contains("Process architecture:", report);
            Assert.Contains("OS architecture:", report);
            Assert.Contains("Dynamic code supported:", report);
            Assert.Contains("System.InvalidOperationException: outer", report);
            Assert.Contains("System.ArgumentException: inner", report);
            Assert.Contains(nameof(FatalReportWorksBeforeNormalLoggerAndIncludesInnerExceptionAndEnvironment), report);
            Assert.Contains("before settings", File.ReadAllText(Path.Combine(root, "startup.log")));
        }
        finally { Directory.Delete(root, true); }
    }
}
