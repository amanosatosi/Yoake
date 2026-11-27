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
$portable = Join-Path $OutputRoot 'Yoake'
if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable -Recurse -Force }
New-Item -ItemType Directory -Force -Path $portable | Out-Null
Copy-Item (Join-Path $PublishRoot '*') $portable -Recurse -Force

$runtimeRoot = Join-Path $portable 'native\win-x64'
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
$searchDirectories = @(
  (Join-Path $NativeRoot 'ffms2\bin'),
  (Join-Path $NativeRoot 'ffmpeg\x64-windows\bin'),
  (Join-Path $NativeRoot 'mangetsu\bin'),
  (Join-Path $NativeRoot 'automation\bin'),
  (Join-Path $NativeRoot 'motion\bin')
)
foreach ($directory in @($searchDirectories)) {
  if (-not (Test-Path -LiteralPath $directory)) { throw "Missing native runtime source directory: $directory" }
}

# FFMS2/vcpkg use the dynamic MSVC CRT under the preserved old-Yoake build
# strategy. Treat the Visual C++ redistributable as part of the dependency
# closure when dumpbin reports one of its DLLs; do not copy the whole redist.
#
# Prefer the normal desktop x64 CRT. Visual Studio also ships onecore and
# Spectre variants containing DLLs with the same names; mixing those into the
# lookup makes the dependency closure ambiguous and is incorrect for Yoake's
# ordinary desktop FFMS2/vcpkg build.
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

  # First try the canonical desktop layout directly beneath this toolset.
  $desktopX64Root = Join-Path $redistRoot 'x64'
  if (Test-Path -LiteralPath $desktopX64Root) {
    $desktopRuntimeDirectories = @(
      Get-ChildItem -LiteralPath $desktopX64Root -Directory -Filter 'Microsoft.VC*.CRT' -ErrorAction SilentlyContinue |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'VCRUNTIME140.dll') } |
        Sort-Object FullName
    )
    if ($desktopRuntimeDirectories.Count -gt 0) {
      $vcRuntimeDirectories = @($desktopRuntimeDirectories[0].FullName)
    }
  }

  # Fallback for a broader VC\Redist\MSVC root: find a desktop x64 CRT, but
  # explicitly reject onecore and Spectre variants which duplicate DLL names.
  if ($vcRuntimeDirectories.Count -eq 0) {
    $runtimeMatches = @(
      Get-ChildItem -LiteralPath $redistRoot -Recurse -File -Filter 'VCRUNTIME140.dll' -ErrorAction SilentlyContinue |
        Where-Object {
          $_.FullName -match '[\\/]x64[\\/]Microsoft\.VC[^\\/]*\.CRT[\\/]VCRUNTIME140\.dll$' -and
          $_.FullName -notmatch '[\\/](onecore|spectre)[\\/]'
        } |
        Sort-Object FullName -Descending
    )
    if ($runtimeMatches.Count -gt 0) {
      $vcRuntimeDirectories = @($runtimeMatches[0].Directory.FullName)
    }
  }

  if ($vcRuntimeDirectories.Count -gt 0) {
    Write-Host "Resolved desktop Visual C++ runtime directory from '$redistRoot': $($vcRuntimeDirectories[0])"
    break
  }
}

if ($vcRuntimeDirectories.Count -gt 0) {
  $searchDirectories += $vcRuntimeDirectories
} else {
  Write-Warning "Could not locate the desktop x64 VCRUNTIME140.dll in Visual Studio redistributable roots. Native dependency resolution will fail if FFMS2 imports the dynamic MSVC CRT."
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

$entryDlls = @(
  (Join-Path $NativeRoot 'ffms2\bin\ffms2.dll'),
  (Join-Path $NativeRoot 'mangetsu\bin\mangetsu.dll'),
  (Join-Path $NativeRoot 'automation\bin\yoake-automation.dll'),
  (Join-Path $NativeRoot 'automation\bin\yoake-automation-modern.dll'),
  (Join-Path $NativeRoot 'motion\bin\yoake-motion.dll')
)
foreach ($entryDll in $entryDlls) {
  if (-not (Test-Path -LiteralPath $entryDll)) { throw "Missing native runtime entry DLL: $entryDll" }
}
$queue = [System.Collections.Generic.Queue[string]]::new()
foreach ($entryDll in $entryDlls) { $queue.Enqueue($entryDll) }
$selected = @{}

while ($queue.Count -gt 0) {
  $binary = $queue.Dequeue()
  $name = [IO.Path]::GetFileName($binary)
  $key = $name.ToLowerInvariant()
  if ($selected.ContainsKey($key)) { continue }
  $selected[$key] = $binary

  foreach ($dependency in (Get-ImportedDllNames $binary)) {
    $dependencyKey = $dependency.ToLowerInvariant()
    if ($available.ContainsKey($dependencyKey)) {
      if (-not $selected.ContainsKey($dependencyKey)) {
        $queue.Enqueue($available[$dependencyKey])
      }
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
  entrypoints = @('ffms2.dll', 'mangetsu.dll', 'yoake-automation.dll', 'yoake-automation-modern.dll', 'yoake-motion.dll')
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
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\licenses\Aegisub-BSD-3-Clause.txt') -Destination $licenseTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\licenses\Aegisub-Karaoke-Matcher-ISC.txt') -Destination $licenseTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\licenses\DependencyControl-MIT.txt') -Destination $licenseTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $portable 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $portable 'THIRD_PARTY_NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'automation') -Destination $portable -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'automation-3.5-above') -Destination $portable -Recurse -Force

# Keep the exact published symbols for crash diagnosis, but never ship
# them in the user-facing ZIP. Preserve their relative paths for lookup.
$symbolsRoot = Join-Path $OutputRoot 'Yoake-Windows-x64-symbols'
if (Test-Path -LiteralPath $symbolsRoot) { Remove-Item -LiteralPath $symbolsRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $symbolsRoot | Out-Null
$symbolFiles = @(Get-ChildItem -LiteralPath $portable -Recurse -Force -File -Filter '*.pdb')
$symbolBytes = [long]0
foreach ($file in $symbolFiles) {
  $relative = [IO.Path]::GetRelativePath($portable, $file.FullName)
  $target = Join-Path $symbolsRoot $relative
  New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
  $symbolBytes += $file.Length
  Move-Item -LiteralPath $file.FullName -Destination $target -Force
}
Write-Host ("Separated {0} PDB files ({1:N2} MiB) from portable runtime." -f $symbolFiles.Count, ($symbolBytes / 1MB))

$zip = Join-Path $OutputRoot 'Yoake-Windows-x64-portable.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $portable -DestinationPath $zip -CompressionLevel Optimal
# Run subsequent loader and GUI acceptance against bytes extracted from the
# exact distributable ZIP, rather than only its pre-compression staging tree.
Remove-Item -LiteralPath $portable -Recurse -Force
Expand-Archive -LiteralPath $zip -DestinationPath $OutputRoot
# Ensure the ZIP has exactly one top-level application directory.
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
  if (@($archive.Entries | Where-Object { $_.FullName -notmatch '^Yoake[\\/]' }).Count -gt 0) { throw 'Portable archive contains files outside Yoake/.' }
  if (@($archive.Entries | Where-Object { $_.FullName -match '\.pdb$' }).Count -gt 0) { throw 'Portable archive contains debug symbols.' }
  $entries = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object {
    [ordered]@{ path = $_.FullName; bytes = $_.Length; compressed_bytes = $_.CompressedLength }
  } | Sort-Object { $_.compressed_bytes } -Descending)
  $report = [ordered]@{
    portable_zip_bytes = (Get-Item -LiteralPath $zip).Length
    portable_extracted_bytes = [long](($entries | ForEach-Object { $_.bytes } | Measure-Object -Sum).Sum)
    excluded_symbol_count = $symbolFiles.Count
    excluded_symbol_bytes = $symbolBytes
    files = $entries
  }
  $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputRoot 'Yoake-Windows-x64-package-size.json') -Encoding utf8
  Write-Host ("Portable ZIP: {0:N2} MiB; extracted: {1:N2} MiB" -f ($report.portable_zip_bytes / 1MB), ($report.portable_extracted_bytes / 1MB))
  Write-Host 'Largest compressed entries:'
  $entries | Select-Object -First 15 | ForEach-Object { Write-Host ("  {0,8:N2} MiB  {1}" -f ($_.compressed_bytes / 1MB), $_.path) }
  if ($env:GITHUB_STEP_SUMMARY) {
    @(
      '### Yoake Windows package size'
      ''
      ("- Portable ZIP: {0:N2} MiB" -f ($report.portable_zip_bytes / 1MB))
      ("- Extracted runtime: {0:N2} MiB" -f ($report.portable_extracted_bytes / 1MB))
      ("- Separate debug symbols: {0:N2} MiB ({1} files)" -f ($symbolBytes / 1MB), $symbolFiles.Count)
      ''
      '| File | Compressed (MiB) | Extracted (MiB) |'
      '|---|---:|---:|'
      $($entries | Select-Object -First 15 | ForEach-Object { "| $($_.path) | {0:N2} | {1:N2} |" -f ($_.compressed_bytes / 1MB), ($_.bytes / 1MB) })
    ) | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
  }
} finally { $archive.Dispose() }
if (-not (Test-Path -LiteralPath (Join-Path $portable 'Yoake.exe'))) { throw 'Portable archive is missing Yoake.exe.' }
Write-Host 'Portable archive extracted for native closure, hash and GUI acceptance.'
Write-Host "Staged $($selected.Count) native runtime DLL(s) in native/win-x64."
Write-Host "Created $zip"
