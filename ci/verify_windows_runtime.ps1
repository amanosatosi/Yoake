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

foreach ($entrypoint in $manifest.entrypoints) {
  $entryPath = Join-Path $runtimeRoot $entrypoint
  if (-not (Test-Path -LiteralPath $entryPath)) {
    throw "Native runtime entrypoint is missing from native/win-x64: $entrypoint"
  }
}

Write-Host 'Staged native runtime DLLs:'
$manifest.files | Sort-Object name | ForEach-Object { Write-Host "  $($_.name)" }

# Verify each packaged native entrypoint with the Windows loader directly.
# The full path plus DLL_LOAD_DIR/System32 checks the portable closure without
# depending on the machine-wide DLL search path.
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

foreach ($entrypoint in $manifest.entrypoints) {
  $entryPath = Join-Path $runtimeRoot $entrypoint
  $handle = [YoakeRuntimeLoader]::LoadLibraryEx($entryPath, [IntPtr]::Zero, $loadFlags)
  if ($handle -eq [IntPtr]::Zero) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $errorMessage = [ComponentModel.Win32Exception]::new($errorCode).Message
    throw "LoadLibraryExW failed for '$entryPath' with Win32 error $errorCode ($errorMessage). The staged native closure is incomplete or contains an unloadable DLL."
  }

  try {
    Write-Host "$entrypoint loaded successfully from the staged portable runtime."
  }
  finally {
    if (-not [YoakeRuntimeLoader]::FreeLibrary($handle)) {
      $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
      Write-Warning "FreeLibrary failed for $entrypoint with Win32 error $errorCode."
    }
  }
}
