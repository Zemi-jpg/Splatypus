[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseFolder)
$ErrorActionPreference = 'Stop'
$folder = (Resolve-Path -LiteralPath $ReleaseFolder).Path
$inventory = Get-Content -LiteralPath (Join-Path $folder 'release-inventory.json') -Raw | ConvertFrom-Json
$expected = @('Splatypus.gha', 'Splatypus.Core.dll', 'README.md', 'Splatypus-Quick-Start.pdf',
    'icon.png', 'LICENSE.txt', 'NOTICE.txt', 'manifest.yml', 'Examples/Splatypus_Workflow.gh')
Add-Type -AssemblyName System.IO.Compression
foreach ($line in Get-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  ([^/\\]+)$') { throw "Invalid hash record: $line" }
    $hash = $Matches[1]; $name = $Matches[2]
    if ((Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Hash mismatch: $name" }
}
foreach ($record in $inventory.artifacts) {
    $tag = if ($record.platform -eq 'windows') { 'win' } elseif ($record.platform -eq 'macos') { 'mac' } else { throw 'Unknown platform.' }
    if ($record.yak -notmatch ('(?i)^splatypus-' + [regex]::Escape($inventory.version) + '-rh8_34-' + $tag + '\.yak$')) { throw 'Incorrect Rhino/platform distribution tag.' }
    $payloads = @{}
    foreach ($name in @($record.yak, $record.zip)) {
        $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $folder $name))
        try {
            $files = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
            $actual = @($files | ForEach-Object { $_.FullName.Replace('\', '/') })
            if (@(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)).Count -ne 0) { throw "$name violates the release file allowlist: $($actual -join ', ')" }
            $hashMap = @{}
            foreach ($entry in $files) {
                $stream = $entry.Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $hashMap[$entry.FullName.Replace('\', '/')] = [Convert]::ToHexString($sha.ComputeHash($stream)) }
                finally { $stream.Dispose(); $sha.Dispose() }
            }
            foreach ($example in @('Examples/Splatypus_Workflow.gh', 'Publication/Package-README.md', 'Publication/Splatypus-Quick-Start.pdf')) {
                $sourceHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot "../$example") -Algorithm SHA256).Hash
                $entryName = if ($example -eq 'Publication/Package-README.md') { 'README.md' } elseif ($example.StartsWith('Publication/')) { [IO.Path]::GetFileName($example) } else { $example }
                if ($hashMap[$entryName] -ne $sourceHash) { throw "$name contains a stale example: $example" }
            }
            $payloads[$name] = $hashMap
            $manifestEntry = $archive.GetEntry('manifest.yml')
            $reader = [IO.StreamReader]::new($manifestEntry.Open())
            try { $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $reader = [IO.StreamReader]::new($archive.GetEntry('LICENSE.txt').Open())
            try { $license = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $rootLicense = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../LICENSE.txt') -Raw
            if ($license.Trim() -ne $rootLicense.Trim() -or $inventory.license -ne 'MIT') { throw "$name does not contain the active MIT License." }
            if ($manifest -notmatch '(?m)^name: Splatypus\s*$' -or $manifest -notmatch ('(?m)^version: ' + [regex]::Escape($inventory.version) + '\s*$') -or
                $manifest -notmatch 'guid:7dca094a-626a-4b5b-ae1c-a626641fcf3c' -or $manifest -notmatch '(?m)^icon: icon.png\s*$') { throw "$name has inconsistent manifest metadata." }
            foreach ($binary in @('Splatypus.gha', 'Splatypus.Core.dll')) {
                $reader = [IO.MemoryStream]::new()
                $source = $archive.GetEntry($binary).Open()
                try {
                    $source.CopyTo($reader)
                    $reader.Position = 0
                    $pe = [System.Reflection.PortableExecutable.PEReader]::new($reader)
                    try {
                        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                        $assembly = $metadata.GetAssemblyDefinition()
                        if ($assembly.Version.ToString() -ne $record.assemblyVersion) { throw "$name has the wrong $binary version." }
                        if ($binary -eq 'Splatypus.gha') {
                            $references = @{}
                            foreach ($handle in $metadata.AssemblyReferences) { $ref = $metadata.GetAssemblyReference($handle); $references[$metadata.GetString($ref.Name)] = $ref.Version.ToString() }
                            foreach ($sdk in @('Grasshopper', 'RhinoCommon')) { if ($references[$sdk] -ne '8.34.26223.11001') { throw "$name references the wrong $sdk." } }
                            if ($tag -eq 'mac' -and $references.ContainsKey('System.Windows.Forms')) { throw 'Mac build depends on Windows Forms.' }
                            if (!$references.ContainsKey('Splatypus.Core')) { throw 'Core library reference missing.' }
                            if ($references['Splatypus.Core'] -ne $record.assemblyVersion) { throw 'GHA/Core library version mismatch.' }
                            if (@($metadata.ManifestResources).Count -ne 21) { throw 'Expected 21 embedded component icons.' }
                            $expectedIcons = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'assets/component-inventory.json') -Raw | ConvertFrom-Json | ForEach-Object { "RhinoSplat.Icons.$($_.id).png" })
                            $actualIcons = @($metadata.ManifestResources | ForEach-Object { $metadata.GetString($metadata.GetManifestResource($_).Name) })
                            if (@(Compare-Object ($expectedIcons | Sort-Object) ($actualIcons | Sort-Object)).Count) { throw 'Embedded icons do not match the current component inventory.' }
                        }
                    } finally { $pe.Dispose() }
                } finally { $source.Dispose(); $reader.Dispose() }
            }
        } finally { $archive.Dispose() }
        Write-Output "Verified ${name}: metadata, version, 8.34 SDK, icons and allowlisted contents."
    }
    foreach ($entryName in $expected | Where-Object { $_ -ne 'manifest.yml' }) {
        # Yak may normalize its manifest; all other payload bytes must match the manual ZIP.
        if ($payloads[$record.yak][$entryName] -ne $payloads[$record.zip][$entryName]) { throw "Yak/ZIP mismatch for $entryName" }
    }
}
Write-Output 'Release archive and SHA-256 checks passed. Native Rhino installation/training are separate checks.'
