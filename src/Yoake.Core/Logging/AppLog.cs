namespace Yoake.Core.Logging;

public interface IAppLog
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class FileAppLog(string path) : IAppLog
{
    private readonly object _gate = new();

    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
        if (exception is not null) line += Environment.NewLine + exception;
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
