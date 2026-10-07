using Avalonia.Logging;
using Yoake.Core.Logging;

namespace Yoake.App;

internal sealed class StartupLogSink(ILogSink? previous) : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning || previous?.IsEnabled(level, area) == true;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        try
        {
            if (level >= LogEventLevel.Warning)
                StartupDiagnostics.FrameworkMessage($"{level} [{area}] {messageTemplate} | {string.Join(" | ", propertyValues.Select(v => v?.ToString()))}", level >= LogEventLevel.Error);
        }
        catch (Exception) { /* A diagnostic sink must not create a startup crash. */ }
        if (previous?.IsEnabled(level, area) == true) previous.Log(level, area, source, messageTemplate, propertyValues);
    }
}
