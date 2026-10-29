param([Parameter(Mandatory = $true)][string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$work = Join-Path $env:RUNNER_TEMP 'yoake-motion-build'
$vcpkg = Join-Path $work 'vcpkg'
$versions = Get-Content -LiteralPath (Join-Path $repo 'third_party\versions.json') -Raw | ConvertFrom-Json
$triplet = $versions.motionOpenCv.triplet
$prefix = Join-Path $vcpkg "installed\$triplet"
$destination = Join-Path $OutputRoot 'motion'
New-Item -ItemType Directory -Force $work, $destination | Out-Null
$revision = $versions.motionOpenCv.vcpkgCommit
if ($revision -notmatch '^[a-f0-9]{40}$') { throw 'Invalid OpenCV registry revision.' }
git clone --no-checkout https://github.com/microsoft/vcpkg.git $vcpkg
if ($LASTEXITCODE -ne 0) { throw 'Could not clone pinned vcpkg.' }
git -C $vcpkg checkout --detach $revision
if ($LASTEXITCODE -ne 0) { throw 'Could not select pinned vcpkg revision.' }
& (Join-Path $vcpkg 'bootstrap-vcpkg.bat') -disableMetrics
if ($LASTEXITCODE -ne 0) { throw 'Could not bootstrap vcpkg.' }
$env:VCPKG_DISABLE_METRICS = '1'
& (Join-Path $vcpkg 'vcpkg.exe') install "$($versions.motionOpenCv.port):$triplet"
if ($LASTEXITCODE -ne 0) { throw 'Could not build OpenCV.' }
$build = Join-Path $work 'bridge'
cmake -S (Join-Path $repo 'third_party\motion_tracking') -B $build -A x64 "-DCMAKE_TOOLCHAIN_FILE=$(Join-Path $vcpkg 'scripts\buildsystems\vcpkg.cmake')" "-DVCPKG_TARGET_TRIPLET=$triplet" "-DOpenCV_DIR=$prefix\share\opencv4"
if ($LASTEXITCODE -ne 0) { throw 'Could not configure motion provider.' }
cmake --build $build --config Release
if ($LASTEXITCODE -ne 0) { throw 'Could not build motion provider.' }
& (Join-Path $build 'Release\yoake-motion-test.exe')
if ($LASTEXITCODE -ne 0) { throw 'OpenCV motion provider synthetic-frame test failed.' }
New-Item -ItemType Directory -Force (Join-Path $destination 'bin'), (Join-Path $destination 'licenses') | Out-Null
Copy-Item (Join-Path $build 'Release\yoake-motion.dll') (Join-Path $destination 'bin')
Get-ChildItem (Join-Path $prefix 'share') -Recurse -File -Filter copyright | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $destination 'licenses' ("motion-" + $_.Directory.Name + '-copyright.txt')) -Force
}
New-Item -ItemType File -Force (Join-Path $destination '.complete') | Out-Null
