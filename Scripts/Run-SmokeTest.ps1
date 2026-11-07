$ErrorActionPreference = 'Stop'

$dotnetRoot = Join-Path $env:ProgramFiles 'dotnet'
$sdk = Get-ChildItem (Join-Path $dotnetRoot 'sdk') -Directory |
    Where-Object Name -like '8.0.*' |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

$referencePack = Get-ChildItem (Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref') -Directory |
    Where-Object Name -like '8.0.*' |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

if ($null -eq $sdk -or $null -eq $referencePack) {
    throw 'A .NET 8 SDK and reference pack are required.'
}

$csc = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$referenceDirectory = Join-Path $referencePack.FullName 'ref\net8.0'
$output = Join-Path $env:TEMP 'RhinoSplat.Core.SmokeTest.dll'
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'Tests/CoreSmokeTest.cs'
$runtimeConfig = Join-Path (Split-Path -Parent $PSScriptRoot) 'Tests/RhinoSplat.Core.SmokeTest.runtimeconfig.json'

$references = Get-ChildItem $referenceDirectory -Filter '*.dll' |
    ForEach-Object { '/reference:' + $_.FullName }

$compilerArguments = @(
    $csc,
    '/noconfig',
    '/nostdlib',
    '/target:exe',
    '/langversion:latest',
    ('/out:' + $output)
) + $references + @($source)

& dotnet @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

& dotnet exec --runtimeconfig $runtimeConfig $output
if ($LASTEXITCODE -ne 0) {
    throw "Smoke test failed with exit code $LASTEXITCODE."
}
