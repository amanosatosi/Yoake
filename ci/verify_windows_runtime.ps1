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

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YoakeRuntimeSearch
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDefaultDllDirectories(uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr AddDllDirectory(string directory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveDllDirectory(IntPtr cookie);
}
'@

$LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000
if (-not [YoakeRuntimeSearch]::SetDefaultDllDirectories($LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)) {
  throw "SetDefaultDllDirectories failed with Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
}
$cookie = [YoakeRuntimeSearch]::AddDllDirectory($runtimeRoot)
if ($cookie -eq [IntPtr]::Zero) {
  throw "AddDllDirectory failed with Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
}

$handle = [IntPtr]::Zero
try {
  $handle = [System.Runtime.InteropServices.NativeLibrary]::Load('ffms2.dll')
  if ($handle -eq [IntPtr]::Zero) { throw 'NativeLibrary.Load returned a null FFMS2 handle.' }
  Write-Host 'FFMS2 and its staged dependency closure loaded successfully with the safe DLL search path.'
}
finally {
  if ($handle -ne [IntPtr]::Zero) { [System.Runtime.InteropServices.NativeLibrary]::Free($handle) }
  [void][YoakeRuntimeSearch]::RemoveDllDirectory($cookie)
}
