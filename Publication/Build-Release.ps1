[CmdletBinding()]
param(
    [ValidateSet('windows', 'macos')][string[]]$Platforms = @('windows'),
    [string]$YakPath = 'C:\Program Files\Rhino 8\System\Yak.exe'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release.json') -Raw | ConvertFrom-Json
if ($config.packageName -ne 'Splatypus') { throw 'Package name must match GH_AssemblyInfo.Name: Splatypus.' }
if ($config.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'Use a semantic release version, for example 0.1.0.' }
if ($config.assemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'assemblyVersion must have four numeric parts.' }
$selectedPlatforms = @($Platforms | Select-Object -Unique)
$missing = [Collections.Generic.List[string]]::new()
foreach ($field in @('author', 'copyrightHolder', 'supportContact')) {
    if ([string]::IsNullOrWhiteSpace($config.$field)) { $missing.Add($field) }
}
if (!$config.identityAndRightsConfirmed) { $missing.Add('identityAndRightsConfirmed') }
if ($config.licenseChoice -ne 'MIT') { throw 'The current release is licensed under MIT.' }
if (!$config.publicationApproved) { $missing.Add('publicationApproved') }
if ($missing.Count) { throw "Publication details are missing: $($missing -join ', ')." }
foreach ($field in @('author', 'copyrightHolder', 'supportContact', 'website')) {
    if ([string]$config.$field -match '[\r\n]') { throw "$field must be one line." }
}
if ($config.website) {
    $uri = $null
    if (![Uri]::TryCreate($config.website, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') { throw 'website must be an absolute HTTPS URL.' }
}
if (!(Test-Path -LiteralPath $YakPath -PathType Leaf)) { throw "Rhino Yak CLI not found: $YakPath" }
$destination = Join-Path $root 'dist/publication'
$work = Join-Path $root ('bin/publication-work/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $destination, $work -Force | Out-Null
$required = @('Publication/Package-README.md')
$exampleFiles = @('Examples/Splatypus_Workflow.gh')
$pdf = Join-Path $PSScriptRoot 'Splatypus-Quick-Start.pdf'
$icon = Join-Path $PSScriptRoot 'assets/splatypus-icon-64.png'
foreach ($path in @($pdf, $icon) + @(($required + $exampleFiles) | ForEach-Object { Join-Path $root $_ })) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required release file missing: $path" }
}
$legalText = Get-Content -LiteralPath (Join-Path $root 'LICENSE.txt') -Raw
$expectedLegalText = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'legal/MIT.txt') -Raw).Replace('{{YEAR}}', [string]$config.copyrightYear).Replace('{{HOLDER}}', [string]$config.copyrightHolder)
if ($legalText.Replace("`r`n", "`n").Trim() -ne $expectedLegalText.Replace("`r`n", "`n").Trim()) { throw 'Repository LICENSE.txt must match the selected MIT copyright details.' }
if ($legalText -match '\{\{[^}]+\}\}') { throw 'License contains unresolved fields.' }
function Escape-MSBuild([string]$value) {
    $value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
}
$artifactPaths = [Collections.Generic.List[string]]::new()
$records = [Collections.Generic.List[object]]::new()
$env:DOTNET_CLI_HOME = Join-Path $root 'bin/publication-cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
Push-Location $root
try {
    foreach ($platform in $selectedPlatforms) {
        $project = if ($platform -eq 'windows') { 'RhinoSplat.Grasshopper.csproj' } else { 'RhinoSplat.Grasshopper.Mac.csproj' }
        $tag = if ($platform -eq 'windows') { 'win' } else { 'mac' }
        $build = Join-Path $work "$platform-build"
        $stage = Join-Path $work "$platform-stage"
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        $buildArgs = @('build', $project, '-c', 'Release', '-o', $build,
            "-p:Version=$($config.version)", "-p:InformationalVersion=$($config.version)",
            "-p:AssemblyVersion=$($config.assemblyVersion)", "-p:FileVersion=$($config.assemblyVersion)",
            ('-p:Company=' + (Escape-MSBuild ([string]$config.author))),
            ('-p:SplatypusSupportContact=' + (Escape-MSBuild ([string]$config.supportContact))),
            ('-p:PackageProjectUrl=' + (Escape-MSBuild ([string]$config.website))),
            '-p:NuGetAudit=false')
        if ($config.copyrightHolder) { $buildArgs += ('-p:Copyright=' + (Escape-MSBuild "Copyright (c) $($config.copyrightYear) $($config.copyrightHolder)")) }
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "$platform release build failed." }
        foreach ($name in @('Splatypus.gha', 'Splatypus.Core.dll')) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination $stage }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Package-README.md') -Destination (Join-Path $stage 'README.md')
        $stagedExamples = Join-Path $stage 'Examples'
        New-Item -ItemType Directory -Path $stagedExamples | Out-Null
        foreach ($file in $exampleFiles) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stagedExamples }
        Copy-Item -LiteralPath $pdf -Destination (Join-Path $stage 'Splatypus-Quick-Start.pdf')
        Copy-Item -LiteralPath $icon -Destination (Join-Path $stage 'icon.png')
        Set-Content -LiteralPath (Join-Path $stage 'LICENSE.txt') -Value $legalText -Encoding utf8
        $notice = "Splatypus $($config.version)`nAuthor: $($config.author)`n"
        if ($config.copyrightHolder) { $notice += "Copyright (c) $($config.copyrightYear) $($config.copyrightHolder).`n" }
        $notice += "License: $($config.licenseChoice); see LICENSE.txt.`nSupport: $($config.supportContact)`nThird-party software is separately licensed; see README.md, Licenses and credits.`n"
        if ($config.website) { $notice += "Website: $($config.website)`n" }
        Set-Content -LiteralPath (Join-Path $stage 'NOTICE.txt') -Value $notice -Encoding utf8
        # JSON string escaping is valid for YAML double-quoted scalars; no shell interpolation.
        $authorYaml = ConvertTo-Json -InputObject ([string]$config.author) -Compress
        $manifest = @('name: Splatypus', "version: $($config.version)", 'authors:', "  - $authorYaml",
            'description: >', '  Photogrammetry and Gaussian splat training workflows for Grasshopper in Rhino 8.34+',
            '  with .NET 8. Connect FFmpeg import, COLMAP reconstruction, three-point alignment',
            '  and Brush training. Includes camera playback, calibrated Rhino scene capture,',
            '  automatic backgrounds and colored point-cloud preview.',
            '  External tools are installed separately. A Gaussian splat renderer is not included.')
        $manifest += 'icon: icon.png'
        if ($config.website) { $manifest += ('url: ' + (ConvertTo-Json -InputObject ([string]$config.website) -Compress)) }
        $manifest += @('keywords:', '  - grasshopper', '  - photogrammetry', '  - gaussian-splatting', '  - reconstruction', '  - point-cloud', '  - COLMAP', '  - Brush', '  - FFmpeg', '  - guid:7dca094a-626a-4b5b-ae1c-a626641fcf3c')
        Set-Content -LiteralPath (Join-Path $stage 'manifest.yml') -Value ($manifest -join "`n") -Encoding utf8
        Push-Location $stage
        try {
            & $YakPath build --platform $tag
            if ($LASTEXITCODE -ne 0) { throw "$platform Yak build failed." }
        } finally { Pop-Location }
        $builtPackages = @(Get-ChildItem -LiteralPath $stage -Filter '*.yak' -File)
        if ($builtPackages.Count -ne 1) { throw 'Expected exactly one Yak package.' }
        $yak = $builtPackages[0]
        if ($yak.Name -notmatch ('(?i)^splatypus-' + [regex]::Escape($config.version) + '-rh8_34-' + $tag + '\.yak$')) { throw "Unexpected compatibility tag: $($yak.Name)" }
        $yakDestination = Join-Path $destination $yak.Name
        Copy-Item -LiteralPath $yak.FullName -Destination $yakDestination -Force
        $artifactPaths.Add($yakDestination)
        $zipDestination = Join-Path $destination "Splatypus-$($config.version)-$platform.zip"
        # The only generated file not belonging to a manual ZIP is the Yak archive itself.
        $zipInputs = @(Get-ChildItem -LiteralPath $stage | Where-Object Extension -NE '.yak' | ForEach-Object FullName)
        Compress-Archive -LiteralPath $zipInputs -DestinationPath $zipDestination -Force
        $artifactPaths.Add($zipDestination)
        $records.Add([ordered]@{ platform = $platform; yak = $yak.Name; zip = [IO.Path]::GetFileName($zipDestination); assemblyVersion = $config.assemblyVersion; tag = "rh8_34-$tag" })
    }
    $hashes = @($artifactPaths | ForEach-Object { $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(); "$hash  $([IO.Path]::GetFileName($_))" })
    Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Value $hashes -Encoding ascii
    $inventory = [ordered]@{ package = $config.packageName; version = $config.version; author = $config.author;
        copyrightHolder = $config.copyrightHolder; license = $config.licenseChoice; support = $config.supportContact; website = $config.website;
        minimumRhinoSdk = '8.34.26223.11001'; runtime = '.NET 8';
        example = [ordered]@{ path = 'Examples/Splatypus_Workflow.gh'; source = 'SP_020';
            sha256 = (Get-FileHash -LiteralPath (Join-Path $root 'Examples/Splatypus_Workflow.gh') -Algorithm SHA256).Hash.ToLowerInvariant() };
        artifacts = @($records); externalToolBinariesBundled = $false; hostSdkBinariesBundled = $false }
    Set-Content -LiteralPath (Join-Path $destination 'release-inventory.json') -Value ($inventory | ConvertTo-Json -Depth 6) -Encoding utf8
    & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ReleaseFolder $destination
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Release verification failed.' }
    Write-Output "Prepared publication artifacts: $destination"
} finally { Pop-Location }
