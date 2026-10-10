using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Yoake.Core.Automation;

namespace Yoake.Native.Automation;

// The Unicode Windows file dialog supplies 3.2.2's non-existing-file and
// multi-selection flags. Native handles and cancellation hooks stay here.
public static partial class WindowsAutomationFilePicker
{
    private sealed class State(CancellationToken token)
    {
        public nint Dialog;
        public CancellationToken Token = token;
        public void Cancel() { var dialog = Volatile.Read(ref Dialog); if (dialog != 0) _ = PostMessageW(dialog, 0x0010, 0, 0); }
    }

    public static unsafe IReadOnlyList<string>? Pick(AutomationFileDialogRequest request, nint owner, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Automation file dialogs require Windows.");
        token.ThrowIfCancellationRequested();
        foreach (var text in new[] { request.Title, request.DefaultDirectory, request.DefaultFile, request.Wildcards })
            if (text.Contains('\0')) throw new ArgumentException("File dialog arguments cannot contain NUL.");
        var parts = request.Wildcards.Length == 0 ? new[] { "All files (*.*)", "*.*" } : request.Wildcards.Split('|');
        if (parts.Length % 2 != 0 || parts.Any(p => p.Length == 0)) throw new ArgumentException("File filters must contain label|pattern pairs.");
        var filter = string.Join('\0', parts) + "\0\0";
        var buffer = new char[65536];
        if (request.DefaultFile.Length >= buffer.Length) throw new ArgumentException("Default filename is too long.");
        request.DefaultFile.CopyTo(0, buffer, 0, request.DefaultFile.Length);
        var state = new State(token); var context = GCHandle.Alloc(state);
        try
        {
            using var cancellation = token.Register(state.Cancel);
            fixed (char* path = buffer, filters = filter, title = request.Title, directory = request.DefaultDirectory)
            {
                OpenFileName dialog = new()
                {
                    Size = (uint)sizeof(OpenFileName), Owner = owner, Filter = filters, FilterIndex = 1,
                    File = path, MaxFile = (uint)buffer.Length, InitialDirectory = directory, Title = title,
                    Flags = 0x00080000 | 0x00000020 | 0x00000008, // EXPLORER, ENABLEHOOK, NOCHANGEDIR
                    CustomData = GCHandle.ToIntPtr(context),
                    Hook = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint>)&Hook
                };
                if (request.AllowMultiple) dialog.Flags |= 0x00000200;
                if (request.MustExist) dialog.Flags |= 0x00001000;
                if (request.PromptOverwrite) dialog.Flags |= 0x00000002;
                var accepted = request.Save ? GetSaveFileNameW(&dialog) : GetOpenFileNameW(&dialog);
                Volatile.Write(ref state.Dialog, 0);
                token.ThrowIfCancellationRequested();
                if (accepted == 0)
                {
                    var error = CommDlgExtendedError();
                    if (error == 0) return null;
                    throw new Win32Exception((int)error, $"Automation file dialog failed (common-dialog code 0x{error:X}).");
                }
                List<string> names = [];
                for (var start = 0; start < buffer.Length && buffer[start] != '\0';)
                {
                    var end = Array.IndexOf(buffer, '\0', start);
                    if (end < 0) throw new InvalidDataException("Native file dialog returned an unterminated path.");
                    names.Add(new string(buffer, start, end - start)); start = end + 1;
                }
                return names.Count <= 1 ? names : names.Skip(1).Select(name => Path.Combine(names[0], name)).ToArray();
            }
        }
        finally { Volatile.Write(ref state.Dialog, 0); context.Free(); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nuint Hook(nint window, uint message, nuint wparam, nint lparam)
    {
        try
        {
            if (message == 0x0110 && lparam != 0) // WM_INITDIALOG's OPENFILENAMEW
            {
                var state = (State)GCHandle.FromIntPtr(((OpenFileName*)lparam)->CustomData).Target!;
                Volatile.Write(ref state.Dialog, GetParent(window));
                if (state.Token.IsCancellationRequested) state.Cancel();
            }
        }
        catch { } // Do not unwind managed exceptions through the window procedure.
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct OpenFileName
    {
        public uint Size; public nint Owner, Instance;
        public char* Filter; public char* CustomFilter; public uint MaxCustomFilter, FilterIndex;
        public char* File; public uint MaxFile; public char* FileTitle; public uint MaxFileTitle;
        public char* InitialDirectory; public char* Title; public uint Flags;
        public ushort FileOffset, ExtensionOffset; public char* DefaultExtension;
        public nint CustomData, Hook; public char* TemplateName; public nint Reserved; public uint ReservedValue, FlagsEx;
    }
    [LibraryImport("comdlg32.dll")] private static unsafe partial int GetOpenFileNameW(OpenFileName* dialog);
    [LibraryImport("comdlg32.dll")] private static unsafe partial int GetSaveFileNameW(OpenFileName* dialog);
    [LibraryImport("comdlg32.dll")] private static partial uint CommDlgExtendedError();
    [LibraryImport("user32.dll")] private static partial nint GetParent(nint window);
    [LibraryImport("user32.dll")] private static partial int PostMessageW(nint window, uint message, nuint wparam, nint lparam);
}
