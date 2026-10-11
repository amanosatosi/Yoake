using System.Text;

namespace Yoake.Core.Logging;

public sealed record LogTail(string Text,bool Exists,bool Truncated);

public static class LogTailReader
{
    public static async Task<LogTail> ReadAsync(string path,int maximumBytes=1024*1024,CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if(maximumBytes is <4 or >8*1024*1024)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if(!File.Exists(path))return new("",false,false);
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,FileOptions.Asynchronous);
        var length=stream.Length;var truncated=length>maximumBytes;
        if(truncated)stream.Seek(length-maximumBytes,SeekOrigin.Begin);
        var bytes=new byte[(int)Math.Min(length,maximumBytes)];var count=0;
        while(count<bytes.Length)
        {
            var read=await stream.ReadAsync(bytes.AsMemory(count),cancellationToken);if(read==0)break;count+=read;
        }
        var text=Encoding.UTF8.GetString(bytes,0,count);
        // A bounded seek can start in a UTF-8 character or partial line. Only
        // complete tail lines are displayed; the file itself is never changed.
        var newline=text.IndexOf('\n');
        if(truncated&&newline>=0)text=text[(newline+1)..];
        else if(text.StartsWith('\uFEFF'))text=text[1..];
        return new(text,true,truncated);
    }
}
