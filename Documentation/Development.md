# Developing Splatypus

## Release baseline

The `v0.1.0` tag preserves the source, documentation and package files corresponding to the first food4Rhino release dated 11 October 2026. Downloads in the repository README point to this tag so later development does not change those links.

Start updates on a feature branch from `main`. Commit related changes together, run the relevant checks, and merge reviewed changes back to `main`. Use a new version and a new annotated tag for each published release; do not move an existing release tag or replace its package files.

The 0.1.0 projects explicitly exclude the experimental mesh components. Development experiments, generated reviews and local caches are not part of the release baseline.

## Build from source

Install the .NET 8 SDK. From the repository root:

```powershell
dotnet build RhinoSplat.Grasshopper.csproj -c Release
dotnet build RhinoSplat.Grasshopper.Mac.csproj -c Release
```

The projects pin RhinoCommon and Grasshopper SDK version 8.34.26223.11001 through NuGet. The Mac build is cross-compiled; native reconstruction and training must be checked on macOS separately. Build outputs use the temporary build root configured in Directory.Build.props.

## Verify and package

Verify the existing 0.1.0 downloads without rebuilding them:

```powershell
./Publication/Verify-Release.ps1 -ReleaseFolder ./dist/publication
```

This checks hashes, file allowlists, assembly versions, Rhino SDK references, embedded icons, example/manual freshness and matching Yak/ZIP payloads.

For a new release, update `Publication/release.json`, the defaults in `Publication/Splatypus.Release.props`, `CHANGELOG.md`, the repository README and package documentation together. With Rhino's Yak CLI installed, build both platforms in PowerShell 7:

```powershell
./Publication/Build-Release.ps1 -Platforms windows,macos
```

`Publication/Package-README.md` is the complete installation manual copied to each package as `README.md`. It is intentionally separate from the GitHub landing page. Its relative links are resolved inside an extracted package. Preserve the historical 0.1.0 archives; publish new builds under a new version.

See [the publishing guide](../Publication/Publish-guide.md) for Yak and food4Rhino steps and [the validation report](../Publication/Validation-report.md) for the scope of checks. Native Rhino installation, reconstruction and training remain separate integration checks.

## Repository contents

| Folder | Purpose |
| --- | --- |
| `Assets/` | Plugin icons and editable artwork |
| `Documentation/` | User and developer guides |
| `Examples/` | Downloadable Grasshopper definition |
| `Publication/` | Package manual, quick-start PDF, listing artwork and build scripts |
| `Tests/` | Existing smoke tests and distribution checks |
| `dist/publication/` | Versioned Yak/ZIP downloads and checksums |

Local SDK caches, telemetry, generated review images, publication-kit duplicates and temporary experiments are excluded from Git. The initial historical commit remains available; cleanup of the current tree does not rewrite repository history.
