param(
    [Parameter(Mandatory = $true)] [string]$PortableRoot
)

$ErrorActionPreference = 'Stop'
$PortableRoot = [IO.Path]::GetFullPath($PortableRoot)
$expected = @(
    'yoake.exe',
    'ffms2.dll',
    'mangetsu.dll',
    'avcodec-61.dll',
    'avformat-61.dll',
    'avutil-59.dll',
    'swresample-5.dll',
    'swscale-8.dll'
)
foreach ($name in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $PortableRoot $name))) {
        throw "Portable package is missing required runtime: $name"
    }
}
$zlibRuntimes = @(Get-ChildItem -LiteralPath $PortableRoot -File | Where-Object {
    $_.Name -match '^(?:z|zlib1?)\.dll$'
})
if ($zlibRuntimes.Count -ne 1) {
    throw "Portable package must contain exactly one pinned zlib runtime: $($zlibRuntimes.Name -join ', ')"
}
$zlibRuntime = $zlibRuntimes[0].Name

$ffmpegDlls = Get-ChildItem -LiteralPath $PortableRoot -File | Where-Object {
    $_.Name -match '^(avcodec|avformat|avutil|swresample|swscale)-.*\.dll$'
} | ForEach-Object { $_.Name.ToLowerInvariant() } | Sort-Object
$expectedFfmpeg = @(
    'avcodec-61.dll',
    'avformat-61.dll',
    'avutil-59.dll',
    'swresample-5.dll',
    'swscale-8.dll'
) | Sort-Object
if (Compare-Object $expectedFfmpeg $ffmpegDlls) {
    throw "Portable package contains an unexpected or incomplete FFmpeg ABI set: $($ffmpegDlls -join ', ')"
}

$qtFfmpegPlugins = Get-ChildItem -LiteralPath $PortableRoot -Recurse -File | Where-Object {
    $_.Name -match 'ffmpeg.*plugin.*\.dll$'
}
if ($qtFfmpegPlugins) {
    throw "Qt's FFmpeg decoder plugin must not be packaged: $($qtFfmpegPlugins.FullName -join ', ')"
}

$ffmsDependencies = (dumpbin /dependents (Join-Path $PortableRoot 'ffms2.dll')) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw 'dumpbin failed for ffms2.dll'
}
foreach ($runtime in $expectedFfmpeg) {
    if ($ffmsDependencies -notmatch [regex]::Escape($runtime)) {
        throw "ffms2.dll is not linked to expected runtime $runtime"
    }
}
if ($ffmsDependencies -notmatch [regex]::Escape($zlibRuntime)) {
    throw "ffms2.dll is not linked to the staged pinned zlib runtime $zlibRuntime"
}

$yoakeDependencies = (dumpbin /dependents (Join-Path $PortableRoot 'yoake.exe')) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw 'dumpbin failed for yoake.exe'
}
foreach ($runtime in @('swresample-5.dll', 'avutil-59.dll')) {
    if ($yoakeDependencies -notmatch [regex]::Escape($runtime)) {
        throw "yoake.exe is not linked to the pinned audio converter runtime $runtime"
    }
}

$ffmsExports = (dumpbin /exports (Join-Path $PortableRoot 'ffms2.dll')) -join "`n"
if ($ffmsExports -notmatch 'FFMS_GetFrame' -or $ffmsExports -notmatch 'FFMS_GetAudio') {
    throw 'ffms2.dll is missing required indexed media APIs'
}
$mangetsuExports = (dumpbin /exports (Join-Path $PortableRoot 'mangetsu.dll')) -join "`n"
foreach ($symbol in @('ass_render_frame_auto', 'ass_free_images_rgba', 'ass_read_memory')) {
    if ($mangetsuExports -notmatch $symbol) {
        throw "mangetsu.dll is missing required renderer symbol $symbol"
    }
}
$mangetsuDependencies = (dumpbin /dependents (Join-Path $PortableRoot 'mangetsu.dll')) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw 'dumpbin failed for mangetsu.dll'
}
foreach ($unexpected in @('png', 'freetype', 'harfbuzz', 'fribidi', 'zlib')) {
    if ($mangetsuDependencies -match "(?im)^\s*[^\s]*$unexpected[^\s]*\.dll\s*$") {
        throw "mangetsu.dll unexpectedly requires a separately deployed $unexpected runtime"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $PortableRoot 'THIRD-PARTY-LICENSES\FFmpeg-COPYRIGHT.txt'))) {
    throw 'Portable package is missing third-party license material'
}

Push-Location $PortableRoot
try {
    foreach ($mode in @('--smoke-test', '--ui-smoke-test')) {
        $startupLog = Join-Path $PortableRoot 'yoake-startup-smoke.log'
        $env:YOAKE_STARTUP_LOG = $startupLog
        $process = Start-Process -FilePath (Join-Path $PortableRoot 'yoake.exe') `
            -ArgumentList $mode -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit(15000)) {
            Stop-Process -Id $process.Id -Force
            throw "Packaged Yoake $mode timed out"
        }
        if ($process.ExitCode -ne 0) {
            if (Test-Path -LiteralPath $startupLog) {
                Write-Host '--- Yoake startup diagnostic ---'
                Get-Content -LiteralPath $startupLog
            }
            throw "Packaged Yoake $mode failed with exit code $($process.ExitCode)"
        }
        Remove-Item -LiteralPath $startupLog -Force -ErrorAction SilentlyContinue
    }
} finally {
    Remove-Item Env:YOAKE_STARTUP_LOG -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host 'Portable runtime, FFMS2/converter ABI, renderer-export, native startup, and visible QML window checks passed.'
