$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
python -m venv "$projectRoot\.venv-robot"
if ($LASTEXITCODE -ne 0) { throw 'Could not create robot Python environment' }
& "$projectRoot\.venv-robot\Scripts\python.exe" -m pip install -r "$projectRoot\tools\requirements-robot.txt"
if ($LASTEXITCODE -ne 0) { throw 'Could not install robot dependencies' }
Write-Host 'Ready. Run companion\robot.cmd start, then companion\robot.cmd --help.'
