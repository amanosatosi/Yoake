using Yoake.Core.Automation;

namespace Yoake.Native.Automation;

public sealed class LuaAutomationProvider(string hostAdapterPath) : IAutomationRuntimeProvider
{
    public async ValueTask<IAutomationScript> LoadAsync(string path, AutomationPathResolver paths, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var script = new LuaAutomationScript(Path.GetFullPath(path), paths);
            try
            {
                script.Initialize(File.ReadAllText(hostAdapterPath), cancellationToken);
                return (IAutomationScript)script;
            }
            catch { script.Dispose(); throw; }
        }, cancellationToken).ConfigureAwait(false);
    }
}
