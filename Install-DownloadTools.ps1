$ErrorActionPreference = 'Stop'
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    throw 'Close all Rhino windows before installing the Download Tools update.'
}
$sourceFolder = Join-Path $PSScriptRoot 'dist\windows'
$librariesFolder = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Grasshopper\Libraries'
$targetFolder = Join-Path $librariesFolder 'Splatypus'
foreach ($oldFile in @('RhinoSplat\RhinoSplat.gha', 'RhinoSplat.gha', 'Splatypus\RhinoSplat.gha')) {
    if (Test-Path -LiteralPath (Join-Path $librariesFolder $oldFile)) {
        throw 'Move the previous RhinoSplat plugin outside the Components Folder before installing Splatypus; they share component IDs.'
    }
}
$fileNames = @('Splatypus.gha', 'Splatypus.Core.dll')
foreach ($fileName in $fileNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFolder $fileName) -PathType Leaf)) { throw "Build file is missing: $fileName" }
}
$backupFolder = Join-Path $PSScriptRoot ('bin\PluginBackups\DownloadTools-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
foreach ($fileName in $fileNames) {
    $targetFile = Join-Path $targetFolder $fileName
    if (Test-Path -LiteralPath $targetFile) { Copy-Item -LiteralPath $targetFile -Destination (Join-Path $backupFolder $fileName) }
}
foreach ($fileName in $fileNames) {
    $sourceFile = Join-Path $sourceFolder $fileName
    $targetFile = Join-Path $targetFolder $fileName
    Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force
    if ((Get-FileHash -LiteralPath $sourceFile).Hash -ne (Get-FileHash -LiteralPath $targetFile).Hash) { throw "Installation verification failed: $fileName" }
}
Write-Output "Installed Download Tools. Restart Rhino. Previous plugin files: $backupFolder"
