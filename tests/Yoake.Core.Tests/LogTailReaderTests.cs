using Yoake.Core.Logging;

namespace Yoake.Core.Tests;

public sealed class LogTailReaderTests
{
    [Fact] public async Task BoundedReaderPreservesUnicodeCompleteLinesAndNeverChangesTheFile()
    {
        var path=Path.Combine(Path.GetTempPath(),"Yoake-log-"+Guid.NewGuid()+".txt");
        var source=string.Concat(Enumerable.Repeat("日本語 မြန်မာ é\n",50))+"last entry 👩‍👩‍👧‍👦\n";
        File.WriteAllText(path,source);
        try
        {
            var complete=await LogTailReader.ReadAsync(path);Assert.True(complete.Exists);Assert.False(complete.Truncated);Assert.Equal(source,complete.Text);
            var tail=await LogTailReader.ReadAsync(path,129);Assert.True(tail.Truncated);Assert.EndsWith("last entry 👩‍👩‍👧‍👦\n",tail.Text);Assert.DoesNotContain("�",tail.Text);
            Assert.Equal(source,File.ReadAllText(path));
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>LogTailReader.ReadAsync(path,cancellationToken:cancellation.Token));
        }
        finally{File.Delete(path);}
        Assert.False((await LogTailReader.ReadAsync(path)).Exists);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>LogTailReader.ReadAsync(path,0));
    }
}
