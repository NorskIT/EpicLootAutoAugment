param([string]$Name = ('smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss')), [switch]$IncludeContainerAccess,
    [ValidateSet('', 'host', 'client')][string]$NetworkRole = '', [string]$Coord = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$environment = Get-Content -LiteralPath (Join-Path $root 'Environment.props')
$gamePath = [string]$environment.Project.PropertyGroup.VALHEIM_INSTALL
$bepPath = [string]$environment.Project.PropertyGroup.BEPINEX_PATH
$epicDll = [string]$environment.Project.PropertyGroup.EPICLOOT_DLL
if ($Name -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid isolated test name.' }
$stage = Join-Path $root "artifacts/$Name"
if (Test-Path -LiteralPath $stage) { throw 'Use a fresh test directory.' }
$project = if ($NetworkRole -eq 'host') { 'tests/NetworkHost/NetworkHost.csproj' } else { 'tests/RuntimeSmoke/RuntimeSmoke.csproj' }
dotnet build (Join-Path $root $project) -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Smoke build failed.' }
New-Item -ItemType Directory -Force "$stage/BepInEx/core", "$stage/BepInEx/plugins/AutoAugment", "$stage/saves" | Out-Null
Copy-Item -Path (Join-Path $bepPath 'core/*') -Destination "$stage/BepInEx/core"
foreach ($package in @('ValheimModding-Jotunn','ValheimModding-JsonDotNET')) {
    Copy-Item -LiteralPath (Join-Path $bepPath "plugins/$package") -Destination "$stage/BepInEx/plugins/$package" -Recurse
}
Copy-Item -LiteralPath (Split-Path $epicDll -Parent) -Destination "$stage/BepInEx/plugins/EpicLoot" -Recurse
if ($IncludeContainerAccess) {
    Copy-Item -LiteralPath (Join-Path $bepPath 'plugins/NorskIT-EpicLootContainerAccess') -Destination "$stage/BepInEx/plugins/ContainerAccess" -Recurse
}
if ($NetworkRole -eq 'host') {
    Copy-Item -LiteralPath (Join-Path $root 'tests/NetworkHost/bin/Release/net481/AutoAugment.TestHost.dll') -Destination "$stage/BepInEx/plugins/AutoAugment"
} else {
    Copy-Item -Path (Join-Path $root 'src/Plugin/bin/Release/net481/EpicLootAutoAugment*.dll') -Destination "$stage/BepInEx/plugins/AutoAugment"
    Copy-Item -LiteralPath (Join-Path $root 'tests/RuntimeSmoke/bin/Release/net481/EpicLootAutoAugment.RuntimeSmoke.dll') -Destination "$stage/BepInEx/plugins/AutoAugment"
}
$priorTarget = $env:DOORSTOP_TARGET_ASSEMBLY
$priorOutput = $env:AUTOAUGMENT_SMOKE_OUTPUT
$priorCoord = $env:AUTOAUGMENT_NETWORK_COORD
try {
    $env:DOORSTOP_TARGET_ASSEMBLY = "$stage/BepInEx/core/BepInEx.Preloader.dll"
    $env:AUTOAUGMENT_SMOKE_OUTPUT = $stage
    $env:AUTOAUGMENT_NETWORK_COORD = $Coord
    $process = Start-Process -FilePath (Join-Path $gamePath 'valheim.exe') -ArgumentList @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '-savedir', ('"' + "$stage/saves" + '"'), '-logFile', ('"' + "$stage/unity.log" + '"'), '--doorstop-enabled', 'true', '--doorstop-target-assembly', ('"' + "$stage/BepInEx/core/BepInEx.Preloader.dll" + '"')) -WorkingDirectory $stage -WindowStyle Hidden -PassThru
    $process.Id | Set-Content -LiteralPath "$stage/process-id.txt"
    Write-Output "Isolated smoke process $($process.Id): $stage"
} finally {
    $env:DOORSTOP_TARGET_ASSEMBLY = $priorTarget
    $env:AUTOAUGMENT_SMOKE_OUTPUT = $priorOutput
    $env:AUTOAUGMENT_NETWORK_COORD = $priorCoord
}
