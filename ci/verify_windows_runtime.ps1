param([Parameter(Mandatory = $true)][string]$PortableRoot)
$ErrorActionPreference = 'Stop'
$PortableRoot = [IO.Path]::GetFullPath($PortableRoot)
$runtimeRoot = Join-Path $PortableRoot 'native\win-x64'
if (-not (Test-Path -LiteralPath $runtimeRoot)) { throw "Missing runtime staging directory: $runtimeRoot" }

$forbidden = Get-ChildItem -LiteralPath (Join-Path $PortableRoot 'native') -Recurse -File | Where-Object {
  $_.Extension -in @('.h', '.hpp', '.lib', '.a', '.pc', '.cmake') -or $_.FullName -match '[\\/](include|share|debug)[\\/]'
}
if ($forbidden) {
  $names = ($forbidden | ForEach-Object FullName) -join [Environment]::NewLine
  throw "Development/build files leaked into portable native runtime:`n$names"
}

$manifestPath = Join-Path $PortableRoot 'native\runtime-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Missing native runtime manifest.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
  $path = Join-Path $runtimeRoot $file.name
  if (-not (Test-Path -LiteralPath $path)) { throw "Manifest DLL is missing: $($file.name)" }
  $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne $file.sha256) { throw "Native runtime hash mismatch: $($file.name)" }
}

$ffms2 = Join-Path $runtimeRoot 'ffms2.dll'
if (-not (Test-Path -LiteralPath $ffms2)) { throw 'ffms2.dll is missing from native/win-x64.' }

Write-Host 'Staged native runtime DLLs:'
$manifest.files | Sort-Object name | ForEach-Object { Write-Host "  $($_.name)" }

# Verify the package with the Windows loader directly. Use the fully qualified
# ffms2.dll path and restrict dependency lookup to the directory containing
# ffms2.dll plus System32. This tests the portable closure itself and avoids
# process-wide search-path mutation or NativeLibrary.Load default-flag
# behavior changing across .NET/PowerShell versions.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YoakeRuntimeLoader
{
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr module);
}
'@

$LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100
$LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800
$loadFlags = $LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR -bor $LOAD_LIBRARY_SEARCH_SYSTEM32
$handle = [YoakeRuntimeLoader]::LoadLibraryEx($ffms2, [IntPtr]::Zero, $loadFlags)
if ($handle -eq [IntPtr]::Zero) {
  $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
  $errorMessage = [ComponentModel.Win32Exception]::new($errorCode).Message
  throw "LoadLibraryExW failed for '$ffms2' with Win32 error $errorCode ($errorMessage). The staged native closure is incomplete or contains an unloadable DLL."
}

try {
  Write-Host 'FFMS2 and its staged dependency closure loaded successfully with LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32.'
}
finally {
  if (-not [YoakeRuntimeLoader]::FreeLibrary($handle)) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    Write-Warning "FreeLibrary failed for FFMS2 with Win32 error $errorCode."
  }
}
