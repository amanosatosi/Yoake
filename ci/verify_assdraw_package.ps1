param([Parameter(Mandatory = $true)][string]$PortableRoot)
$ErrorActionPreference = 'Stop'
$portable = (Resolve-Path -LiteralPath $PortableRoot).Path
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../third_party/assdraw.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $portable 'assdraw-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.commit -cne $pin.commit -or $manifest.repository -cne $pin.repository -or $manifest.agg.commit -cne $pin.agg.commit -or $manifest.wxWidgets.sha1 -cne $pin.wxWidgets.sha1 -or $manifest.modifiedSource -ne $false) { throw 'Bundled ASSDraw provenance is not the pinned unmodified fork.' }
$exe = Join-Path $portable 'ASSDraw3.exe'
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.sha256) { throw 'Packaged ASSDraw executable hash mismatch.' }
foreach ($source in $manifest.sources) {
  if ([IO.Path]::GetFileName($source.name) -cne $source.name) { throw 'Invalid source manifest filename.' }
  $path = Join-Path $portable "sources/assdraw/$($source.name)"
  if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $source.sha256) { throw "Corresponding source hash mismatch: $($source.name)" }
}
foreach ($name in @('ASSDraw-BSD.txt','ASSDraw-NOTICE.md','ASSDraw-GPL-2.0.txt','AGG-COPYING.txt','wxWidgets-licence.txt','wxWidgets-lgpl.txt')) {
  if (-not (Test-Path -LiteralPath (Join-Path $portable "licenses/assdraw/$name"))) { throw "Missing ASSDraw distribution license: $name" }
}
# Invoke the exact fork's own audited verifier and standalone smoke test from its
# included source. These verify the real packaged bytes, not the negative-case stub.
$auditRoot = Join-Path $env:RUNNER_TEMP ('yoake-assdraw-package-audit-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath (Join-Path $portable 'sources/assdraw/assdraw-source.zip') -DestinationPath $auditRoot
& (Join-Path $auditRoot 'ci/verify-windows-binary.ps1') -Executable $exe
& (Join-Path $auditRoot 'ci/smoke-test-windows.ps1') -Executable $exe
Write-Host "PASS: packaged fork revision $($manifest.commit), executable SHA256 $($manifest.sha256), corresponding source hashes/licenses, static dependency/resource audit and real standalone native window."
