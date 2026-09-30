param(
  [Parameter(Mandatory = $true)][string]$Executable,
  [int]$StartupWindowSeconds = 5
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing NativeAOT executable: $Executable" }
$process = Start-Process -FilePath $Executable -PassThru
try {
  Start-Sleep -Seconds $StartupWindowSeconds
  if ($process.HasExited) {
    throw "Yoake exited unexpectedly during the ${StartupWindowSeconds}s startup smoke window with code $($process.ExitCode)."
  }
  Write-Host "Yoake remained alive for the ${StartupWindowSeconds}s startup smoke window."
}
finally {
  if (-not $process.HasExited) {
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
  }
}
