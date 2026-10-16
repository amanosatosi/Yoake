// Behavioral algorithms adapted from the supplied Aegisub source.
// Copyright (c) 2016 Thomas Goyne. ISC permission:
// Permission to use, copy, modify, and distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
// OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
// CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Yoake.Core.Fonts;

namespace Yoake.Native;

public sealed class WindowsFontResolverFactory:IFontResolverFactory
{public IFontFileResolver Create()=>new WindowsFontFileResolver();}

// Supported Windows font metadata APIs only. COM vtables are declared from
// Microsoft's dwrite.h; no RCWs, reflection, runtime code generation or decoder.
public sealed unsafe partial class WindowsFontFileResolver:IFontFileResolver
{
    private nint _factory,_collection,_interop,_dc;
    private Dictionary<string,(uint Family,uint Font)>? _fullNames;
    private readonly Dictionary<string,ResolvedFontFile> _files=new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private static readonly Guid FactoryId=new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"),LocalLoaderId=new("b2d9f3ec-c9fe-4a11-a2ec-d86208f7c0a2");
    public WindowsFontFileResolver()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Font file discovery requires Windows DirectWrite.");
        try
        {
            Check(DWriteCreateFactory(0,FactoryId,out _factory));
            nint collection=0;Check(((delegate* unmanaged[Stdcall]<nint,nint*,int,int>)Method(_factory,3))(_factory,&collection,1));_collection=collection;
        }
        catch{Dispose();throw;}
    }
    public ResolvedFont Resolve(FontUsage usage,CancellationToken token=default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);token.ThrowIfCancellationRequested();var name=usage.Request.Family.Trim().TrimStart('@');
        if(name.Length is 0 or >1024||name.Any(char.IsControl))return Missing(usage,"Invalid or empty requested family.");
        uint familyIndex=0;int exists=0;fixed(char* text=name)Check(((delegate* unmanaged[Stdcall]<nint,char*,uint*,int*,int>)Method(_collection,5))(_collection,text,&familyIndex,&exists));
        uint? fontIndex=null;
        if(exists==0)
        {
            BuildAliases(token);
            if(_fullNames!.TryGetValue(name,out var alias)){familyIndex=alias.Family;fontIndex=alias.Font;exists=1;}
        }
        if(exists==0)return FromGdi(usage,name,token);
        nint family=0,font=0,face=0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)Method(_collection,4))(_collection,familyIndex,&family));
            if(fontIndex is {} index)Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)Method(family,5))(family,index,&font));
            else Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,uint,nint*,int>)Method(family,7))(family,(uint)usage.Request.Weight,5,usage.Request.Italic?2u:0u,&font));
            var familyName=NamesAt(family,6).FirstOrDefault()??name;var faceName=NamesAt(font,8).FirstOrDefault()??"";
            var weight=((delegate* unmanaged[Stdcall]<nint,uint>)Method(font,4))(font);var style=((delegate* unmanaged[Stdcall]<nint,uint>)Method(font,6))(font);
            Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)Method(font,13))(font,&face));
            return Describe(usage,face,familyName,faceName,usage.Request.Weight>=600&&weight<600,usage.Request.Italic&&style==0,token);
        }
        finally{Release(face);Release(font);Release(family);}
    }
    private void BuildAliases(CancellationToken token)
    {
        if(_fullNames is not null)return;var result=new Dictionary<string,(uint,uint)>(StringComparer.OrdinalIgnoreCase);
        var count=((delegate* unmanaged[Stdcall]<nint,uint>)Method(_collection,3))(_collection);if(count>50000)throw new InvalidOperationException("Unreasonable system font family count.");
        for(uint i=0;i<count;i++)
        {
            token.ThrowIfCancellationRequested();nint family=0;
            try
            {
                Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)Method(_collection,4))(_collection,i,&family));var fonts=((delegate* unmanaged[Stdcall]<nint,uint>)Method(family,4))(family);
                if(fonts>4096)throw new InvalidOperationException("Unreasonable font face count.");
                for(uint j=0;j<fonts;j++)
                {
                    token.ThrowIfCancellationRequested();nint font=0,strings=0;int exists=0;
                    try
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)Method(family,5))(family,j,&font));
                        Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int*,int>)Method(font,9))(font,16,&strings,&exists));
                        if(exists!=0)foreach(var name in Names(strings))result.TryAdd(name,(i,j));
                    }
                    finally{Release(strings);Release(font);}
                }
            }
            finally{Release(family);}
        }
        _fullNames=result;
    }
    private ResolvedFont FromGdi(FontUsage usage,string requested,CancellationToken token)
    {
        // Font resources private to this process are not in the DirectWrite
        // system collection. The same supported GDI->DirectWrite bridge used
        // by later Aegisub resolves them, with exact alias verification.
        if(_interop==0){nint interop=0;Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)Method(_factory,17))(_factory,&interop));_interop=interop;}
        if(_dc==0){_dc=CreateCompatibleDC(0);if(_dc==0)throw new Win32Exception(Marshal.GetLastPInvokeError());}
        var font=CreateFontW(-16,0,0,0,usage.Request.Weight,usage.Request.Italic?1u:0u,0,0,1,4,0,4,0,requested);if(font==0)throw new Win32Exception(Marshal.GetLastPInvokeError());
        nint previous=0,face=0;
        try
        {
            previous=SelectObject(_dc,font);if(previous==0||previous==-1)throw new Win32Exception(Marshal.GetLastPInvokeError());
            var buffer=new char[1024];fixed(char* text=buffer)if(GetTextFaceW(_dc,buffer.Length,text)==0)throw new Win32Exception(Marshal.GetLastPInvokeError());
            var end=Array.IndexOf(buffer,'\0');var selected=new string(buffer,0,end<0?buffer.Length:end);
            Check(((delegate* unmanaged[Stdcall]<nint,nint,nint*,int>)Method(_interop,6))(_interop,_dc,&face));
            var names=FaceNames(face);var aliases=names.Where(p=>p.Key is 1 or 4 or 16).SelectMany(p=>p.Value);
            if(!selected.Equals(requested,StringComparison.OrdinalIgnoreCase)&&!aliases.Contains(requested,StringComparer.OrdinalIgnoreCase))
                return new(usage,FontMatch.Substituted,selected,"",0,false,false,[],[],["The requested font is unavailable; this Windows substitution is not collected."]);
            var faceName=(names.GetValueOrDefault(17)??names.GetValueOrDefault(2))?.FirstOrDefault()??"";
            return Describe(usage,face,selected,faceName,false,false,token);
        }
        finally{Release(face);if(previous!=0&&previous!=-1)SelectObject(_dc,previous);DeleteObject(font);}
    }
    private ResolvedFont Describe(FontUsage usage,nint face,string family,string faceName,bool syntheticBold,bool syntheticItalic,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var index=((delegate* unmanaged[Stdcall]<nint,uint>)Method(face,5))(face);var simulations=((delegate* unmanaged[Stdcall]<nint,uint>)Method(face,6))(face);
        var missing=new List<int>();
        foreach(var batch in usage.Characters.Chunk(512))
        {
            token.ThrowIfCancellationRequested();var codepoints=batch.Select(c=>(uint)c).ToArray();var glyphs=new ushort[codepoints.Length];
            fixed(uint* characters=codepoints)fixed(ushort* output=glyphs)Check(((delegate* unmanaged[Stdcall]<nint,uint*,uint,ushort*,int>)Method(face,11))(face,characters,(uint)codepoints.Length,output));
            for(var i=0;i<glyphs.Length;i++)if(glyphs[i]==0)missing.Add(batch[i]);
        }
        uint count=0;Check(((delegate* unmanaged[Stdcall]<nint,uint*,nint*,int>)Method(face,4))(face,&count,null));if(count is 0 or >16)throw new InvalidOperationException("Unsupported font file count.");
        var files=new nint[count];var resolved=new List<ResolvedFontFile>();var notes=new List<string>();
        try
        {
            fixed(nint* pointers=files)Check(((delegate* unmanaged[Stdcall]<nint,uint*,nint*,int>)Method(face,4))(face,&count,pointers));
            foreach(var file in files)
            {
                token.ThrowIfCancellationRequested();var path=FilePath(file);
                if(!_files.TryGetValue(path,out var metadata))_files[path]=metadata=ResolvedFontFile.Inspect(path,token);resolved.Add(metadata);
                if(metadata.MetadataError is {} error)notes.Add("Embedding metadata unavailable: "+error);
                if(metadata.Metadata?.Faces.Any(f=>f.Variable)==true)notes.Add("Variable font: the complete source file is collected; no new axis instance or subset is generated.");
                if(metadata.Metadata?.RightsUnknown==true)notes.Add("Embedding flags are unavailable for at least one face; review its license.");
            }
        }
        finally{foreach(var file in files)Release(file);}
        return new(usage,FontMatch.Exact,family,faceName,index,syntheticBold||(simulations&1)!=0,syntheticItalic||(simulations&2)!=0,missing.ToArray(),resolved.ToArray(),notes.Distinct().ToArray());
    }
    private static string FilePath(nint file)
    {
        nint loader=0,local=0;void* key=null;uint size=0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint,void**,uint*,int>)Method(file,3))(file,&key,&size));
            Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)Method(file,4))(file,&loader));var id=LocalLoaderId;
            Check(((delegate* unmanaged[Stdcall]<nint,Guid*,nint*,int>)Method(loader,0))(loader,&id,&local));uint length=0;
            Check(((delegate* unmanaged[Stdcall]<nint,void*,uint,uint*,int>)Method(local,4))(local,key,size,&length));if(length is 0 or >32767)throw new InvalidOperationException("Invalid local font path length.");
            var buffer=new char[length+1];fixed(char* text=buffer)Check(((delegate* unmanaged[Stdcall]<nint,void*,uint,char*,uint,int>)Method(local,5))(local,key,size,text,length+1));
            return Path.GetFullPath(new string(buffer,0,(int)length));
        }
        finally{Release(local);Release(loader);}
    }
    private static Dictionary<int,List<string>> FaceNames(nint face)
    {
        void* data=null;void* context=null;uint length=0;int exists=0;
        Check(((delegate* unmanaged[Stdcall]<nint,uint,void**,uint*,void**,int*,int>)Method(face,12))(face,0x656D616E,&data,&length,&context,&exists));
        try
        {if(exists==0)return [];if(length>1024*1024)throw new InvalidOperationException("Font name table exceeds the metadata limit.");return OpenTypeMetadata.ReadNames(new ReadOnlySpan<byte>(data,(int)length));}
        finally{if(context!=null)((delegate* unmanaged[Stdcall]<nint,void*,void>)Method(face,13))(face,context);}
    }
    private static string[] NamesAt(nint owner,int method)
    {nint strings=0;try{Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)Method(owner,method))(owner,&strings));return Names(strings);}finally{Release(strings);}}
    private static string[] Names(nint strings)
    {
        if(strings==0)return [];var count=((delegate* unmanaged[Stdcall]<nint,uint>)Method(strings,3))(strings);if(count>4096)throw new InvalidOperationException("Too many localized font names.");var result=new List<string>();
        for(uint i=0;i<count;i++)
        {uint length=0;Check(((delegate* unmanaged[Stdcall]<nint,uint,uint*,int>)Method(strings,7))(strings,i,&length));if(length>65535)throw new InvalidOperationException("Font name is too long.");var buffer=new char[length+1];fixed(char* text=buffer)Check(((delegate* unmanaged[Stdcall]<nint,uint,char*,uint,int>)Method(strings,8))(strings,i,text,length+1));result.Add(new string(buffer,0,(int)length));}
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static ResolvedFont Missing(FontUsage usage,string note)=>new(usage,FontMatch.Missing,"","",0,false,false,[],[],[note]);
    private static nint Method(nint pointer,int index){if(pointer==0)throw new InvalidOperationException("Windows font API returned a null interface.");return ((nint*)*(nint*)pointer)[index];}
    private static void Check(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
    private static void Release(nint pointer){if(pointer!=0)((delegate* unmanaged[Stdcall]<nint,uint>)Method(pointer,2))(pointer);}
    public void Dispose()
    {if(_disposed)return;_disposed=true;if(_dc!=0)DeleteDC(_dc);Release(_interop);Release(_collection);Release(_factory);_dc=_interop=_collection=_factory=0;}
    internal static IDisposable RegisterAcceptanceFont(string path)
    {if(AddFontResourceExW(path,0x10,0)==0)throw new Win32Exception(Marshal.GetLastPInvokeError(),"Controlled acceptance font could not be registered.");return new AcceptanceFont(path);}
    private sealed class AcceptanceFont(string path):IDisposable
    {private bool _disposed;public void Dispose(){if(_disposed)return;_disposed=true;RemoveFontResourceExW(path,0x10,0);}}
    [LibraryImport("dwrite.dll")]private static partial int DWriteCreateFactory(uint type,in Guid id,out nint factory);
    [LibraryImport("gdi32.dll",SetLastError=true)]private static partial nint CreateCompatibleDC(nint device);
    [LibraryImport("gdi32.dll",SetLastError=true)]private static partial int DeleteDC(nint device);
    [LibraryImport("gdi32.dll",SetLastError=true)]private static partial nint SelectObject(nint device,nint value);
    [LibraryImport("gdi32.dll",SetLastError=true)]private static partial int DeleteObject(nint value);
    [LibraryImport("gdi32.dll",SetLastError=true)]private static partial int GetTextFaceW(nint device,int count,char* name);
    [LibraryImport("gdi32.dll",StringMarshalling=StringMarshalling.Utf16,SetLastError=true)]private static partial nint CreateFontW(int height,int width,int escapement,int orientation,int weight,uint italic,uint underline,uint strikeout,uint charset,uint outputPrecision,uint clipPrecision,uint quality,uint pitch,string family);
    [LibraryImport("gdi32.dll",StringMarshalling=StringMarshalling.Utf16,SetLastError=true)]private static partial int AddFontResourceExW(string path,uint flags,nint reserved);
    [LibraryImport("gdi32.dll",StringMarshalling=StringMarshalling.Utf16,SetLastError=true)]private static partial int RemoveFontResourceExW(string path,uint flags,nint reserved);
}
