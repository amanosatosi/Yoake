param(
  [Parameter(Mandatory = $true)][string]$Executable,
  [string]$MediaFixturesRoot,
  [string]$VerificationRoot
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
if (-not $VerificationRoot) { $VerificationRoot = Join-Path (Split-Path $exe -Parent) '..\ui-startup-verification' }
$root = [IO.Path]::GetFullPath($VerificationRoot)
New-Item -ItemType Directory -Force -Path $root | Out-Null

foreach ($case in @('clean', 'legacy', 'damaged', 'null-collections', 'irrecoverable', 'fatal-diagnostics')) {
  $caseRoot = Join-Path $root ($case + '-' + [Guid]::NewGuid().ToString('N'))
  $profile = Join-Path $caseRoot 'profile'
  $logs = Join-Path $caseRoot 'logs'
  $report = Join-Path $caseRoot 'verification.txt'
  New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
  if ($case -eq 'fatal-diagnostics') {
    # A file where a profile directory should be forces real logger/bootstrap
    # failure, proving top-level reporting works before a window can be created.
    Set-Content -LiteralPath $profile -Value 'not a directory'
  } else {
    New-Item -ItemType Directory -Force -Path $profile | Out-Null
    $json = switch ($case) {
      'legacy' { '{"schemaVersion":1,"theme":2,"mainSplitRatio":0.5,"gridHeight":230}' }
      'damaged' { '{"theme":2,"gridColumnWidths":["NaN",-5,1000000],"recentFiles":[null,"","test.ass"],"futureField":{"preserve":true}}' }
      'null-collections' { '{"theme":1,"gridColumnWidths":null,"recentFiles":null}' }
      'irrecoverable' { '{broken' }
      default { $null }
    }
    if ($null -ne $json) { Set-Content -LiteralPath (Join-Path $profile 'settings.json') -Value $json -Encoding utf8 }
  }
  $arguments = @('--verify-ui-startup', ('"{0}"' -f $report), '--profile-directory', ('"{0}"' -f $profile), '--diagnostics-directory', ('"{0}"' -f $logs))
  if ($MediaFixturesRoot) { $arguments += @('--verification-media', ('"{0}"' -f ([IO.Path]::GetFullPath($MediaFixturesRoot)))) }
  $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe -Parent) -WindowStyle Hidden -PassThru
  try {
    if (-not $process.WaitForExit(40000)) { throw "UI startup verification timed out: $case" }
    $text = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    if ($case -eq 'fatal-diagnostics') {
      if ($process.ExitCode -eq 0 -or -not $text.StartsWith('FAIL')) { throw 'Expected bootstrap failure did not produce a failing report/exit code.' }
      $crashes = @(Get-ChildItem -LiteralPath $logs -Filter 'crash-*.log')
      if ($crashes.Count -ne 1) { throw 'Fatal bootstrap did not write its independent crash log.' }
      $crash = Get-Content -LiteralPath $crashes[0].FullName -Raw
      foreach ($required in @('Application:', 'Process architecture:', 'OS architecture:', 'Exception type:', 'Desktop startup/dispatcher')) {
        if (-not $crash.Contains($required)) { throw "Crash report is missing: $required" }
      }
      Write-Host 'PASS: fatal bootstrap diagnostics, nonzero exit, independent full crash report.'
    } else {
      if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Real UI startup failed for $case (exit $($process.ExitCode)):`n$text" }
      Write-Host "$case : $text"
    }
  } catch {
    if (Test-Path -LiteralPath $logs) { Get-ChildItem -LiteralPath $logs -File | ForEach-Object { Write-Host $_.Name; Get-Content -LiteralPath $_.FullName } }
    throw
  } finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
  }
}
