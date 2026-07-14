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
    'swscale-8.dll',
    'zlib1.dll'
)
foreach ($name in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $PortableRoot $name))) {
        throw "Portable package is missing required runtime: $name"
    }
}

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

if (-not (Test-Path -LiteralPath (Join-Path $PortableRoot 'THIRD-PARTY-LICENSES\FFmpeg-COPYRIGHT.txt'))) {
    throw 'Portable package is missing third-party license material'
}

Push-Location $PortableRoot
try {
    $process = Start-Process -FilePath (Join-Path $PortableRoot 'yoake.exe') `
        -ArgumentList '--smoke-test' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Packaged Yoake startup smoke test failed with exit code $($process.ExitCode)"
    }
} finally {
    Pop-Location
}

Write-Host 'Portable runtime, ABI, renderer-export, and startup checks passed.'
