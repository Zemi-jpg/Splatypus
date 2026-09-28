$ErrorActionPreference = 'Stop'
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    throw 'Close Rhino before installing Splatypus.'
}

$sourceFolder = Join-Path $PSScriptRoot 'dist\windows'
$fileNames = @('Splatypus.gha', 'Splatypus.Core.dll')
foreach ($file in $fileNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFolder $file) -PathType Leaf)) {
        throw "Missing installation file: $file"
    }
}

$libraries = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Grasshopper\Libraries'))
$targetFolder = Join-Path $libraries 'Splatypus'
$oldFolder = Join-Path $libraries 'RhinoSplat'
$backupFolder = Join-Path $PSScriptRoot ('bin\PluginBackups\Splatypus-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))

# Every move is restricted to an explicitly named immediate child of Libraries.
foreach ($path in @($oldFolder, $targetFolder)) {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $libraries) {
        throw "Unexpected installation path: $path"
    }
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Installation folder must not be a link: $path"
        }
        if ((Resolve-Path -LiteralPath $path).Path -ne [IO.Path]::GetFullPath($path)) {
            throw "Unexpected resolved installation path: $path"
        }
    }
}
if (Test-Path -LiteralPath $libraries) {
    $copies = @(Get-ChildItem -LiteralPath $libraries -Recurse -File | Where-Object { $_.Name -in @('RhinoSplat.gha', 'Splatypus.gha') })
    foreach ($copy in $copies) {
        if ($copy.DirectoryName -notin @($oldFolder, $targetFolder)) {
            throw "Another plugin copy needs to be moved outside Libraries first: $($copy.FullName)"
        }
    }
}

New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
foreach ($folder in @($oldFolder, $targetFolder)) {
    if (Test-Path -LiteralPath $folder) {
        Move-Item -LiteralPath $folder -Destination (Join-Path $backupFolder ([IO.Path]::GetFileName($folder)))
    }
}
New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
foreach ($file in $fileNames) {
    $source = Join-Path $sourceFolder $file
    $target = Join-Path $targetFolder $file
    Copy-Item -LiteralPath $source -Destination $target
    Unblock-File -LiteralPath $target
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw "Installed file verification failed: $file. Previous installation is backed up at $backupFolder"
    }
}
$installed = @(Get-ChildItem -LiteralPath $libraries -Recurse -File | Where-Object { $_.Name -in @('RhinoSplat.gha', 'Splatypus.gha') })
if ($installed.Count -ne 1 -or $installed[0].FullName -ne (Join-Path $targetFolder 'Splatypus.gha')) {
    throw 'Unexpected duplicate plugin installation after upgrade.'
}
Write-Output "Installed and verified Splatypus: $targetFolder"
Write-Output "Previous plugin backup: $backupFolder"
Write-Output 'Start Rhino and open Grasshopper to use the Splatypus tab.'
