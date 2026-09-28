param([string]$DistributionFolder = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$expectedSdk = [Version]'8.34.26223.11001'
foreach ($platform in @('windows', 'macos')) {
    $folder = Join-Path $DistributionFolder $platform
    $assemblyPath = (Resolve-Path -LiteralPath (Join-Path $folder 'Splatypus.gha')).Path
    $assembly = [Reflection.Assembly]::LoadFile($assemblyPath)
    if ($assembly.GetName().Name -ne 'Splatypus') { throw "$platform has an unexpected assembly name" }
    $references = $assembly.GetReferencedAssemblies()
    if (@($references | Where-Object Name -EQ 'Splatypus.Core').Count -ne 1) { throw "$platform does not reference Splatypus.Core" }
    $iconFolder = Join-Path $PSScriptRoot '../Assets/Icons-Splatypus/png24'
    $iconFiles = @(Get-ChildItem -LiteralPath $iconFolder -Filter '*.png')
    $resources = @($assembly.GetManifestResourceNames())
    if ($iconFiles.Count -ne 21 -or $resources.Count -ne 21) { throw "$platform must contain all 21 Splatypus icons" }
    foreach ($icon in $iconFiles) {
        $stream = $assembly.GetManifestResourceStream("RhinoSplat.Icons.$($icon.Name)")
        if ($null -eq $stream) { throw "$platform is missing icon $($icon.Name)" }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath $icon.FullName).Hash) { throw "$platform contains the wrong artwork for $($icon.Name)" }
    }
    Write-Output "$platform contains all 21 verified Splatypus icons."
    foreach ($name in @('Grasshopper', 'RhinoCommon')) {
        $reference = @($references | Where-Object Name -EQ $name)
        if ($reference.Count -ne 1 -or $reference[0].Version -ne $expectedSdk) {
            throw "$platform has an unexpected $name SDK reference: $reference"
        }
        Write-Output "$platform references $name $expectedSdk"
    }
    if ($platform -eq 'macos' -and @($references | Where-Object Name -EQ 'System.Windows.Forms').Count -ne 0) {
        throw 'Mac build references Windows Forms'
    }
    $zipName = if ($platform -eq 'windows') { 'Splatypus-Windows-workflow.zip' } else { 'Splatypus-macOS-workflow.zip' }
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path (Resolve-Path -LiteralPath $DistributionFolder).Path $zipName))
    try {
        $guide = if ($platform -eq 'windows') { 'README-Windows.md' } else { 'Mac-workflow.md' }
        $expectedFiles = @('Splatypus.gha', 'Splatypus.Core.dll', $guide, 'Download-tools.md', 'Colmap-pipeline.md')
        if ($archive.Entries.Count -ne $expectedFiles.Count) { throw "$zipName contains unexpected files" }
        foreach ($name in $expectedFiles) {
            $entry = $archive.GetEntry($name)
            if ($null -eq $entry) { throw "$zipName is missing $name" }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $folder $name)).Hash) { throw "$zipName contains stale $name" }
        }
        Write-Output "$zipName verified: current plugin, Core DLL and guides only; no bundled host SDK DLLs."
    } finally { $archive.Dispose() }
}
