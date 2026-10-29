param([Parameter(Mandatory = $true)][string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pin = Get-Content -LiteralPath (Join-Path $repoRoot 'third_party/assdraw.json') -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputRoot)
# A fresh, owned build directory; no deletion or reuse of unrelated checkout state.
$work = Join-Path $env:RUNNER_TEMP ('yoake-assdraw-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
New-Item -ItemType Directory -Force -Path $output | Out-Null
function Checked([string]$Name) { if ($LASTEXITCODE -ne 0) { throw "$Name failed: $LASTEXITCODE" } }
function Checkout([string]$Url, [string]$Commit, [string]$Destination) {
  if ($Commit -notmatch '^[a-f0-9]{40}$') { throw 'ASSDraw sources require immutable full commit IDs.' }
  git clone --no-checkout $Url $Destination
  Checked 'Clone pinned source'
  git -C $Destination checkout --detach $Commit
  Checked 'Checkout pinned source'
  $actual = (& git -C $Destination rev-parse HEAD).Trim()
  Checked 'Read source revision'
  if ($actual -cne $Commit) { throw "Source identity mismatch: $actual" }
}
$source = Join-Path $work 'assdraw'
$agg = Join-Path $work 'agg'
Checkout $pin.repository $pin.commit $source
Checkout $pin.agg.repository $pin.agg.commit $agg
$wxArchive = Join-Path $work 'wxWidgets-3.2.11.zip'
Invoke-WebRequest -Uri $pin.wxWidgets.url -OutFile $wxArchive
if ((Get-FileHash -LiteralPath $wxArchive -Algorithm SHA1).Hash.ToLowerInvariant() -cne $pin.wxWidgets.sha1) { throw 'Pinned official wxWidgets source hash mismatch.' }
$wxRoot = Join-Path $work 'wx'
Expand-Archive -LiteralPath $wxArchive -DestinationPath $wxRoot
$wx = if (Test-Path -LiteralPath (Join-Path $wxRoot 'CMakeLists.txt')) { $wxRoot } else { (Get-ChildItem -LiteralPath $wxRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'CMakeLists.txt') } | Select-Object -First 1).FullName }
if (-not $wx) { throw 'Official wxWidgets archive has no buildable source root.' }
$build = Join-Path $work 'build'
# Use the fork's supported, unchanged CMake build. Local supplied source overrides
# retain its exact dependency revisions and allow an offline corresponding-source rebuild.
cmake -S $source -B $build -G 'Visual Studio 17 2022' -A x64 -DBUILD_TESTING=ON "-DFETCHCONTENT_SOURCE_DIR_WXWIDGETS=$wx" "-DFETCHCONTENT_SOURCE_DIR_AGG=$agg"
Checked 'Configure ASSDraw'
cmake --build $build --config Release --parallel 2
Checked 'Build ASSDraw'
ctest --test-dir $build -C Release --output-on-failure
Checked 'ASSDraw tests'
$exe = Join-Path $build 'Release/ASSDraw3.exe'
& (Join-Path $source 'ci/verify-windows-binary.ps1') -Executable $exe
& (Join-Path $source 'ci/smoke-test-windows.ps1') -Executable $exe
Copy-Item -LiteralPath $exe -Destination (Join-Path $output 'ASSDraw3.exe')
$licenses = Join-Path $output 'licenses'
$sources = Join-Path $output 'sources'
New-Item -ItemType Directory -Path $licenses,$sources | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $licenses 'ASSDraw-BSD.txt')
Copy-Item -LiteralPath (Join-Path $source 'NOTICE.md') -Destination (Join-Path $licenses 'ASSDraw-NOTICE.md')
Copy-Item -LiteralPath (Join-Path $agg 'agg-src/copying') -Destination (Join-Path $licenses 'AGG-COPYING.txt')
foreach ($name in @('gpl.txt','lgpl.txt','licence.txt','licendoc.txt')) {
  Copy-Item -LiteralPath (Join-Path $wx "docs/$name") -Destination (Join-Path $licenses "wxWidgets-$name")
}
Copy-Item -LiteralPath (Join-Path $wx 'docs/gpl.txt') -Destination (Join-Path $licenses 'ASSDraw-GPL-2.0.txt')
# Retain the notices of wx's statically bundled libraries as individual files,
# preserving relative paths to avoid collisions between their LICENSE/COPYING names.
Get-ChildItem -LiteralPath (Join-Path $wx 'src') -Recurse -File | Where-Object { $_.Name -match '^(?i)(copying|copyright|licen[cs]e)([._-].*)?$' } | ForEach-Object {
  $relative = [IO.Path]::GetRelativePath($wx,$_.FullName)
  $destination = Join-Path $licenses (Join-Path 'wxWidgets-third-party' $relative)
  New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
  Copy-Item -LiteralPath $_.FullName -Destination $destination
}
# Include every upstream source/header/build script and license, not merely links
# or a time-limited artifact. wx's original archive also carries its bundled libraries.
$assdrawArchive = Join-Path $sources 'assdraw-source.zip'
git -C $source archive --format=zip "--output=$assdrawArchive" $pin.commit
Checked 'Archive corresponding ASSDraw source'
$aggArchive = Join-Path $sources 'agg-source.zip'
git -C $agg archive --format=zip "--output=$aggArchive" $pin.agg.commit
Checked 'Archive corresponding AGG source'
Copy-Item -LiteralPath $wxArchive -Destination $sources
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party/assdraw.json') -Destination $sources
Copy-Item -LiteralPath $PSCommandPath -Destination $sources
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party/assdraw-source-README.md') -Destination (Join-Path $sources 'README.md')
$compiler = (Get-Item (Get-Command cl.exe -ErrorAction Stop).Source).VersionInfo.FileVersion
$manifest = [ordered]@{
  repository = $pin.repository; commit = $pin.commit; wxWidgets = $pin.wxWidgets; agg = $pin.agg
  architecture = 'x64'; compiler = $compiler; configuration = 'Release /MT'; modifiedSource = $false
  executable = 'ASSDraw3.exe'; sha256 = (Get-FileHash -LiteralPath (Join-Path $output 'ASSDraw3.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
  sources = @(Get-ChildItem -LiteralPath $sources -File | Sort-Object Name | ForEach-Object { [ordered]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'assdraw-manifest.json') -Encoding utf8
Write-Host "Verified bundled ASSDraw fork $($pin.commit), SHA256 $($manifest.sha256)."
