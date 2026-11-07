$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    dotnet build Source/RhinoSplat.Grasshopper.csproj -c Release -o dist/windows
    if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
    dotnet build Source/RhinoSplat.Grasshopper.Mac.csproj -c Release -o dist/macos
    if ($LASTEXITCODE -ne 0) { throw 'macOS build failed' }
    foreach ($platform in @('windows', 'macos')) {
        $folder = Join-Path 'dist' $platform
        Copy-Item -LiteralPath 'Publication/Package-README.md' -Destination (Join-Path $folder 'README.md') -Force
        foreach ($name in @('LICENSE.txt', 'NOTICE.txt')) {
            Copy-Item -LiteralPath $name -Destination (Join-Path $folder $name) -Force
        }
        Copy-Item -LiteralPath 'Publication/Splatypus-Quick-Start.pdf' -Destination (Join-Path $folder 'Splatypus-Quick-Start.pdf') -Force
        $examples = Join-Path $folder 'Examples'
        New-Item -ItemType Directory -Path $examples -Force | Out-Null
        foreach ($name in @('Splatypus_Workflow.gh')) {
            Copy-Item -LiteralPath (Join-Path 'Examples' $name) -Destination (Join-Path $examples $name) -Force
        }
        $zip = if ($platform -eq 'windows') { 'Splatypus-Windows-workflow.zip' } else { 'Splatypus-macOS-workflow.zip' }
        # Stage the example directory alone so legacy notes cannot enter the archive.
        $zipStage = Join-Path (Split-Path -Parent $PSScriptRoot) ('bin/manual-package/' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $zipStage 'Examples') -Force | Out-Null
        foreach ($name in @('Splatypus.gha', 'Splatypus.Core.dll', 'README.md', 'LICENSE.txt', 'NOTICE.txt', 'Splatypus-Quick-Start.pdf')) { Copy-Item -LiteralPath (Join-Path $folder $name) -Destination $zipStage }
        Copy-Item -LiteralPath (Join-Path $examples 'Splatypus_Workflow.gh') -Destination (Join-Path $zipStage 'Examples')
        $files = @(Get-ChildItem -LiteralPath $zipStage | ForEach-Object FullName)
        Compress-Archive -LiteralPath $files -DestinationPath (Join-Path 'dist' $zip) -Force
    }
    & (Join-Path (Split-Path -Parent $PSScriptRoot) 'Tests/VerifyDistributionSdk.ps1')
} finally {
    Pop-Location
}
