using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Fonts;

public enum FontMatch { Exact, Missing, Substituted, Error }
public enum FontCollectionMode { Check, Folder, ScriptFolder, Zip }
public enum FontLogLevel { Information, Success, Warning, Error }
public sealed record FontProgress(double Fraction,FontLogLevel Level,string Message);
public sealed record ResolvedFontFile(string Path,long Length,long LastWriteTicks,OpenTypeMetadata? Metadata,string? MetadataError=null)
{
    public bool CopyRestricted=>Metadata?.CopyRestricted==true;
    public static ResolvedFontFile Inspect(string path,CancellationToken token=default)
    {
        var file=new FileInfo(System.IO.Path.GetFullPath(path));var length=file.Length;var modified=file.LastWriteTimeUtc.Ticks;OpenTypeMetadata? metadata=null;string? error=null;
        try{using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.Read);metadata=OpenTypeMetadata.Read(stream,token);}
        catch(Exception failure)when(failure is IOException or InvalidDataException or ArgumentException){error=failure.Message;}
        token.ThrowIfCancellationRequested();file.Refresh();if(!file.Exists||file.Length!=length||file.LastWriteTimeUtc.Ticks!=modified)throw new IOException("Font file changed while reading metadata: "+path);
        return new(file.FullName,length,modified,metadata,error);
    }
}
public sealed record ResolvedFont(FontUsage Usage,FontMatch Match,string ResolvedFamily,string Face,uint FaceIndex,bool SyntheticBold,bool SyntheticItalic,int[] MissingGlyphs,ResolvedFontFile[] Files,string[] Notes)
{
    public override string ToString()
    {
        var glyphs=MissingGlyphs.Length==0?"":$"\nMissing {MissingGlyphs.Length} scalar glyphs: "+string.Join(" ",MissingGlyphs.Take(40).Select(c=>$"U+{c:X4}"));
        return $"{Usage.Request}\n{Match}: {ResolvedFamily} {Face} · face index {FaceIndex}"+
            (SyntheticBold?" · simulated/unavailable bold":"")+(SyntheticItalic?" · simulated/unavailable italic":"")+glyphs+
            "\n"+string.Join("\n",Files.Select(f=>f.Path+(f.CopyRestricted?" · restricted embedding — excluded from copy":"")))+
            (Notes.Length==0?"":"\n"+string.Join("\n",Notes));
    }
}
public interface IFontFileResolver:IDisposable { ResolvedFont Resolve(FontUsage usage,CancellationToken token=default); }
public interface IFontResolverFactory { IFontFileResolver Create(); }
public sealed record FontCollectionReport(ResolvedFont[] Fonts,string[] Warnings)
{
    public int MissingFonts=>Fonts.Count(f=>f.Match!=FontMatch.Exact);
    public bool Complete=>MissingFonts==0&&Warnings.Length==0&&Fonts.All(f=>f.Files.Length>0&&f.MissingGlyphs.Length==0&&!f.SyntheticBold&&!f.SyntheticItalic&&f.Files.All(p=>!p.CopyRestricted&&p.Metadata is not null&&!p.Metadata.RightsUnknown));
    public string Summary=>Fonts.Length==0?"No rendered font requirements were found.":$"{Fonts.Length} font requests; {MissingFonts} missing/substituted/unresolved; {Fonts.Sum(f=>f.MissingGlyphs.Length)} missing scalar glyphs. "+(Complete?"All requested fonts and scalar glyphs found.":"Review warnings and missing-font details.");
}
public sealed record FontCollectionOutput(string Path,int Files,bool Complete);

public static class FontCollection
{
    public const string LicenseNotice="Font availability and embedding flags do not establish redistribution permission. Verify the license of every collected font before distribution. Files are copied whole; no subsetting, font installation or subtitle modification occurs.";
    public static FontCollectionReport Scan(FontUsageSnapshot snapshot,bool includeUnusedStyles,IFontResolverFactory factory,IProgress<FontProgress>? progress=null,CancellationToken token=default)
    {
        var requirements=snapshot.Scan(includeUnusedStyles,token);var result=new List<ResolvedFont>();using var resolver=factory.Create();
        foreach(var warning in requirements.Warnings)progress?.Report(new(0,FontLogLevel.Warning,warning));
        for(var i=0;i<requirements.Fonts.Length;i++)
        {
            token.ThrowIfCancellationRequested();var usage=requirements.Fonts[i];ResolvedFont font;
            try{font=resolver.Resolve(usage,token);}catch(OperationCanceledException){throw;}
            catch(Exception error)when(error is IOException or InvalidDataException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {font=new(usage,FontMatch.Error,"","",0,false,false,[],[],[error.Message]);}
            result.Add(font);progress?.Report(new((i+1d)/Math.Max(1,requirements.Fonts.Length),font.Match==FontMatch.Exact&&font.MissingGlyphs.Length==0?FontLogLevel.Success:FontLogLevel.Warning,font.ToString()));
        }
        token.ThrowIfCancellationRequested();return new(result.ToArray(),requirements.Warnings);
    }
    /// <summary>Stage a complete collection, then atomically publish a new directory or ZIP.</summary>
    public static async Task<FontCollectionOutput> WriteAsync(FontCollectionReport report,FontCollectionMode mode,string destination,bool rightsAcknowledged,RecoveryLease lease,long generation,IProgress<FontProgress>? progress=null,CancellationToken token=default)
    {
        if(mode is not (FontCollectionMode.Folder or FontCollectionMode.ScriptFolder or FontCollectionMode.Zip))throw new ArgumentException("Choose a copy mode.");
        if(!rightsAcknowledged)throw new InvalidOperationException("Confirm that you have checked font redistribution permission before copying.");
        token.ThrowIfCancellationRequested();var files=report.Fonts.Where(f=>f.Match==FontMatch.Exact).SelectMany(f=>f.Files).GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.FirstOrDefault(f=>f.CopyRestricted)??g.First()).ToArray();
        foreach(var restricted in files.Where(f=>f.CopyRestricted))progress?.Report(new(0,FontLogLevel.Warning,"Excluded restricted-embedding file: "+restricted.Path));
        var eligible=files.Where(f=>!f.CopyRestricted).ToArray();if(eligible.Length==0)throw new InvalidOperationException("No resolved, permitted font files are available. Nothing was written.");
        var full=Path.GetFullPath(destination);var parent=mode==FontCollectionMode.Zip?Path.GetDirectoryName(full)!:full;
        if(mode==FontCollectionMode.Zip&&!Path.GetExtension(full).Equals(".zip",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a ZIP filename.");
        Directory.CreateDirectory(parent);var staging=Path.Combine(parent,".yoake-fonts-"+Guid.NewGuid().ToString("N")+".tmp");
        if(Directory.Exists(staging)||File.Exists(staging))throw new IOException("Font staging path already exists.");Directory.CreateDirectory(staging);string? zip=null;
        try
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"FONT-LICENSE-NOTICE.txt"};var hashes=new HashSet<string>(StringComparer.Ordinal);var copied=new List<(ResolvedFontFile File,string Name)>();
            for(var i=0;i<eligible.Length;i++)
            {
                token.ThrowIfCancellationRequested();var file=eligible[i];Verify(file);var name=Path.GetFileName(file.Path);AttachmentFileNames.RequireSafe(name);
                var temporary=Path.Combine(staging,Guid.NewGuid().ToString("N")+".part");
                await using(var input=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan))
                await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
                {await input.CopyToAsync(output,81920,token).ConfigureAwait(false);await output.FlushAsync(token).ConfigureAwait(false);output.Flush(true);}
                Verify(file);string hash;
                await using(var input=new FileStream(temporary,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous))hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token).ConfigureAwait(false));
                if(!hashes.Add(hash)){File.Delete(temporary);progress?.Report(new((i+1d)/eligible.Length,FontLogLevel.Information,"Deduplicated identical bytes: "+file.Path));continue;}
                if(!names.Add(name))
                {
                    var stem=Path.GetFileNameWithoutExtension(name);var extension=Path.GetExtension(name);var candidate=stem+"-"+hash[..12]+extension;var n=2;
                    while(!names.Add(candidate))candidate=stem+"-"+hash[..12]+"-"+(n++)+extension;name=candidate;
                }
                AttachmentFileNames.RequireSafe(name);File.Move(temporary,Path.Combine(staging,name));copied.Add((file,name));
                progress?.Report(new((i+1d)/eligible.Length,FontLogLevel.Information,"Prepared "+name));
            }
            token.ThrowIfCancellationRequested();
            var notice=new StringBuilder(LicenseNotice+"\n\n");foreach(var font in report.Fonts)notice.AppendLine(font.ToString());
            foreach(var pair in copied)
            {notice.AppendLine("\n"+pair.Name+" ← "+pair.File.Path);if(pair.File.MetadataError is {} error)notice.AppendLine("Embedding metadata unavailable: "+error);foreach(var face in pair.File.Metadata?.Faces??[])foreach(var license in face.License)notice.AppendLine(license);}
            await File.WriteAllTextAsync(Path.Combine(staging,"FONT-LICENSE-NOTICE.txt"),notice.ToString(),new UTF8Encoding(false),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();string target;
            if(mode==FontCollectionMode.Zip)
            {
                zip=Path.Combine(parent,".yoake-fonts-"+Guid.NewGuid().ToString("N")+".zip.tmp");
                await using(var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
                {
                    using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true,Encoding.UTF8))
                    foreach(var path in Directory.EnumerateFiles(staging).Order(StringComparer.OrdinalIgnoreCase))
                    {token.ThrowIfCancellationRequested();var entry=archive.CreateEntry(Path.GetFileName(path),CompressionLevel.Optimal);await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous);await using var entryStream=entry.Open();await input.CopyToAsync(entryStream,81920,token).ConfigureAwait(false);}
                    await output.FlushAsync(token).ConfigureAwait(false);output.Flush(true);
                }
                target=Unique(parent,Path.GetFileName(full),false);lease.Publish(generation,()=>{token.ThrowIfCancellationRequested();File.Move(zip,target,false);});zip=null;
            }
            else
            {target=Unique(parent,"fonts",true);lease.Publish(generation,()=>{token.ThrowIfCancellationRequested();Directory.Move(staging,target);});}
            progress?.Report(new(1,report.Complete&&eligible.Length==files.Length?FontLogLevel.Success:FontLogLevel.Warning,$"Published {copied.Count} complete font files at {target}. "+report.Summary));
            return new(target,copied.Count,report.Complete&&eligible.Length==files.Length);
        }
        finally{if(zip is not null&&File.Exists(zip))File.Delete(zip);if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    private static void Verify(ResolvedFontFile file)
    {var live=new FileInfo(file.Path);if(!live.Exists||live.Length!=file.Length||live.LastWriteTimeUtc.Ticks!=file.LastWriteTicks)throw new IOException("Resolved font changed or disappeared. Scan again: "+file.Path);}
    private static string Unique(string parent,string name,bool directory)
    {
        var names=Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);var candidate=name;var stem=directory?name:Path.GetFileNameWithoutExtension(name);var extension=directory?"":Path.GetExtension(name);
        for(var i=2;names.Contains(candidate);i++){if(i>10000)throw new IOException("Too many colliding collection names.");candidate=stem+"-"+i+extension;}
        return Path.Combine(parent,candidate);
    }
}
