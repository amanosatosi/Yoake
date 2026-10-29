param([Parameter(Mandatory = $true)][string]$PortableRoot, [Parameter(Mandatory = $true)][string]$AssDrawRoot)
$ErrorActionPreference = 'Stop'
$portable = (Resolve-Path -LiteralPath $PortableRoot).Path
$sourceRoot = (Resolve-Path -LiteralPath (Join-Path $AssDrawRoot 'sources')).Path
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../third_party/assdraw.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $portable 'assdraw-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.commit -cne $pin.commit -or $manifest.repository -cne $pin.repository -or $manifest.agg.commit -cne $pin.agg.commit -or $manifest.wxWidgets.sha1 -cne $pin.wxWidgets.sha1 -or $manifest.modifiedSource -ne $false) { throw 'Bundled ASSDraw provenance is not the pinned unmodified fork.' }
$exe = Join-Path $portable 'ASSDraw3.exe'
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.sha256) { throw 'Packaged ASSDraw executable hash mismatch.' }
if (-not (Test-Path -LiteralPath (Join-Path $portable 'ASSDraw-source-availability.txt'))) { throw 'Missing corresponding-source availability notice.' }
if (Test-Path -LiteralPath (Join-Path $portable 'sources/assdraw')) { throw 'Corresponding ASSDraw source must be in the separate artifact, not the portable package.' }
foreach ($name in @('assdraw-source.zip','agg-source.zip','wxWidgets-3.2.11.zip','assdraw.json','build_assdraw.ps1','README.md')) {
  if (-not ($manifest.sources.name -ccontains $name)) { throw "Missing corresponding-source manifest entry: $name" }
}
foreach ($source in $manifest.sources) {
  if ([IO.Path]::GetFileName($source.name) -cne $source.name) { throw 'Invalid source manifest filename.' }
  $path = Join-Path $sourceRoot $source.name
  if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $source.sha256) { throw "Corresponding source hash mismatch: $($source.name)" }
}
foreach ($name in @('ASSDraw-BSD.txt','ASSDraw-NOTICE.md','ASSDraw-GPL-2.0.txt','AGG-COPYING.txt','wxWidgets-licence.txt','wxWidgets-lgpl.txt')) {
  if (-not (Test-Path -LiteralPath (Join-Path $portable "licenses/assdraw/$name"))) { throw "Missing ASSDraw distribution license: $name" }
}
# Invoke the exact fork's own audited verifier and standalone smoke test from its
# separately published matching source. These verify the real packaged bytes.
$auditRoot = Join-Path $env:RUNNER_TEMP ('yoake-assdraw-package-audit-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath (Join-Path $sourceRoot 'assdraw-source.zip') -DestinationPath $auditRoot
& (Join-Path $auditRoot 'ci/verify-windows-binary.ps1') -Executable $exe
& (Join-Path $auditRoot 'ci/smoke-test-windows.ps1') -Executable $exe
Write-Host "PASS: packaged fork revision $($manifest.commit), executable SHA256 $($manifest.sha256), corresponding source hashes/licenses, static dependency/resource audit and real standalone native window."
