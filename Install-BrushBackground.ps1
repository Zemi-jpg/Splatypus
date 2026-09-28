$ErrorActionPreference = 'Stop'
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    throw 'Close all Rhino windows before replacing the Grasshopper plugin, then run this script again.'
}
$sourceFolder = Join-Path $PSScriptRoot 'bin\BrushBackground'
$targetFolder = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Grasshopper\Libraries\RhinoSplat'
$fileNames = @('RhinoSplat.gha', 'RhinoSplat.Core.dll')
foreach ($fileName in $fileNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFolder $fileName) -PathType Leaf)) {
        throw "Build file is missing: $fileName"
    }
}
$backupFolder = Join-Path $PSScriptRoot ('bin\PluginBackups\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
foreach ($fileName in $fileNames) {
    $targetFile = Join-Path $targetFolder $fileName
    if (Test-Path -LiteralPath $targetFile) {
        Copy-Item -LiteralPath $targetFile -Destination (Join-Path $backupFolder $fileName)
    }
}
foreach ($fileName in $fileNames) {
    $sourceFile = Join-Path $sourceFolder $fileName
    $targetFile = Join-Path $targetFolder $fileName
    Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force
    if ((Get-FileHash -LiteralPath $sourceFile).Hash -ne (Get-FileHash -LiteralPath $targetFile).Hash) {
        throw "Installation verification failed: $fileName"
    }
}
Write-Output "Installed and verified both plugin files. Restart Rhino. Previous files: $backupFolder"
