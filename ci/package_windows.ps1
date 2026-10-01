param(
  [Parameter(Mandatory = $true)][string]$PublishRoot,
  [Parameter(Mandatory = $true)][string]$NativeRoot,
  [Parameter(Mandatory = $true)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$PublishRoot = [IO.Path]::GetFullPath($PublishRoot)
$NativeRoot = [IO.Path]::GetFullPath($NativeRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$portable = Join-Path $OutputRoot 'Yoake-Windows-x64-portable'
if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable -Recurse -Force }
New-Item -ItemType Directory -Force -Path $portable | Out-Null
Copy-Item (Join-Path $PublishRoot '*') $portable -Recurse -Force

$runtimeRoot = Join-Path $portable 'native\win-x64'
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
$searchDirectories = @(
  (Join-Path $NativeRoot 'ffms2\bin'),
  (Join-Path $NativeRoot 'ffmpeg\x64-windows\bin')
)
foreach ($directory in @($searchDirectories)) {
  if (-not (Test-Path -LiteralPath $directory)) { throw "Missing native runtime source directory: $directory" }
}

# FFMS2/vcpkg use the dynamic MSVC CRT under the preserved old-Yoake build
# strategy. Treat the Visual C++ redistributable as part of the dependency
# closure when dumpbin reports one of its DLLs; do not copy the whole redist.
#
# Do not assume a particular Microsoft.VC*.CRT directory name or that the
# selected toolset's redistributable has one fixed layout. VS2026 hosted
# runners can move these directories while keeping VCToolsRedistDir valid.
# Locate the actual x64 CRT by VCRUNTIME140.dll, preferring VCToolsRedistDir
# and then falling back to the Visual Studio VC redist tree.
$vcRedistRoots = [System.Collections.Generic.List[string]]::new()
if ($env:VCToolsRedistDir) {
  $vcRedistRoots.Add([IO.Path]::GetFullPath($env:VCToolsRedistDir))
}
if ($env:VCINSTALLDIR) {
  $vcRedistRoots.Add([IO.Path]::GetFullPath((Join-Path $env:VCINSTALLDIR 'Redist\MSVC')))
}
if ($env:VSINSTALLDIR) {
  $vcRedistRoots.Add([IO.Path]::GetFullPath((Join-Path $env:VSINSTALLDIR 'VC\Redist\MSVC')))
}

$vcRuntimeDirectories = @()
foreach ($redistRoot in ($vcRedistRoots | Select-Object -Unique)) {
  if (-not (Test-Path -LiteralPath $redistRoot)) { continue }

  $runtimeMatches = @(
    Get-ChildItem -LiteralPath $redistRoot -Recurse -File -Filter 'VCRUNTIME140.dll' -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '[\\/]x64[\\/]' } |
      Sort-Object FullName
  )
  if ($runtimeMatches.Count -eq 0) { continue }

  $vcRuntimeDirectories = @(
    $runtimeMatches |
      ForEach-Object { $_.Directory.FullName } |
      Select-Object -Unique
  )
  Write-Host "Resolved Visual C++ runtime directory from '$redistRoot': $($vcRuntimeDirectories -join ', ')"
  break
}

if ($vcRuntimeDirectories.Count -gt 0) {
  $searchDirectories += $vcRuntimeDirectories
} else {
  Write-Warning "Could not locate VCRUNTIME140.dll in Visual Studio redistributable roots. Native dependency resolution will fail if FFMS2 imports the dynamic MSVC CRT."
}

$dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
if (-not $dumpbin) { throw 'dumpbin.exe is required to resolve the portable native runtime dependency closure.' }

$available = @{}
foreach ($directory in $searchDirectories) {
  Get-ChildItem -LiteralPath $directory -Filter '*.dll' -File | ForEach-Object {
    $key = $_.Name.ToLowerInvariant()
    if ($available.ContainsKey($key) -and $available[$key] -ne $_.FullName) {
      throw "Ambiguous native runtime DLL '$($_.Name)' exists in more than one source directory."
    }
    $available[$key] = $_.FullName
  }
}

function Get-ImportedDllNames([string]$BinaryPath) {
  $output = & $dumpbin.Source /nologo /dependents $BinaryPath 2>&1
  if ($LASTEXITCODE -ne 0) { throw "dumpbin failed for '$BinaryPath' with exit code $LASTEXITCODE" }
  foreach ($line in $output) {
    if ($line -match '^\s+([A-Za-z0-9_.+\-]+\.dll)\s*$') {
      $matches[1]
    }
  }
}

$entryDll = Join-Path $NativeRoot 'ffms2\bin\ffms2.dll'
if (-not (Test-Path -LiteralPath $entryDll)) { throw "Missing FFMS2 runtime entry DLL: $entryDll" }
$queue = [System.Collections.Generic.Queue[string]]::new()
$queue.Enqueue($entryDll)
$selected = @{}

while ($queue.Count -gt 0) {
  $binary = $queue.Dequeue()
  $name = [IO.Path]::GetFileName($binary)
  $key = $name.ToLowerInvariant()
  if ($selected.ContainsKey($key)) { continue }
  $selected[$key] = $binary

  foreach ($dependency in (Get-ImportedDllNames $binary)) {
    $dependencyKey = $dependency.ToLowerInvariant()
    if ($available.ContainsKey($dependencyKey) -and -not $selected.ContainsKey($dependencyKey)) {
      $queue.Enqueue($available[$dependencyKey])
      continue
    }
    if ($dependencyKey -match '^(vcruntime140|msvcp140|concrt140|vcomp140).*\.dll$') {
      throw "Required Visual C++ runtime DLL '$dependency' was not found in the resolved Visual Studio redistributable directories."
    }
  }
}

foreach ($key in ($selected.Keys | Sort-Object)) {
  Copy-Item -LiteralPath $selected[$key] -Destination (Join-Path $runtimeRoot ([IO.Path]::GetFileName($selected[$key]))) -Force
}

$manifest = [ordered]@{
  architecture = 'win-x64'
  entrypoints = @('ffms2.dll')
  files = @(
    Get-ChildItem -LiteralPath $runtimeRoot -Filter '*.dll' -File | Sort-Object Name | ForEach-Object {
      [ordered]@{
        name = $_.Name
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      }
    }
  )
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $portable 'native\runtime-manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $NativeRoot 'versions.json') -Destination (Join-Path $portable 'native\versions.json')

$licenseTarget = Join-Path $portable 'licenses'
New-Item -ItemType Directory -Force -Path $licenseTarget | Out-Null
if (Test-Path -LiteralPath (Join-Path $NativeRoot 'licenses')) {
  Get-ChildItem -LiteralPath (Join-Path $NativeRoot 'licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licenseTarget -Force
  }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\licenses\Avalonia-MIT.txt') -Destination $licenseTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $portable 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $portable 'THIRD_PARTY_NOTICES.md') -Force

$zip = Join-Path $OutputRoot 'Yoake-Windows-x64-portable.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Staged $($selected.Count) native runtime DLL(s) in native/win-x64."
Write-Host "Created $zip"
