param(
    [Parameter(Mandatory = $true)] [string]$BuildRoot,
    [Parameter(Mandatory = $true)] [string]$NativeRoot,
    [Parameter(Mandatory = $true)] [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$NativeRoot = [IO.Path]::GetFullPath($NativeRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$stage = Join-Path $OutputRoot 'Yoake-Windows-x64-portable'
$zip = Join-Path $OutputRoot 'Yoake-Windows-x64-portable.zip'

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $BuildRoot 'yoake.exe') -Destination (Join-Path $stage 'yoake.exe')
windeployqt --release --qmldir (Join-Path $repoRoot 'qml') (Join-Path $stage 'yoake.exe')
if ($LASTEXITCODE -ne 0) {
    throw "windeployqt failed with exit code $LASTEXITCODE"
}

# QAudioSink is the only Qt Multimedia API Yoake uses. Its FFmpeg decoder
# plugin is intentionally excluded to prevent a second FFmpeg ABI stack.
Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object {
    $_.Name -match 'ffmpeg.*plugin.*\.dll$'
} | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
}

Copy-Item -LiteralPath (Join-Path $NativeRoot 'ffms2\bin\ffms2.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $NativeRoot 'mangetsu\bin\mangetsu.dll') -Destination $stage
$ffmpegBin = Join-Path $NativeRoot 'ffmpeg\x64-windows\bin'
foreach ($runtime in @(
    'avcodec-61.dll',
    'avformat-61.dll',
    'avutil-59.dll',
    'swresample-5.dll',
    'swscale-8.dll',
    'zlib1.dll'
)) {
    Copy-Item -LiteralPath (Join-Path $ffmpegBin $runtime) -Destination $stage
}

$licenses = Join-Path $stage 'THIRD-PARTY-LICENSES'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Get-ChildItem -LiteralPath (Join-Path $NativeRoot 'licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licenses
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\README.md') `
    -Destination (Join-Path $licenses 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party\versions.json') `
    -Destination (Join-Path $licenses 'versions.json')

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Portable package created: $zip"
