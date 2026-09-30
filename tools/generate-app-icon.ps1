param(
  [Parameter(Mandatory = $true)][string]$SourceSvg,
  [Parameter(Mandatory = $true)][string]$OutputIco
)
$ErrorActionPreference = 'Stop'
$SourceSvg = [IO.Path]::GetFullPath($SourceSvg)
$OutputIco = [IO.Path]::GetFullPath($OutputIco)
if (-not (Test-Path -LiteralPath $SourceSvg)) { throw "Missing app icon SVG: $SourceSvg" }
$magick = Get-Command magick -ErrorAction SilentlyContinue
if (-not $magick) {
  throw 'ImageMagick (magick) is required to generate the Windows application icon from assets/icons/app/yoake-logo.svg.'
}
$dir = Split-Path -Parent $OutputIco
New-Item -ItemType Directory -Force -Path $dir | Out-Null
if (Test-Path -LiteralPath $OutputIco) { Remove-Item -LiteralPath $OutputIco -Force }
& $magick.Source -background none $SourceSvg -define icon:auto-resize=256,128,64,48,32,24,16 $OutputIco
if ($LASTEXITCODE -ne 0) { throw "ImageMagick icon generation failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $OutputIco)) { throw "Application icon was not generated: $OutputIco" }
if ((Get-Item -LiteralPath $OutputIco).Length -lt 512) { throw "Generated application icon looks invalid: $OutputIco" }
Write-Host "Generated Windows application icon: $OutputIco"
