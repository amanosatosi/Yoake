param(
  [Parameter(Mandatory = $true)][string]$Executable,
  [Parameter(Mandatory = $true)][string]$MediaFixturesRoot,
  [Parameter(Mandatory = $true)][string]$VerificationRoot
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$root = [IO.Path]::GetFullPath($VerificationRoot)
$caseRoot = Join-Path $root ('hotkeys-' + [Guid]::NewGuid().ToString('N'))
$profile = Join-Path $caseRoot 'profile'
New-Item -ItemType Directory -Force -Path $profile | Out-Null
# The second launch reuses the real settings written by the first. This is
# independent of the unchanged startup/profile smoke matrix.
foreach ($case in @('fresh', 'restored')) {
  $logs = Join-Path $caseRoot ($case + '-logs')
  $report = Join-Path $caseRoot ($case + '.txt')
  $arguments = @('--verify-ui-hotkeys', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $profile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
  try {
    if (-not $process.WaitForExit(150000)) { throw "Packaged contextual hotkey acceptance timed out: $case" }
    $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged contextual hotkey acceptance failed for $case (exit $($process.ExitCode)):`n$text" }
    Write-Host $text
  } catch {
    Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
    throw
  } finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
  }
}

# Timing tools use a fresh profile and a separate process/watchdog from both
# startup and contextual-keyboard acceptance. No existing case is bypassed.
$timingRoot = Join-Path $root ('timing-' + [Guid]::NewGuid().ToString('N'))
$timingProfile = Join-Path $timingRoot 'profile'
New-Item -ItemType Directory -Force -Path $timingProfile | Out-Null
foreach ($case in @('fresh', 'restored')) {
  $logs = Join-Path $timingRoot ($case + '-logs')
  $report = Join-Path $timingRoot ($case + '.txt')
  $arguments = @('--verify-ui-timing', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $timingProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
  try {
    if (-not $process.WaitForExit(150000)) { throw "Packaged timing acceptance timed out: $case" }
    $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged timing acceptance failed for $case (exit $($process.ExitCode)):`n$text" }
    Write-Host $text
  } catch {
    Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
    throw
  } finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
  }
}
