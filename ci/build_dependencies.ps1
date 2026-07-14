param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versions = Get-Content -LiteralPath (Join-Path $repoRoot 'third_party\versions.json') -Raw | ConvertFrom-Json
$workRoot = Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) 'yoake-native-build'
$binaryCache = Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) 'yoake-vcpkg-binaries'

function Assert-LastExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE"
    }
}

function Checkout-Pinned([string]$url, [string]$commit, [string]$path, [bool]$versionedRegistry = $false) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    if ($versionedRegistry) {
        # vcpkg's version registry checks historical port trees out through Git.
        # Keep the clone complete so that install never needs a promisor-remote
        # fetch halfway through dependency resolution on a busy CI runner.
        git clone --no-checkout $url $path
        Assert-LastExitCode "complete versioned registry clone for $url"
        git -C $path checkout --detach $commit
        Assert-LastExitCode "git checkout $commit from $url"
    } else {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        git -C $path init --quiet
        Assert-LastExitCode "git init for $url"
        git -C $path remote add origin $url
        Assert-LastExitCode "git remote add for $url"
        git -C $path fetch --depth 1 origin $commit
        Assert-LastExitCode "git fetch $commit from $url"
        git -C $path checkout --detach FETCH_HEAD
        Assert-LastExitCode "git checkout $commit from $url"
    }
    $actual = (git -C $path rev-parse HEAD).Trim()
    Assert-LastExitCode "git rev-parse for $url"
    if ($actual -ne $commit) {
        throw "Pinned checkout mismatch for ${url}: expected $commit, got $actual"
    }
}

New-Item -ItemType Directory -Force -Path $workRoot, $OutputRoot, $binaryCache | Out-Null

Write-Host "Building pinned FFmpeg $($versions.ffmpeg.version) through vcpkg $($versions.vcpkg.commit)"
$vcpkgRoot = Join-Path $workRoot 'vcpkg'
Checkout-Pinned 'https://github.com/microsoft/vcpkg.git' $versions.vcpkg.commit $vcpkgRoot $true
& (Join-Path $vcpkgRoot 'bootstrap-vcpkg.bat') -disableMetrics
Assert-LastExitCode 'vcpkg bootstrap'
$env:VCPKG_DISABLE_METRICS = '1'
Remove-Item Env:VCPKG_ROOT -ErrorAction SilentlyContinue
$env:VCPKG_BINARY_SOURCES = "clear;files,$binaryCache,readwrite"
$ffmpegInstall = Join-Path $OutputRoot 'ffmpeg'
$ffmpegManifest = Join-Path $repoRoot 'third_party\ffmpeg'
& (Join-Path $vcpkgRoot 'vcpkg.exe') install `
    --x-manifest-root=$ffmpegManifest `
    --x-install-root=$ffmpegInstall `
    --triplet=x64-windows `
    --clean-after-build
Assert-LastExitCode 'pinned FFmpeg vcpkg install'
$ffmpegPrefix = Join-Path $ffmpegInstall 'x64-windows'

Write-Host "Building pinned FFMS2 $($versions.ffms2.version)"
$ffmsSource = Join-Path $workRoot 'ffms2-source'
$ffmsBuild = Join-Path $workRoot 'ffms2-build'
$ffmsInstall = Join-Path $OutputRoot 'ffms2'
Checkout-Pinned 'https://github.com/FFMS/ffms2.git' $versions.ffms2.commit $ffmsSource
cmake -S (Join-Path $repoRoot 'third_party\ffms2') -B $ffmsBuild -G Ninja `
    -DCMAKE_BUILD_TYPE=Release `
    -DFFMS2_SOURCE_DIR=$ffmsSource `
    -DFFMPEG_ROOT=$ffmpegPrefix `
    -DCMAKE_INSTALL_PREFIX=$ffmsInstall
Assert-LastExitCode 'FFMS2 configure'
cmake --build $ffmsBuild --parallel 2
Assert-LastExitCode 'FFMS2 build'
cmake --install $ffmsBuild
Assert-LastExitCode 'FFMS2 install'

Write-Host "Building pinned Mangetsu $($versions.mangetsu.commit)"
$mangetsuSource = Join-Path $workRoot 'mangetsu-source'
$mangetsuBuild = Join-Path $workRoot 'mangetsu-build'
$mangetsuInstall = Join-Path $OutputRoot 'mangetsu'
Checkout-Pinned 'https://github.com/amanosatosi/libassmod.git' $versions.mangetsu.commit $mangetsuSource
$mangetsuSubprojects = Join-Path $mangetsuSource 'subprojects'
New-Item -ItemType Directory -Force -Path $mangetsuSubprojects | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\freetype2.wrap') -Destination $mangetsuSubprojects
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\fribidi.wrap') -Destination $mangetsuSubprojects
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\harfbuzz.wrap') -Destination $mangetsuSubprojects
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\zlib.wrap') -Destination $mangetsuSubprojects

python -m mesonbuild.mesonmain setup $mangetsuBuild $mangetsuSource `
    --backend=ninja `
    --buildtype=release `
    --prefix=$mangetsuInstall `
    --libdir=bin `
    --force-fallback-for=freetype2,fribidi,harfbuzz `
    -Ddefault_library=shared `
    -Dfontconfig=disabled `
    -Ddirectwrite=enabled `
    -Dlibunibreak=disabled `
    -Dasm=disabled `
    -Dtest=disabled `
    -Dcompare=disabled `
    -Dprofile=disabled `
    -Dfuzz=disabled `
    -Dcheckasm=disabled `
    -Dfreetype2:default_library=static `
    -Dfreetype2:harfbuzz=disabled `
    -Dfribidi:default_library=static `
    -Dfribidi:tests=false `
    -Dfribidi:docs=false `
    -Dharfbuzz:default_library=static `
    -Dharfbuzz:freetype=disabled `
    -Dharfbuzz:cairo=disabled `
    -Dharfbuzz:glib=disabled `
    -Dharfbuzz:gobject=disabled `
    -Dharfbuzz:tests=disabled `
    -Dharfbuzz:docs=disabled `
    -Dharfbuzz:icu=disabled `
    -Dzlib:default_library=static
Assert-LastExitCode 'Mangetsu configure'
python -m mesonbuild.mesonmain compile -C $mangetsuBuild
Assert-LastExitCode 'Mangetsu build'
python -m mesonbuild.mesonmain install -C $mangetsuBuild
Assert-LastExitCode 'Mangetsu install'

$mangetsuBin = Join-Path $mangetsuInstall 'bin'
$builtRenderer = Get-ChildItem -LiteralPath $mangetsuBin -File | Where-Object {
    $_.Name -in @('ass.dll', 'libass.dll')
} | Select-Object -First 1
if (-not $builtRenderer) {
    throw "Mangetsu build did not install ass.dll/libass.dll under $mangetsuBin"
}
Copy-Item -LiteralPath $builtRenderer.FullName -Destination (Join-Path $mangetsuBin 'mangetsu.dll') -Force

$licenseRoot = Join-Path $OutputRoot 'licenses'
New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $ffmpegPrefix 'share\ffmpeg\copyright') `
    -Destination (Join-Path $licenseRoot 'FFmpeg-COPYRIGHT.txt')
Copy-Item -LiteralPath (Join-Path $ffmsSource 'COPYING') `
    -Destination (Join-Path $licenseRoot 'FFMS2-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $mangetsuSource 'COPYING') `
    -Destination (Join-Path $licenseRoot 'Mangetsu-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\freetype2\LICENSE.TXT') `
    -Destination (Join-Path $licenseRoot 'FreeType-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\fribidi\COPYING') `
    -Destination (Join-Path $licenseRoot 'FriBidi-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\harfbuzz\COPYING') `
    -Destination (Join-Path $licenseRoot 'HarfBuzz-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\zlib-1.2.11\README') `
    -Destination (Join-Path $licenseRoot 'zlib-1.2.11-README.txt')
Copy-Item -LiteralPath (Join-Path $ffmpegPrefix 'share\zlib\copyright') `
    -Destination (Join-Path $licenseRoot 'zlib-COPYRIGHT.txt')
Invoke-WebRequest -UseBasicParsing `
    -Uri 'https://raw.githubusercontent.com/qt/qtbase/v6.8.3/LICENSES/LGPL-3.0-only.txt' `
    -OutFile (Join-Path $licenseRoot 'Qt-LGPL-3.0-only.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\versions.json') `
    -Destination (Join-Path $OutputRoot 'versions.json')
New-Item -ItemType File -Force -Path (Join-Path $OutputRoot '.complete') | Out-Null

Write-Host "Pinned native dependency stack installed at $OutputRoot"
