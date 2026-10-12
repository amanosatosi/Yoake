using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace Yoake.UI.Services;

/// <summary>Owns the image until Avalonia/Windows releases the clipboard transfer.</summary>
internal sealed class FrameClipboardTransfer : IAsyncDataTransfer
{
    private readonly DataTransfer _data = new();
    private Bitmap? _bitmap;
    public FrameClipboardTransfer(Bitmap bitmap)
    { _bitmap = bitmap; _data.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap)); }
    public IReadOnlyList<DataFormat> Formats => _data.Formats;
    public IReadOnlyList<IAsyncDataTransferItem> Items => _data.Items;
    public void Dispose() => Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}
