$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $root 'dist/Splatypus-Publication-Kit.zip'
& (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ReleaseFolder (Join-Path $root 'dist/publication')
Add-Type -AssemblyName System.IO.Compression
$files = [Collections.Generic.List[string]]::new()
foreach ($dir in @('Publication', 'dist/publication')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $dir) -File -Recurse) {
        # The upload kit contains publication material, not build sources or license templates.
        if ($dir -eq 'Publication' -and ($file.Extension -in @('.ps1', '.props', '.py', '.cjs') -or $file.DirectoryName -eq (Join-Path $PSScriptRoot 'legal'))) { continue }
        $files.Add($file.FullName)
    }
}
foreach ($name in @('LICENSE.txt', 'NOTICE.txt', 'Examples/Splatypus_Workflow.gh')) {
    $files.Add((Join-Path $root $name))
}
$stream = [IO.File]::Open($output, [IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($root, $file).Replace('\', '/')
        if ($relative.StartsWith('../')) { throw 'Publication bundle file escaped the workspace.' }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root 'Publication/Package-README.md'), 'README.md', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root 'Publication/Splatypus-Quick-Start.pdf'), 'Splatypus-Quick-Start.pdf', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $archive.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $root 'dist/Splatypus-Publication-Kit.sha256.txt') -Value "$hash  Splatypus-Publication-Kit.zip" -Encoding ascii
Write-Output "Prepared publication bundle with $($files.Count + 2) files: $output"
