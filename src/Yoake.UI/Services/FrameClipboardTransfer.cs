using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace Yoake.UI.Services;

/// <summary>Owns the image until Avalonia/Windows releases the clipboard transfer.</summary>
internal sealed class FrameClipboardTransfer : IAsyncDataTransfer, IDataTransfer
{
    private readonly DataTransfer _data = new();
    private Bitmap? _bitmap;
    private volatile bool _published;
    public FrameClipboardTransfer(Bitmap bitmap, Func<bool> isCurrent, CancellationToken token)
    {
        _bitmap = bitmap;
        // Windows can retry OleSetClipboard asynchronously while locked. Guard
        // delayed image rendering until Flush completes, so those retries cannot
        // turn an obsolete extraction into a persistent image payload.
        _data.Add(DataTransferItem.Create(DataFormat.Bitmap, () =>
            _published || !token.IsCancellationRequested && isCurrent() ? _bitmap : null));
    }
    public IReadOnlyList<DataFormat> Formats => _data.Formats;
    public IReadOnlyList<IAsyncDataTransferItem> Items => _data.Items;
    IReadOnlyList<IDataTransferItem> IDataTransfer.Items => _data.Items;
    public void Accept() => _published = true;
    public void Dispose() => Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}
