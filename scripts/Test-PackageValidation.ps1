param([string]$Archive)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Archive) { $Archive = Join-Path $root 'artifacts/EpicLootAutoAugment-0.1.0.zip' }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$testRoot = Join-Path $root ('artifacts/zip-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $testRoot | Out-Null
$cases = @('BepInEx\bad.dll','/absolute','C:/drive','a/../bad','a/./bad','a//bad',("bad" + [char]1),'README.MD','wrong-dll','wrong-version')
$passed = 0
foreach ($case in $cases) {
    $file = Join-Path $testRoot ("case-$passed.zip")
    Copy-Item -LiteralPath $Archive -Destination $file
    $zip = [IO.Compression.ZipFile]::Open($file, [IO.Compression.ZipArchiveMode]::Update)
    try {
        if ($case -eq 'wrong-dll') { $entryName = 'BepInEx/plugins/EpicLootAutoAugment/EpicLootAutoAugment.dll'; $zip.GetEntry($entryName).Delete(); $entry = $zip.CreateEntry($entryName) }
        elseif ($case -eq 'wrong-version') {
            $old = $zip.GetEntry('manifest.json'); $reader = New-Object IO.StreamReader($old.Open())
            try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            $manifest.version_number = '99.0.0'; $old.Delete(); $entry = $zip.CreateEntry('manifest.json')
        } elseif ($case.Contains([char]1)) { $entry = $zip.CreateEntry('badX') }
        else { $entry = $zip.CreateEntry($case) }
        $writer = New-Object IO.StreamWriter($entry.Open())
        try { if ($case -eq 'wrong-version') { $writer.Write(($manifest | ConvertTo-Json -Depth 5)) } else { $writer.Write('invalid fixture') } } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    if ($case.Contains([char]1)) {
        # ZipArchive refuses controls on creation. Mutate the local/central filename bytes in
        # the fixture so validation still exercises an archive received from another writer.
        $bytes = [IO.File]::ReadAllBytes($file)
        for ($i = 0; $i -lt $bytes.Length - 3; $i++) {
            if ($bytes[$i] -eq 98 -and $bytes[$i+1] -eq 97 -and $bytes[$i+2] -eq 100 -and $bytes[$i+3] -eq 88) { $bytes[$i+3] = 1 }
        }
        [IO.File]::WriteAllBytes($file, $bytes)
    }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Validate-Package.ps1') -Archive $file | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw "Validator accepted invalid package: $case" }
    $passed++
}
& (Join-Path $PSScriptRoot 'Validate-Package.ps1') -Archive $Archive
Write-Output "$passed invalid ZIP fixtures rejected; intended ZIP accepted."
