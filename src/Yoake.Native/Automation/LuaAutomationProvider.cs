using Yoake.Core.Automation;

namespace Yoake.Native.Automation;

public sealed class LuaAutomationProvider(string hostAdapterPath) : IAutomationRuntimeProvider
{
    public ValueTask<IAutomationScript> LoadAsync(string path, AutomationPathResolver paths, CancellationToken cancellationToken)
        => LoadAsync(path, paths, null, cancellationToken);
    public async ValueTask<IAutomationScript> LoadAsync(string path, AutomationPathResolver paths, IAutomationHostServices? platformServices, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var script = new LuaAutomationScript(Path.GetFullPath(path), paths, platformServices);
            try
            {
                script.Initialize(File.ReadAllText(hostAdapterPath), cancellationToken);
                return (IAutomationScript)script;
            }
            catch { script.Dispose(); throw; }
        }, cancellationToken).ConfigureAwait(false);
    }
}
