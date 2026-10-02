param(
  [Parameter(Mandatory = $true)][string]$OutputRoot,
  [ValidateSet('FFmpeg', 'FFMS2', 'Mangetsu', 'Finalize', 'All')][string]$Stage = 'All'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versions = Get-Content -LiteralPath (Join-Path $repoRoot 'third_party\versions.json') -Raw | ConvertFrom-Json
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$workRoot = Join-Path $tempRoot 'yoake-native-build'
$binaryCache = Join-Path (Split-Path -Parent $OutputRoot) 'vcpkg-binaries'

function Assert-LastExitCode([string]$operation) {
  if ($LASTEXITCODE -ne 0) { throw "$operation failed with exit code $LASTEXITCODE" }
}
function Checkout-Pinned([string]$url, [string]$commit, [string]$path, [bool]$versionedRegistry = $false) {
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
  if ($versionedRegistry) {
    git clone --no-checkout $url $path; Assert-LastExitCode "clone $url"
    git -C $path checkout --detach $commit; Assert-LastExitCode "checkout $commit"
  } else {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    git -C $path init --quiet; Assert-LastExitCode "git init $url"
    git -C $path remote add origin $url; Assert-LastExitCode "git remote $url"
    git -C $path fetch --depth 1 origin $commit; Assert-LastExitCode "git fetch $commit"
    git -C $path checkout --detach FETCH_HEAD; Assert-LastExitCode "git checkout $commit"
  }
  $actual = (git -C $path rev-parse HEAD).Trim(); Assert-LastExitCode "git rev-parse $url"
  if ($actual -ne $commit) { throw "Pinned checkout mismatch for ${url}: expected $commit, got $actual" }
}

function Checkout-LatestBranch([string]$url, [string]$branch, [string]$path) {
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
  git clone --depth 1 --single-branch --branch $branch $url $path
  Assert-LastExitCode "clone latest branch $branch from $url"
  $actual = (git -C $path rev-parse HEAD).Trim()
  Assert-LastExitCode "git rev-parse $url"
  Write-Host "Resolved $url branch '$branch' to $actual"
  return $actual
}

New-Item -ItemType Directory -Force -Path $workRoot, $OutputRoot, $binaryCache | Out-Null
$vcpkgRoot = Join-Path $workRoot 'vcpkg'
$ffmpegInstall = Join-Path $OutputRoot 'ffmpeg'
$ffmpegPrefix = Join-Path $ffmpegInstall 'x64-windows'
$ffmsInstall = Join-Path $OutputRoot 'ffms2'
$mangetsuInstall = Join-Path $OutputRoot 'mangetsu'

if ($Stage -in @('FFmpeg', 'All')) {
  Write-Host "Building pinned FFmpeg $($versions.ffmpeg.version) via vcpkg $($versions.vcpkg.commit)"
  Checkout-Pinned 'https://github.com/microsoft/vcpkg.git' $versions.vcpkg.commit $vcpkgRoot $true
  & (Join-Path $vcpkgRoot 'bootstrap-vcpkg.bat') -disableMetrics; Assert-LastExitCode 'vcpkg bootstrap'
  $env:VCPKG_DISABLE_METRICS = '1'
  Remove-Item Env:VCPKG_ROOT -ErrorAction SilentlyContinue
  $env:VCPKG_BINARY_SOURCES = "clear;files,$binaryCache,readwrite"
  & (Join-Path $vcpkgRoot 'vcpkg.exe') install `
    "--x-manifest-root=$(Join-Path $repoRoot 'third_party\ffmpeg')" `
    "--x-install-root=$ffmpegInstall" --triplet=x64-windows --clean-after-build
  Assert-LastExitCode 'pinned FFmpeg vcpkg install'
  foreach ($required in @('include\libavcodec\avcodec.h','bin\avcodec-61.dll','bin\avformat-61.dll','bin\avutil-59.dll','bin\swresample-5.dll','bin\swscale-8.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ffmpegPrefix $required))) { throw "Pinned FFmpeg stage is missing $required" }
  }
  $licenses = Join-Path $ffmpegInstall 'licenses'; New-Item -ItemType Directory -Force -Path $licenses | Out-Null
  Copy-Item (Join-Path $ffmpegPrefix 'share\ffmpeg\copyright') (Join-Path $licenses 'FFmpeg-COPYRIGHT.txt')
  Copy-Item (Join-Path $ffmpegPrefix 'share\zlib\copyright') (Join-Path $licenses 'zlib-COPYRIGHT.txt')
  New-Item -ItemType File -Force -Path (Join-Path $ffmpegInstall '.complete') | Out-Null
}

$ffmsSource = Join-Path $workRoot 'ffms2-source'
$ffmsBuild = Join-Path $workRoot 'ffms2-build'
if ($Stage -in @('FFMS2', 'All')) {
  if (-not (Test-Path -LiteralPath (Join-Path $ffmpegInstall '.complete'))) { throw 'Restore/build FFmpeg before FFMS2.' }
  Checkout-Pinned 'https://github.com/FFMS/ffms2.git' $versions.ffms2.commit $ffmsSource
  cmake -S (Join-Path $repoRoot 'third_party\ffms2') -B $ffmsBuild -G Ninja `
    -DCMAKE_BUILD_TYPE=Release "-DYOAKE_FFMS2_SOURCE_ROOT:PATH=$ffmsSource" `
    "-DFFMPEG_ROOT:PATH=$ffmpegPrefix" "-DCMAKE_INSTALL_PREFIX:PATH=$ffmsInstall"
  Assert-LastExitCode 'FFMS2 configure'
  cmake --build $ffmsBuild --parallel 2; Assert-LastExitCode 'FFMS2 build'
  cmake --install $ffmsBuild; Assert-LastExitCode 'FFMS2 install'
  if (-not (Test-Path -LiteralPath (Join-Path $ffmsInstall 'bin\ffms2.dll'))) { throw 'FFMS2 stage did not install ffms2.dll' }
  $licenses = Join-Path $ffmsInstall 'licenses'; New-Item -ItemType Directory -Force -Path $licenses | Out-Null
  Copy-Item (Join-Path $ffmsSource 'COPYING') (Join-Path $licenses 'FFMS2-COPYING.txt')
  New-Item -ItemType File -Force -Path (Join-Path $ffmsInstall '.complete') | Out-Null
}

$mangetsuSource = Join-Path $workRoot 'mangetsu-source'
$mangetsuBuild = Join-Path $workRoot 'mangetsu-build'
$mangetsuDist = Join-Path $workRoot 'mangetsu-dist'
if ($Stage -in @('Mangetsu', 'All')) {
  Write-Host "Building latest Mangetsu branch '$($versions.mangetsu.branch)' (no cache)"
  Checkout-Pinned 'https://github.com/microsoft/vcpkg.git' $versions.vcpkg.commit $vcpkgRoot $true
  & (Join-Path $vcpkgRoot 'bootstrap-vcpkg.bat') -disableMetrics; Assert-LastExitCode 'vcpkg bootstrap for Mangetsu'
  $env:VCPKG_DISABLE_METRICS = '1'
  Remove-Item Env:VCPKG_ROOT -ErrorAction SilentlyContinue
  # Mangetsu intentionally builds fresh every run. Do not read or write
  # persistent vcpkg binary archives for its static dependency stack.
  $env:VCPKG_BINARY_SOURCES = "clear"

  $triplet = 'x64-windows-static'
  & (Join-Path $vcpkgRoot 'vcpkg.exe') install freetype fribidi harfbuzz pkgconf --triplet $triplet
  Assert-LastExitCode 'Mangetsu static dependency install'

  $resolvedMangetsuCommit = Checkout-LatestBranch 'https://github.com/amanosatosi/libassmod.git' $versions.mangetsu.branch $mangetsuSource
  if (Test-Path -LiteralPath $mangetsuBuild) { Remove-Item -LiteralPath $mangetsuBuild -Recurse -Force }
  if (Test-Path -LiteralPath $mangetsuDist) { Remove-Item -LiteralPath $mangetsuDist -Recurse -Force }

  $prefix = Join-Path $vcpkgRoot "installed\$triplet"
  $pkgconf = Join-Path $prefix 'tools\pkgconf\pkgconf.exe'
  if (-not (Test-Path -LiteralPath $pkgconf)) { throw "pkgconf was not installed at $pkgconf" }
  $env:PKG_CONFIG = $pkgconf
  $env:PKG_CONFIG_PATH = "$(Join-Path $prefix 'lib\pkgconfig');$(Join-Path $prefix 'share\pkgconfig')"
  $env:PKG_CONFIG_LIBDIR = $env:PKG_CONFIG_PATH

  $inc = @(
    "/I$prefix\include",
    "/I$prefix\include\freetype2",
    "/I$prefix\include\fribidi",
    "/I$prefix\include\harfbuzz",
    "/I$prefix\include\libpng16"
  ) -join ' '
  $link = "/LIBPATH:$prefix\lib"

  if (-not (Test-Path -LiteralPath (Join-Path $mangetsuSource 'meson.build'))) {
    throw "Mangetsu source checkout is missing meson.build: $mangetsuSource"
  }

  meson setup $mangetsuBuild $mangetsuSource `
    --prefix="$mangetsuDist" `
    -Ddefault_library=shared `
    -Db_vscrt=mt `
    -Dfontconfig=disabled `
    -Dlibunibreak=disabled `
    -Dasm=disabled `
    -Dtest=disabled `
    -Dcompare=disabled `
    -Dprofile=disabled `
    -Dfuzz=disabled `
    -Dcheckasm=disabled `
    -Dc_args="$inc" `
    -Dc_link_args="$link"
  Assert-LastExitCode 'Mangetsu configure'
  meson compile -C $mangetsuBuild; Assert-LastExitCode 'Mangetsu build'
  meson install -C $mangetsuBuild; Assert-LastExitCode 'Mangetsu install'

  $dll = Get-ChildItem -Path @($mangetsuDist, $mangetsuBuild) -Recurse -Filter '*.dll' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^(ass|libass)([-.].+)?\.dll$' } |
    Select-Object -First 1
  if (-not $dll) { throw 'Mangetsu build did not produce a libass DLL.' }

  $bin = Join-Path $mangetsuInstall 'bin'
  $licenses = Join-Path $mangetsuInstall 'licenses'
  New-Item -ItemType Directory -Force -Path $bin, $licenses | Out-Null
  Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $bin 'mangetsu.dll') -Force
  Copy-Item -LiteralPath (Join-Path $mangetsuSource 'COPYING') -Destination (Join-Path $licenses 'Mangetsu-LICENSE.txt') -Force
  $resolvedMangetsuCommit | Set-Content -LiteralPath (Join-Path $mangetsuInstall 'resolved-commit.txt') -Encoding ASCII
  New-Item -ItemType File -Force -Path (Join-Path $mangetsuInstall '.complete') | Out-Null
}

if ($Stage -in @('Finalize', 'All')) {
  foreach ($component in @('ffmpeg','ffms2','mangetsu')) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputRoot "$component\.complete"))) { throw "Native dependency stage is incomplete: $component" }
  }
  $licenseRoot = Join-Path $OutputRoot 'licenses'
  if (Test-Path $licenseRoot) { Remove-Item $licenseRoot -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
  foreach ($component in @($ffmpegInstall,$ffmsInstall,$mangetsuInstall)) {
    Get-ChildItem (Join-Path $component 'licenses') -File | ForEach-Object { Copy-Item $_.FullName $licenseRoot }
  }
  Copy-Item (Join-Path $repoRoot 'third_party\versions.json') (Join-Path $OutputRoot 'versions.json')
  New-Item -ItemType File -Force -Path (Join-Path $OutputRoot '.complete') | Out-Null
  Write-Host "Native dependency stack finalized at $OutputRoot"
}
