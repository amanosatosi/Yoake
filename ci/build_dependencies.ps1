param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [ValidateSet('FFmpeg', 'FFMS2', 'Mangetsu', 'Finalize', 'All')]
    [string]$Stage = 'All'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$versions = Get-Content -LiteralPath (Join-Path $repoRoot 'third_party\versions.json') -Raw | ConvertFrom-Json
$workRoot = Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) 'yoake-native-build'
$binaryCache = Join-Path (Split-Path -Parent $OutputRoot) 'vcpkg-binaries'

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

$vcpkgRoot = Join-Path $workRoot 'vcpkg'
$ffmpegInstall = Join-Path $OutputRoot 'ffmpeg'
$ffmpegManifest = Join-Path $repoRoot 'third_party\ffmpeg'
$ffmpegPrefix = Join-Path $ffmpegInstall 'x64-windows'

if ($Stage -in @('FFmpeg', 'All')) {
    Write-Host "Building pinned FFmpeg $($versions.ffmpeg.version) through vcpkg $($versions.vcpkg.commit)"
    Checkout-Pinned 'https://github.com/microsoft/vcpkg.git' $versions.vcpkg.commit $vcpkgRoot $true
    & (Join-Path $vcpkgRoot 'bootstrap-vcpkg.bat') -disableMetrics
    Assert-LastExitCode 'vcpkg bootstrap'
    $env:VCPKG_DISABLE_METRICS = '1'
    Remove-Item Env:VCPKG_ROOT -ErrorAction SilentlyContinue
    $env:VCPKG_BINARY_SOURCES = "clear;files,$binaryCache,readwrite"
    & (Join-Path $vcpkgRoot 'vcpkg.exe') install `
        "--x-manifest-root=$ffmpegManifest" `
        "--x-install-root=$ffmpegInstall" `
        --triplet=x64-windows `
        --clean-after-build
    Assert-LastExitCode 'pinned FFmpeg vcpkg install'

    foreach ($required in @(
        'include\libavcodec\avcodec.h',
        'bin\avcodec-61.dll',
        'bin\avformat-61.dll',
        'bin\avutil-59.dll',
        'bin\swresample-5.dll',
        'bin\swscale-8.dll'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $ffmpegPrefix $required))) {
            throw "Pinned FFmpeg stage is missing $required"
        }
    }
    $componentLicenses = Join-Path $ffmpegInstall 'licenses'
    New-Item -ItemType Directory -Force -Path $componentLicenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $ffmpegPrefix 'share\ffmpeg\copyright') `
        -Destination (Join-Path $componentLicenses 'FFmpeg-COPYRIGHT.txt')
    Copy-Item -LiteralPath (Join-Path $ffmpegPrefix 'share\zlib\copyright') `
        -Destination (Join-Path $componentLicenses 'zlib-COPYRIGHT.txt')
    New-Item -ItemType File -Force -Path (Join-Path $ffmpegInstall '.complete') | Out-Null
    Write-Host "Pinned FFmpeg cache stage installed at $ffmpegInstall"
}

$ffmsSource = Join-Path $workRoot 'ffms2-source'
$ffmsBuild = Join-Path $workRoot 'ffms2-build'
$ffmsInstall = Join-Path $OutputRoot 'ffms2'

if ($Stage -in @('FFMS2', 'All')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ffmpegInstall '.complete'))) {
        throw 'The pinned FFmpeg cache stage must be restored before building FFMS2'
    }
    Write-Host "Building pinned FFMS2 $($versions.ffms2.version)"
    Checkout-Pinned 'https://github.com/FFMS/ffms2.git' $versions.ffms2.commit $ffmsSource
    cmake -S (Join-Path $repoRoot 'third_party\ffms2') -B $ffmsBuild -G Ninja `
        -DCMAKE_BUILD_TYPE=Release `
        "-DYOAKE_FFMS2_SOURCE_ROOT:PATH=$ffmsSource" `
        "-DFFMPEG_ROOT:PATH=$ffmpegPrefix" `
        "-DCMAKE_INSTALL_PREFIX:PATH=$ffmsInstall"
    Assert-LastExitCode 'FFMS2 configure'
    cmake --build $ffmsBuild --parallel 2
    Assert-LastExitCode 'FFMS2 build'
    cmake --install $ffmsBuild
    Assert-LastExitCode 'FFMS2 install'
    if (-not (Test-Path -LiteralPath (Join-Path $ffmsInstall 'bin\ffms2.dll'))) {
        throw 'FFMS2 stage did not install bin\ffms2.dll'
    }
    $componentLicenses = Join-Path $ffmsInstall 'licenses'
    New-Item -ItemType Directory -Force -Path $componentLicenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $ffmsSource 'COPYING') `
        -Destination (Join-Path $componentLicenses 'FFMS2-COPYING.txt')
    New-Item -ItemType File -Force -Path (Join-Path $ffmsInstall '.complete') | Out-Null
    Write-Host "Pinned FFMS2 cache stage installed at $ffmsInstall"
}

$mangetsuSource = Join-Path $workRoot 'mangetsu-source'
$mangetsuBuild = Join-Path $workRoot 'mangetsu-build'
$mangetsuInstall = Join-Path $OutputRoot 'mangetsu'

if ($Stage -in @('Mangetsu', 'All')) {
    Write-Host "Building pinned Mangetsu $($versions.mangetsu.commit)"
    Checkout-Pinned 'https://github.com/amanosatosi/libassmod.git' $versions.mangetsu.commit $mangetsuSource
    $mangetsuSubprojects = Join-Path $mangetsuSource 'subprojects'
    New-Item -ItemType Directory -Force -Path $mangetsuSubprojects | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\freetype2.wrap') -Destination $mangetsuSubprojects
    Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\fribidi.wrap') -Destination $mangetsuSubprojects
    Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\harfbuzz.wrap') -Destination $mangetsuSubprojects
    Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\subprojects\zlib.wrap') -Destination $mangetsuSubprojects

    $mangetsuOptions = Get-Content -LiteralPath (Join-Path $repoRoot 'third_party\mangetsu\meson-options.txt') |
        Where-Object { $_ -and -not $_.StartsWith('#') }
    python -m mesonbuild.mesonmain setup $mangetsuBuild $mangetsuSource `
        --backend=ninja `
        --buildtype=release `
        "--prefix=$mangetsuInstall" `
        --libdir=bin `
        --force-fallback-for=freetype2,fribidi,harfbuzz `
        @mangetsuOptions
    Assert-LastExitCode 'Mangetsu configure'
    python -m mesonbuild.mesonmain compile -C $mangetsuBuild
    Assert-LastExitCode 'Mangetsu build'
    python -m mesonbuild.mesonmain install -C $mangetsuBuild
    Assert-LastExitCode 'Mangetsu install'

    $mangetsuBin = Join-Path $mangetsuInstall 'bin'
    $builtRenderer = Get-ChildItem -LiteralPath $mangetsuBin -File | Where-Object {
        $_.Name -match '^(?:lib)?ass(?:-[0-9]+)?\.dll$'
    } | Select-Object -First 1
    if (-not $builtRenderer) {
        throw "Mangetsu build did not install an ass DLL under $mangetsuBin"
    }
    Copy-Item -LiteralPath $builtRenderer.FullName -Destination (Join-Path $mangetsuBin 'mangetsu.dll') -Force

    $componentLicenses = Join-Path $mangetsuInstall 'licenses'
    New-Item -ItemType Directory -Force -Path $componentLicenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $mangetsuSource 'COPYING') `
        -Destination (Join-Path $componentLicenses 'Mangetsu-COPYING.txt')
    Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\freetype2\LICENSE.TXT') `
        -Destination (Join-Path $componentLicenses 'FreeType-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\fribidi\COPYING') `
        -Destination (Join-Path $componentLicenses 'FriBidi-COPYING.txt')
    Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\harfbuzz\COPYING') `
        -Destination (Join-Path $componentLicenses 'HarfBuzz-COPYING.txt')
    Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\libpng-1.6.43\LICENSE') `
        -Destination (Join-Path $componentLicenses 'libpng-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $mangetsuSource 'subprojects\zlib-1.2.11\README') `
        -Destination (Join-Path $componentLicenses 'zlib-1.2.11-README.txt')
    New-Item -ItemType File -Force -Path (Join-Path $mangetsuInstall '.complete') | Out-Null
    Write-Host "Pinned Mangetsu cache stage installed at $mangetsuInstall"
}

if ($Stage -in @('Finalize', 'All')) {
    foreach ($component in @('ffmpeg', 'ffms2', 'mangetsu')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputRoot "$component\.complete"))) {
            throw "Pinned $component cache stage is incomplete"
        }
    }
    $licenseRoot = Join-Path $OutputRoot 'licenses'
    if (Test-Path -LiteralPath $licenseRoot) {
        Remove-Item -LiteralPath $licenseRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
    foreach ($component in @($ffmpegInstall, $ffmsInstall, $mangetsuInstall)) {
        Get-ChildItem -LiteralPath (Join-Path $component 'licenses') -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $licenseRoot
        }
    }
    Invoke-WebRequest -UseBasicParsing `
        -Uri 'https://raw.githubusercontent.com/qt/qtbase/v6.8.3/LICENSES/LGPL-3.0-only.txt' `
        -OutFile (Join-Path $licenseRoot 'Qt-LGPL-3.0-only.txt')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\versions.json') `
        -Destination (Join-Path $OutputRoot 'versions.json')
    New-Item -ItemType File -Force -Path (Join-Path $OutputRoot '.complete') | Out-Null

    Write-Host "Pinned native dependency stack finalized at $OutputRoot"
}
