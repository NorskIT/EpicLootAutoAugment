param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem, System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName
        if (!$name -or $name -match '[\\:\x00-\x1f\x7f]' -or $name.StartsWith('/') -or
            @($name.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -gt 0 -or
            @($name.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0) { throw "Unsafe ZIP entry: $name" }
        if ($entries.ContainsKey($name)) { throw "Duplicate ZIP entry: $name" }
        $entries[$name] = $entry
    }
    $required = @('manifest.json','README.md','CHANGELOG.md','VERIFICATION.md','icon.png','BepInEx/plugins/EpicLootAutoAugment/EpicLootAutoAugment.dll','BepInEx/plugins/EpicLootAutoAugment/EpicLootAutoAugment.Core.dll')
    foreach ($name in $required) { if (!$entries.ContainsKey($name)) { throw "Missing package entry: $name" } }
    if ($entries.Count -ne $required.Count) { throw 'Unexpected package entries.' }
    $reader = New-Object IO.StreamReader($entries['manifest.json'].Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    $intended = Get-Content -LiteralPath (Join-Path $root 'packaging/manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.name -ne $intended.name -or $manifest.version_number -ne $intended.version_number -or
        (($manifest.dependencies | Sort-Object) -join '|') -ne (($intended.dependencies | Sort-Object) -join '|')) { throw 'Manifest differs from intended build.' }
    foreach ($dll in @('EpicLootAutoAugment.dll', 'EpicLootAutoAugment.Core.dll')) {
        $built = Join-Path $root "src/Plugin/bin/Release/net481/$dll"
        $stream = $entries["BepInEx/plugins/EpicLootAutoAugment/$dll"].Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash) { throw "Packaged DLL differs: $dll" }
        if ([Reflection.AssemblyName]::GetAssemblyName($built).Version.ToString(3) -ne $manifest.version_number) { throw "DLL version differs: $dll" }
    }
    $stream = $entries['icon.png'].Open()
    try {
        $icon = [Drawing.Image]::FromStream($stream)
        try { if ($icon.Width -ne 256 -or $icon.Height -ne 256) { throw 'Icon must be 256 x 256.' } } finally { $icon.Dispose() }
    } finally { $stream.Dispose() }
    Write-Output 'Finished ZIP validation passed: paths, uniqueness, metadata, icon, versions and DLL hashes.'
} finally { $zip.Dispose() }
