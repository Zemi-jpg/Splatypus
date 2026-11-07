# Splatypus

Photogrammetry and Gaussian splat training for **Grasshopper in Rhino 8**.

Import photos or video with FFmpeg, reconstruct cameras with COLMAP, and train Gaussian splats with Brush. Align reconstructions, preview point clouds, replay cameras, and capture Rhino scenes in one local workflow.

[Download v0.2.0](https://github.com/Zemi-jpg/Splatypus/releases/tag/v0.2.0) · [Installation](Documentation/Installation.md) · [User guide](Documentation/User-guide.md) · [food4Rhino](https://www.food4rhino.com/en/app/splatypus)

![Splatypus workflow in Grasshopper](Publication/assets/splatypus-hero-1600x900.png)

## Get started

1. Download the **Windows** or **macOS** ZIP from [Releases](https://github.com/Zemi-jpg/Splatypus/releases/tag/v0.2.0).
2. In Grasshopper, open **File → Special Folders → Components Folder**, then close Rhino.
3. Extract the ZIP into a Splatypus subfolder, keeping **Splatypus.gha** and **Splatypus.Core.dll** together. Keep one installed copy and restart Rhino.
4. Open the included [workflow example](Examples/Splatypus_Workflow.gh) with the solver disabled, set your paths, and follow the [user guide](Documentation/User-guide.md).

Requires **Rhino 8.34.26223.11001 or newer**, Grasshopper, and **.NET 8**. FFmpeg, COLMAP, and Brush are installed separately; **SP Download Tools** supports Windows x64 and Apple Silicon. Brush training requires a compatible GPU. Native macOS testing of the new interactive workflows remains outstanding; see [validation](Publication/Validation-report.md).

Splat Preview shows colored point centers. Gaussian splat viewport rendering and mesh reconstruction are not included in the release packages.

## Explore

- [Documentation](Documentation/README.md) — installation, workflows, features, and troubleshooting.
- [Quick-start PDF](Publication/Splatypus-Quick-Start.pdf) and [example notes](Examples/README.md).
- [What's new in 0.2.0](Publication/Update-log-0.2.0.md) and [changelog](CHANGELOG.md).
- [Source](Source/) · [Build and install scripts](Scripts/) · [Development guide](Documentation/Development.md).

Built by [Zeming Li](https://zeming.li/). [MIT License](LICENSE.txt) · [Third-party notices](THIRD-PARTY-NOTICES.md) · [Report an issue](https://github.com/Zemi-jpg/Splatypus/issues).
