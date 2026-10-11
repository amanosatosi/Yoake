param(
  [Parameter(Mandatory = $true)][string]$Executable,
  [Parameter(Mandatory = $true)][string]$MediaFixturesRoot,
  [Parameter(Mandatory = $true)][string]$VerificationRoot
)
$ErrorActionPreference = 'Stop'
# Timecode acceptance is a separate process below; existing startup, keyboard
# and timing verification remain independent requirements.
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

# External timecode metadata has its own bounded real-window acceptance path.
$timecodeRoot = Join-Path $root ('timecodes-' + [Guid]::NewGuid().ToString('N'))
$timecodeProfile = Join-Path $timecodeRoot 'profile'
New-Item -ItemType Directory -Force -Path $timecodeProfile | Out-Null
foreach ($case in @('fresh', 'restored')) {
  $logs = Join-Path $timecodeRoot ($case + '-logs')
  $report = Join-Path $timecodeRoot ($case + '.txt')
  $arguments = @('--verify-ui-timecodes', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $timecodeProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
  try {
    if (-not $process.WaitForExit(150000)) { throw "Packaged timecode acceptance timed out: $case" }
    $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged timecode acceptance failed for $case (exit $($process.ExitCode)):`n$text" }
    Write-Host $text
  } catch {
    Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
    throw
  } finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
  }
}
# Display/viewport audio workflows run independently of timing and startup.
$audioRoot = Join-Path $root ('audio-display-' + [Guid]::NewGuid().ToString('N'))
$audioProfile = Join-Path $audioRoot 'profile'
New-Item -ItemType Directory -Force -Path $audioProfile | Out-Null
foreach ($case in @('fresh', 'restored')) {
  $logs = Join-Path $audioRoot ($case + '-logs')
  $report = Join-Path $audioRoot ($case + '.txt')
  $arguments = @('--verify-ui-audio-display', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $audioProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
  try {
    if (-not $process.WaitForExit(150000)) { throw "Packaged audio display acceptance timed out: $case" }
    $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged audio display acceptance failed for $case (exit $($process.ExitCode)):`n$text" }
    Write-Host $text
  } catch {
    Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
    throw
  } finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
  }
}