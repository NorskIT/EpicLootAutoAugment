param([string]$Name = ('network-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid isolated test name.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$coord = Join-Path $root "artifacts/$Name"
if (Test-Path -LiteralPath $coord) { throw 'Use a fresh test directory.' }
New-Item -ItemType Directory -Path $coord | Out-Null
& (Join-Path $PSScriptRoot 'Test-RuntimeSmoke.ps1') -Name "$Name-host" -NetworkRole host -Coord $coord
& (Join-Path $PSScriptRoot 'Test-RuntimeSmoke.ps1') -Name "$Name-client" -NetworkRole client -Coord $coord
