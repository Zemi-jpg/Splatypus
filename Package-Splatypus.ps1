$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build RhinoSplat.Grasshopper.csproj -c Release -o dist/windows
    if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
    dotnet build RhinoSplat.Grasshopper.Mac.csproj -c Release -o dist/macos
    if ($LASTEXITCODE -ne 0) { throw 'macOS build failed' }
    Copy-Item -LiteralPath 'Mac-workflow.md' -Destination 'dist/macos/Mac-workflow.md' -Force
    foreach ($platform in @('windows', 'macos')) {
        $folder = Join-Path 'dist' $platform
        Copy-Item -LiteralPath 'Download-tools.md' -Destination (Join-Path $folder 'Download-tools.md') -Force
        Copy-Item -LiteralPath 'Colmap-pipeline.md' -Destination (Join-Path $folder 'Colmap-pipeline.md') -Force
        $guide = if ($platform -eq 'windows') { 'README-Windows.md' } else { 'Mac-workflow.md' }
        $zip = if ($platform -eq 'windows') { 'Splatypus-Windows-workflow.zip' } else { 'Splatypus-macOS-workflow.zip' }
        $files = @('Splatypus.gha', 'Splatypus.Core.dll', $guide, 'Download-tools.md', 'Colmap-pipeline.md') | ForEach-Object { Join-Path $folder $_ }
        Compress-Archive -LiteralPath $files -DestinationPath (Join-Path 'dist' $zip) -Force
    }
    & (Join-Path $PSScriptRoot 'Tests/VerifyDistributionSdk.ps1')
} finally {
    Pop-Location
}
