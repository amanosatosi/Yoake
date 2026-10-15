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
# Select Lines/visible-selection workflows have an independent process/profile.
$selectionRoot = Join-Path $root ('selection-' + [Guid]::NewGuid().ToString('N'))
$selectionProfile = Join-Path $selectionRoot 'profile'
New-Item -ItemType Directory -Force -Path $selectionProfile | Out-Null
foreach ($case in @('fresh', 'restored')) {
  $logs = Join-Path $selectionRoot ($case + '-logs')
  $report = Join-Path $selectionRoot ($case + '.txt')
  $arguments = @('--verify-ui-selection', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $selectionProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
  try {
    if (-not $process.WaitForExit(150000)) { throw "Packaged selection acceptance timed out: $case" }
    $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged selection acceptance failed for $case (exit $($process.ExitCode)):`n$text" }
    Write-Host $text
  } catch {
    Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
    throw
  } finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
  }
}
# Audio export exercises the real menu/input path in a separate native process.
$clipRoot = Join-Path $root ('audio-clip-' + [Guid]::NewGuid().ToString('N'))
$clipProfile = Join-Path $clipRoot 'profile'
New-Item -ItemType Directory -Force -Path $clipProfile | Out-Null
$logs = Join-Path $clipRoot 'logs'
$report = Join-Path $clipRoot 'acceptance.txt'
$arguments = @('--verify-ui-audio-clip', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $clipProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged audio clip acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged audio clip acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}
# Audio playback ranges and focused context keys use their own acceptance process.
$playbackRoot = Join-Path $root ('audio-playback-' + [Guid]::NewGuid().ToString('N'))
$playbackProfile = Join-Path $playbackRoot 'profile'
New-Item -ItemType Directory -Force -Path $playbackProfile | Out-Null
$logs = Join-Path $playbackRoot 'logs'
$report = Join-Path $playbackRoot 'acceptance.txt'
$arguments = @('--verify-ui-audio-playback', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $playbackProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged audio playback acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged audio playback acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}
# Pending timing uses a separate real-input acceptance process.
$audioTimingRoot = Join-Path $root ('audio-timing-' + [Guid]::NewGuid().ToString('N'))
$audioTimingProfile = Join-Path $audioTimingRoot 'profile'
New-Item -ItemType Directory -Force -Path $audioTimingProfile | Out-Null
$logs = Join-Path $audioTimingRoot 'logs'
$report = Join-Path $audioTimingRoot 'acceptance.txt'
$arguments = @('--verify-ui-audio-timing', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $audioTimingProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged audio timing acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged audio timing acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Video tool input uses a separate real-input acceptance process.
$videoToolsRoot = Join-Path $root ('video-tools-' + [Guid]::NewGuid().ToString('N'))
$videoToolsProfile = Join-Path $videoToolsRoot 'profile'
New-Item -ItemType Directory -Force -Path $videoToolsProfile | Out-Null
$logs = Join-Path $videoToolsRoot 'logs'
$report = Join-Path $videoToolsRoot 'acceptance.txt'
$arguments = @('--verify-ui-video', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $videoToolsProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Video tools acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Video tools acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Video navigation input uses a separate real-input acceptance process.
$videoNavigationRoot = Join-Path $root ('video-navigation-' + [Guid]::NewGuid().ToString('N'))
$videoNavigationProfile = Join-Path $videoNavigationRoot 'profile'
New-Item -ItemType Directory -Force -Path $videoNavigationProfile | Out-Null
$logs = Join-Path $videoNavigationRoot 'logs'
$report = Join-Path $videoNavigationRoot 'acceptance.txt'
$arguments = @('--verify-ui-video-navigation', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $videoNavigationProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Video navigation acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Video navigation acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Independent media ownership input uses a separate real-input acceptance process.
$mediaOwnershipRoot = Join-Path $root ('media-ownership-' + [Guid]::NewGuid().ToString('N'))
$mediaOwnershipProfile = Join-Path $mediaOwnershipRoot 'profile'
New-Item -ItemType Directory -Force -Path $mediaOwnershipProfile | Out-Null
$logs = Join-Path $mediaOwnershipRoot 'logs'
$report = Join-Path $mediaOwnershipRoot 'acceptance.txt'
$arguments = @('--verify-ui-media-ownership', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $mediaOwnershipProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Independent media ownership acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Independent media ownership acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Video utilities input uses a separate real-input acceptance process.
$videoUtilitiesRoot = Join-Path $root ('video-utilities-' + [Guid]::NewGuid().ToString('N'))
$videoUtilitiesProfile = Join-Path $videoUtilitiesRoot 'profile'
New-Item -ItemType Directory -Force -Path $videoUtilitiesProfile | Out-Null
$logs = Join-Path $videoUtilitiesRoot 'logs'
$report = Join-Path $videoUtilitiesRoot 'acceptance.txt'
$arguments = @('--verify-ui-video-utilities', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $videoUtilitiesProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Video utilities acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Video utilities acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Video presentation input uses a separate real-input acceptance process.
# Original/Paste Over uses its own native process and modal clipboard workflow.
$originalPasteRoot = Join-Path $root ('original-paste-' + [Guid]::NewGuid().ToString('N'))
$originalPasteProfile = Join-Path $originalPasteRoot 'profile'
New-Item -ItemType Directory -Force -Path $originalPasteProfile | Out-Null
$logs = Join-Path $originalPasteRoot 'logs'
$report = Join-Path $originalPasteRoot 'acceptance.txt'
$arguments = @('--verify-ui-original-paste', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $originalPasteProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Original/Paste Over acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Original/Paste Over acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Toshiki K-Timing is intentionally two independent native processes. The first is
# terminated without Closing/Dispose; the second must use only persisted data.
$karaokeRoot = Join-Path $root ('karaoke-' + [Guid]::NewGuid().ToString('N'))
$karaokeProfile = Join-Path $karaokeRoot 'profile'
New-Item -ItemType Directory -Force -Path $karaokeProfile | Out-Null
$captureReport = Join-Path $karaokeRoot 'capture.txt'
$captureLogs = Join-Path $karaokeRoot 'capture-logs'
$arguments = @('--verify-ui-karaoke', ('"{0}"' -f $captureReport), '--profile-directory', ('"{0}"' -f $karaokeProfile), '--diagnostics-directory', ('"{0}"' -f $captureLogs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(150)
  while (-not (Test-Path -LiteralPath $captureReport) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
  $text = if (Test-Path -LiteralPath $captureReport) { Get-Content -LiteralPath $captureReport -Raw } else { '' }
  if ($process.HasExited -or -not $text.StartsWith('READY:')) { throw "Toshiki capture failed before forced termination:`n$text" }
  $process.Kill(); $process.WaitForExit(); Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $captureLogs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}
$report = Join-Path $karaokeRoot 'recovered.txt'
$logs = Join-Path $karaokeRoot 'restart-logs'
$arguments = @('--verify-ui-karaoke-recovery', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $karaokeProfile), '--diagnostics-directory', ('"{0}"' -f $logs))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged restarted Toshiki recovery acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Toshiki recovery acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Recovery is intentionally two independent native processes. The first is
# terminated without Closing/Dispose; the second must use only persisted data.
$recoveryRoot = Join-Path $root ('recovery-' + [Guid]::NewGuid().ToString('N'))
$recoveryProfile = Join-Path $recoveryRoot 'profile'
New-Item -ItemType Directory -Force -Path $recoveryProfile | Out-Null
$captureReport = Join-Path $recoveryRoot 'capture.txt'
$captureLogs = Join-Path $recoveryRoot 'capture-logs'
$arguments = @('--verify-ui-recovery-capture', ('"{0}"' -f $captureReport), '--profile-directory', ('"{0}"' -f $recoveryProfile), '--diagnostics-directory', ('"{0}"' -f $captureLogs))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(150)
  while (-not (Test-Path -LiteralPath $captureReport) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
  $text = if (Test-Path -LiteralPath $captureReport) { Get-Content -LiteralPath $captureReport -Raw } else { '' }
  if ($process.HasExited -or -not $text.StartsWith('READY:')) { throw "Recovery capture failed before forced termination:`n$text" }
  $process.Kill(); $process.WaitForExit(); Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $captureLogs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}
$report = Join-Path $recoveryRoot 'recovered.txt'
$logs = Join-Path $recoveryRoot 'restart-logs'
$arguments = @('--verify-ui-recovery', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $recoveryProfile), '--diagnostics-directory', ('"{0}"' -f $logs))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged restarted Recovery acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Recovery acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Attachment integration and unsaved-import crash recovery have their own two
# native processes, independent of Stage 2A and startup acceptance.
$attachmentRoot = Join-Path $root ('attachments-' + [Guid]::NewGuid().ToString('N'))
$attachmentProfile = Join-Path $attachmentRoot 'profile'
New-Item -ItemType Directory -Force -Path $attachmentProfile | Out-Null
$captureReport = Join-Path $attachmentRoot 'capture.txt'
$captureLogs = Join-Path $attachmentRoot 'capture-logs'
$arguments = @('--verify-ui-attachments-capture', ('"{0}"' -f $captureReport), '--profile-directory', ('"{0}"' -f $attachmentProfile), '--diagnostics-directory', ('"{0}"' -f $captureLogs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(150)
  while (-not (Test-Path -LiteralPath $captureReport) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
  $text = if (Test-Path -LiteralPath $captureReport) { Get-Content -LiteralPath $captureReport -Raw } else { '' }
  if ($process.HasExited -or -not $text.StartsWith('READY:')) { throw "Attachment GUI capture failed:`n$text" }
  $process.Kill(); $process.WaitForExit(); Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $captureLogs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}
$report = Join-Path $attachmentRoot 'recovered.txt'
$logs = Join-Path $attachmentRoot 'restart-logs'
$arguments = @('--verify-ui-attachments-recovery', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $attachmentProfile), '--diagnostics-directory', ('"{0}"' -f $logs))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged attachment restart acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged attachment recovery failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

# Resolution resampling owns a separate native process and watchdog.
$resampleRoot = Join-Path $root ('resample-' + [Guid]::NewGuid().ToString('N'))
$resampleProfile = Join-Path $resampleRoot 'profile'
New-Item -ItemType Directory -Force -Path $resampleProfile | Out-Null
$logs = Join-Path $resampleRoot 'logs'
$report = Join-Path $resampleRoot 'acceptance.txt'
$arguments = @('--verify-ui-resample', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $resampleProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged resolution resampling acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged resolution resampling failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

$fontRoot = Join-Path $root ('font-collector-' + [Guid]::NewGuid().ToString('N'))
$fontProfile = Join-Path $fontRoot 'profile'
New-Item -ItemType Directory -Force -Path $fontProfile | Out-Null
$logs = Join-Path $fontRoot 'logs'
$report = Join-Path $fontRoot 'acceptance.txt'
$arguments = @('--verify-ui-font-collector', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $fontProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Fonts Collector acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Fonts Collector failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

$assDrawRoot = Join-Path $root ('assdraw-' + [Guid]::NewGuid().ToString('N'))
$assDrawProfile = Join-Path $assDrawRoot 'profile'
New-Item -ItemType Directory -Force -Path $assDrawProfile | Out-Null
$logs = Join-Path $assDrawRoot 'logs'
$report = Join-Path $assDrawRoot 'acceptance.txt'
$arguments = @('--verify-ui-assdraw', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $assDrawProfile), '--diagnostics-directory', ('"{0}"' -f $logs))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged ASSDraw external integration timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged ASSDraw external integration failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}

$videoPresentationRoot = Join-Path $root ('video-presentation-' + [Guid]::NewGuid().ToString('N'))
$videoPresentationProfile = Join-Path $videoPresentationRoot 'profile'
New-Item -ItemType Directory -Force -Path $videoPresentationProfile | Out-Null
$logs = Join-Path $videoPresentationRoot 'logs'
$report = Join-Path $videoPresentationRoot 'acceptance.txt'
$arguments = @('--verify-ui-video-presentation', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $videoPresentationProfile), '--diagnostics-directory', ('"{0}"' -f $logs), '--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot))))
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
try {
  if (-not $process.WaitForExit(150000)) { throw 'Packaged Video presentation acceptance timed out' }
  $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
  if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Packaged Video presentation acceptance failed (exit $($process.ExitCode)):`n$text" }
  Write-Host $text
} catch {
  Get-ChildItem -LiteralPath $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 90 }
  throw
} finally {
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  $process.Dispose()
}
