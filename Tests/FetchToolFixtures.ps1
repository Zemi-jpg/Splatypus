$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot '..\bin\DownloadToolsVerification\archives'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$assets = @(
    @('colmap-cuda.zip', 'https://github.com/colmap/colmap/releases/download/3.11.1/colmap-x64-windows-cuda.zip'),
    @('colmap-cpu.zip', 'https://github.com/colmap/colmap/releases/download/3.11.1/colmap-x64-windows-nocuda.zip'),
    @('brush.zip', 'https://github.com/ArthurBrussee/brush/releases/download/v0.3.0/brush-app-x86_64-pc-windows-msvc.zip'),
    @('ffmpeg.zip', 'https://github.com/GyanD/codexffmpeg/releases/download/8.0/ffmpeg-8.0-essentials_build.zip')
)
foreach ($asset in $assets) {
    $path = Join-Path $folder $asset[0]
    if (-not (Test-Path -LiteralPath $path)) {
        Invoke-WebRequest -Uri $asset[1] -OutFile $path
    }
    Get-FileHash -LiteralPath $path -Algorithm SHA256 | Select-Object Path,Hash
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try { $archive.Entries | Where-Object { $_.Name -match '^(ffmpeg|ffprobe|colmap|brush.*)\.exe$' } | Select-Object FullName,Length }
    finally { $archive.Dispose() }
}
$release = Invoke-RestMethod 'https://api.anaconda.org/release/conda-forge/ffmpeg/7.1.1'
$release.distributions | Where-Object { $_.attrs.subdir -in @('osx-arm64','osx-64') } | Select-Object basename,version | ConvertTo-Json

$mamba = Join-Path $folder 'micromamba.exe'
if (-not (Test-Path -LiteralPath $mamba)) {
    Invoke-WebRequest 'https://github.com/mamba-org/micromamba-releases/releases/download/2.3.3-0/micromamba-win-64.exe' -OutFile $mamba
}
if ((Get-FileHash -LiteralPath $mamba).Hash -ne '90085767d811e08a0fe240f7cb7a713915e1e5f24e3b0acfad9b37daa64c98fe') { throw 'Micromamba checksum mismatch' }
$env:CONDA_OVERRIDE_OSX = '14.0'
foreach ($spec in @('ffmpeg=7.1.1','colmap=3.11.1')) {
    $prefix = Join-Path $folder ('solve-' + $spec.Split('=')[0])
    & $mamba --no-rc --root-prefix (Join-Path $folder 'mamba-cache') create --yes --dry-run --json --prefix $prefix --override-channels --channel https://conda.anaconda.org/conda-forge --strict-channel-priority --platform osx-arm64 $spec | Set-Content -LiteralPath ($prefix + '.json')
    if ($LASTEXITCODE -ne 0) { throw "macOS package solve failed: $spec" }
    Write-Output "macOS dependency solve passed: $spec"
}
$macBrush = Join-Path $folder 'brush-macos.tar.xz'
if (-not (Test-Path -LiteralPath $macBrush)) {
    Invoke-WebRequest 'https://github.com/ArthurBrussee/brush/releases/download/v0.3.0/brush-app-aarch64-apple-darwin.tar.xz' -OutFile $macBrush
}
if ((Get-FileHash -LiteralPath $macBrush).Hash -ne '65b2631398c839be3c1d4d7160fe2326389dec87830aac0710985e6690a1048c') { throw 'Mac Brush checksum mismatch' }
tar -tf $macBrush
if ($LASTEXITCODE -ne 0) { throw 'Could not list Mac Brush archive' }
