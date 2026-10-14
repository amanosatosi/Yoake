param([Parameter(Mandatory = $true)][string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $taskRoot | Out-Null
$taskSource = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'fixtures\assdraw_stub.cpp')).Path
$taskExe = Join-Path $taskRoot 'ASSDraw3.exe'
$taskObj = Join-Path $taskRoot 'assdraw_stub.obj'
& cl /nologo /EHsc /DUNICODE /D_UNICODE $taskSource ("/Fo" + $taskObj) ("/Fe" + $taskExe) /link /SUBSYSTEM:WINDOWS user32.lib
if ($LASTEXITCODE -ne 0) { throw 'External-tool process fixture compilation failed' }
if (-not (Test-Path -LiteralPath $taskExe -PathType Leaf)) { throw 'External-tool process fixture is missing' }
