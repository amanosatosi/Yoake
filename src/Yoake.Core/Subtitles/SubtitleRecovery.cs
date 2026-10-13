using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yoake.Core.Subtitles;

public sealed record RecoveryDraft
{
    public int Row { get; init; }
    public Dictionary<string, string> Values { get; init; } = [];
    public string OriginalText { get; init; } = "";
    public int Caret { get; init; }
    public int SelectionStart { get; init; }
    public int SelectionEnd { get; init; }
}
public sealed record RecoveryMetadata
{
    public int Schema { get; init; } = 1;
    public Guid DocumentId { get; init; }
    public long Sequence { get; init; }
    public long Revision { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Title { get; init; } = "";
    public string? OriginalPath { get; init; }
    public string SourceHash { get; init; } = "";
    public RecoveryDraft? Draft { get; init; }
    public ToshikiRecovery[]? Karaoke { get; init; }
    public int SelectedRow { get; init; } = -1;
}
public sealed record RecoveryEntry(string Path, RecoveryMetadata? Metadata, string? Error, string SavedFileComparison)
{
    public bool CanRecover => Error is null && Metadata is not null;
    public string Group => Metadata is {} m ? $"{m.Title} — {m.OriginalPath ?? "never saved"} [{m.DocumentId.ToString("N")[..8]}]" : "Unreadable recovery";
    public override string ToString() => Metadata is {} m ? $"{m.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} · version {m.Sequence} · {SavedFileComparison}{(Error is null ? "" : " · " + Error)}" : System.IO.Path.GetFileName(Path) + " · " + Error;
}
public sealed record RecoveredSubtitle(AssDocument Document, RecoveryMetadata Metadata);

/// <summary>Publication fence. Invalidation never waits for serialization or disk flush.</summary>
public sealed class RecoveryLease
{
    private readonly object _gate = new();
    private long _generation;
    public long Renew() { lock (_gate) return ++_generation; }
    public void Invalidate() { lock (_gate) ++_generation; }
    public bool IsCurrent(long generation) { lock (_gate) return generation == _generation; }
    internal void Publish(long generation, Action publication)
    {
        lock (_gate)
        {
            if (generation != _generation) throw new OperationCanceledException("Obsolete recovery snapshot.");
            publication();
        }
    }
}

/// <summary>One atomic bundle keeps encoded ASS and draft metadata inseparable.</summary>
public sealed class SubtitleRecoveryStore(string directory, int versionsPerDocument = 10, long storageLimit = 512L * 1024 * 1024)
{
    public const long MaximumSourceBytes = 256L * 1024 * 1024;
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string DirectoryPath { get; } = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));

    public async Task<string> WriteAsync(AssDocumentSnapshot snapshot, RecoveryMetadata metadata, RecoveryLease lease, long generation, CancellationToken token = default)
    {
        // This API deliberately owns its background boundary, including when
        // called from a UI command. CaptureSnapshot itself does no serialization.
        return await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var source = snapshot.SerializeBytes();
            if (source.LongLength > MaximumSourceBytes) throw new InvalidDataException("Subtitle exceeds the recovery size limit.");
            metadata = metadata with { Revision = snapshot.Revision, SourceHash = Convert.ToHexString(SHA256.HashData(source)) };
            var manifest = JsonSerializer.SerializeToUtf8Bytes(metadata, RecoveryJsonContext.Default.RecoveryMetadata);
            if (manifest.Length > MaximumMetadataBytes) throw new InvalidDataException("Draft exceeds the recovery size limit.");
            await _gate.WaitAsync(token).ConfigureAwait(false);
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                using var storageLock = await LockStorageAsync(token).ConfigureAwait(false);
                // Holding the cross-process publication lock proves that no
                // production writer owns these temporary recovery bundles.
                foreach(var abandoned in Directory.EnumerateFiles(DirectoryPath,"*.yoake-recovery.tmp"))
                {token.ThrowIfCancellationRequested();File.Delete(abandoned);}
                var name = $"{metadata.DocumentId:N}.{metadata.Sequence:D20}.{Guid.NewGuid():N}.yoake-recovery";
                var target = System.IO.Path.Combine(DirectoryPath, name);
                temporary = target + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    {
                        Write(archive, "document.ass", source); Write(archive, "recovery.json", manifest);
                        Write(archive, "metadata.sha256", SHA256.HashData(manifest));
                    }
                    stream.Flush(true);
                }
                ReadBundle(temporary, token); // verify before publication, never advertise a half bundle
                token.ThrowIfCancellationRequested();
                if (!lease.IsCurrent(generation)) throw new OperationCanceledException("Obsolete recovery snapshot.");
                CheckBudget(new FileInfo(temporary).Length);
                lease.Publish(generation, () => { token.ThrowIfCancellationRequested(); File.Move(temporary, target); });
                Prune(metadata.DocumentId, Math.Max(1, versionsPerDocument), target);
                return target;
            }
            finally
            {
                if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
                _gate.Release();
            }
        }, token).ConfigureAwait(false);
    }
    private static void Write(ZipArchive archive, string name, byte[] data)
    { using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open(); stream.Write(data); }

    private async Task<FileStream> LockStorageAsync(CancellationToken token)
    {
        var path = System.IO.Path.Combine(DirectoryPath, ".publication-lock");
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 300) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    public Task<RecoveryEntry[]> ListAsync(CancellationToken token = default) => Task.Run(() =>
    {
        if (!Directory.Exists(DirectoryPath)) return Array.Empty<RecoveryEntry>();
        var entries = new List<RecoveryEntry>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.yoake-recovery"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var bundle = ReadBundle(path, token); var m = bundle.Metadata;
                var comparison = "never saved";
                if (m.OriginalPath is {} original)
                {
                    comparison = "original unavailable";
                    try
                    {
                        using var file = File.OpenRead(original);
                        comparison = Convert.ToHexString(SHA256.HashData(file)) == m.SourceHash ? "same committed ASS as saved file" : "differs from saved file";
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
                if (m.Draft is not null) comparison += " · pending draft";
                entries.Add(new(path, m, null, comparison));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
            { entries.Add(new(path, null, error.Message, "validation failed")); }
        }
        return entries.OrderByDescending(e => e.Metadata?.Timestamp).ToArray();
    }, token);

    public Task<RecoveredSubtitle> ReadAsync(string path, CancellationToken token = default) => Task.Run(() =>
    {
        RequireStoredPath(path);
        var bundle = ReadBundle(path, token);
        return new RecoveredSubtitle(AssDocument.LoadBytes(bundle.Source), bundle.Metadata);
    }, token);

    private static (byte[] Source, RecoveryMetadata Metadata) ReadBundle(string path, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        if (file.Length > MaximumSourceBytes + MaximumMetadataBytes) throw new InvalidDataException("Recovery bundle is too large.");
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        if (archive.Entries.Count != 3) throw new InvalidDataException("Unexpected recovery entries.");
        var source = Read(archive, "document.ass", MaximumSourceBytes, token);
        var json = Read(archive, "recovery.json", MaximumMetadataBytes, token);
        if(!Read(archive,"metadata.sha256",32,token).AsSpan().SequenceEqual(SHA256.HashData(json)))throw new InvalidDataException("Recovery metadata checksum mismatch.");
        var m = JsonSerializer.Deserialize(json, RecoveryJsonContext.Default.RecoveryMetadata) ?? throw new InvalidDataException("Missing recovery metadata.");
        if (m.Schema != 1 || m.DocumentId == Guid.Empty || m.Sequence < 1 || m.Title is null || m.Title.Length > 4096 ||
            m.SourceHash != Convert.ToHexString(SHA256.HashData(source))) throw new InvalidDataException("Invalid recovery identity or checksum.");
        var document = AssDocument.LoadBytes(source);
        if (m.SelectedRow < -1 || m.SelectedRow >= document.Events.Count) throw new InvalidDataException("Invalid recovery selection.");
        if(m.Karaoke is {} karaoke)
        {
            if(karaoke.Length > document.Events.Count || karaoke.Any(k=>k is null || k.Row<0 || k.Row>=document.Events.Count) || karaoke.Select(k=>k.Row).Distinct().Count()!=karaoke.Length)
                throw new InvalidDataException("Invalid recovered karaoke row identity.");
            foreach(var plan in karaoke) ToshikiTimingPlan.Restore(plan);
        }
        if (m.Draft is {} draft)
        {
            if (draft.Row < 0 || draft.Row >= document.Events.Count || draft.Values is null || draft.Values.Count > 1024 || draft.OriginalText is null ||
                draft.Values.Any(p => p.Value is null || !document.Events[draft.Row].FieldNames.Contains(p.Key, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Draft does not belong to its recovered row.");
            var length = draft.Values.GetValueOrDefault("Text", "").Length;
            if (draft.Caret < 0 || draft.Caret > length || draft.SelectionStart < 0 || draft.SelectionEnd < 0 || draft.SelectionStart > length || draft.SelectionEnd > length)
                throw new InvalidDataException("Invalid draft text selection.");
        }
        return (source, m);
    }
    private static byte[] Read(ZipArchive archive, string name, long limit, CancellationToken token)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Incomplete recovery bundle.");
        if (entry.Length > limit) throw new InvalidDataException("Recovery entry exceeds its size limit.");
        using var stream = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[65536];
        int count; while ((count = stream.Read(buffer)) > 0)
        { token.ThrowIfCancellationRequested(); if (output.Length + count > limit) throw new InvalidDataException("Recovery entry exceeds its size limit."); output.Write(buffer, 0, count); }
        if (output.Length != entry.Length) throw new InvalidDataException("Incomplete recovery entry.");
        return output.ToArray();
    }
    private void CheckBudget(long incomingBytes)
    {
        var total=Directory.GetFiles(DirectoryPath,"*.yoake-recovery").Sum(p=>new FileInfo(p).Length)+incomingBytes;
        if (total > storageLimit) throw new IOException("Recovery storage limit reached. Discard obsolete recovery versions in Recover Subtitles.");
    }
    private void Prune(Guid documentId,int retain,string published)
    {
        var own=Directory.GetFiles(DirectoryPath,documentId.ToString("N")+".*.yoake-recovery")
            .Where(p=>p!=published).OrderByDescending(p=>System.IO.Path.GetFileName(p),StringComparer.Ordinal).ToArray();
        foreach(var path in own.Skip(retain-1))
        {
            // Publication succeeded first. Failed cleanup cannot make the newest
            // valid snapshot disappear or incorrectly report its write as failed.
            try{File.Delete(path);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){ }
        }
    }
    public async Task DiscardAsync(string path, CancellationToken token = default)
    {
        RequireStoredPath(path); await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var storageLock = await LockStorageAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Delete(path);
        }
        finally { _gate.Release(); }
    }
    private void RequireStoredPath(string path)
    {
        if (!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".yoake-recovery", StringComparison.Ordinal)) throw new ArgumentException("Not a recovery version in this profile.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RecoveryMetadata))]
internal partial class RecoveryJsonContext : JsonSerializerContext { }
