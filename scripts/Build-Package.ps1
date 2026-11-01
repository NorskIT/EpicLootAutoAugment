$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
dotnet build (Join-Path $root 'src/Plugin/Plugin.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet test (Join-Path $root 'tests/Core.Tests/Core.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
& (Join-Path $PSScriptRoot 'New-PackageIcon.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $root 'packaging/manifest.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts') | Out-Null
$archive = Join-Path $root "artifacts/EpicLootAutoAugment-$($manifest.version_number).zip"
$files = [ordered]@{
    'manifest.json' = 'packaging/manifest.json'
    'icon.png' = 'packaging/icon.png'
    'README.md' = 'README.md'
    'CHANGELOG.md' = 'CHANGELOG.md'
    'VERIFICATION.md' = 'VERIFICATION.md'
    'BepInEx/plugins/EpicLootAutoAugment/EpicLootAutoAugment.dll' = 'src/Plugin/bin/Release/net481/EpicLootAutoAugment.dll'
    'BepInEx/plugins/EpicLootAutoAugment/EpicLootAutoAugment.Core.dll' = 'src/Plugin/bin/Release/net481/EpicLootAutoAugment.Core.dll'
}
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $files.GetEnumerator()) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $entry.Value), $entry.Key, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
& (Join-Path $PSScriptRoot 'Validate-Package.ps1') -Archive $archive
Write-Output "Validated test package: $archive"
