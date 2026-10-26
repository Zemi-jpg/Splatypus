# Third-party software and trademarks

Prepared from the source/dependency inventory on 2026-10-06. This document distinguishes host references, separately downloaded tools, and publication artwork. The publisher must confirm ownership of their own contributions before release.

## Package contents

The publication packager allowlists Splatypus.gha, Splatypus.Core.dll, Splatypus legal notices, documentation, and the Splatypus package icon. It excludes RhinoCommon.dll, Grasshopper.dll, System.Drawing.Common.dll, .NET runtime files, FFmpeg/COLMAP/Brush binaries, the experimental RhinoSplat.Renderer.rhp, Krill, and downloaded reference logos.

Rhino and Grasshopper supply the host SDK assemblies at runtime. The project references Grasshopper and RhinoCommon 8.34.26223.11001 and System.Drawing.Common 8.0.0 for compilation, with runtime assets excluded. These references do not transfer ownership or a license to redistribute the host software.

## Separate local applications

| Tool | Pinned version/source | Licensing scope |
| --- | --- | --- |
| FFmpeg + ffprobe, Windows | 8.0 essentials from Gyan's codexffmpeg release | FFmpeg is LGPL-2.1-or-later, with optional GPL parts changing the resulting build's terms. Use the archive's actual license/configuration; do not describe all FFmpeg binaries as LGPL. |
| FFmpeg + ffprobe, Apple Silicon | 7.1.1 from conda-forge | Recipe/build and dependencies determine terms. Installed package metadata/license files are authoritative for the resolved build. |
| COLMAP | 3.11.1 upstream Windows CPU/CUDA builds; conda-forge on Apple Silicon | COLMAP itself uses BSD-3-Clause, copyright 2023 ETH Zurich and UNC Chapel Hill. Dependencies are separately licensed and can affect the resulting binary. |
| Brush | 0.3.0 official Windows x64 / Apple Silicon releases | Apache-2.0 in the tagged upstream LICENSE. Retain applicable notices; dependencies remain separately licensed. |
| micromamba | 2.3.3 bootstrap on Apple Silicon | mamba project BSD-3-Clause; its shipped components/dependencies have their own notices. Bootstrap is downloaded separately and is not in the Yak/ZIP. |

Splatypus launches these tools as separate processes. Download Tools obtains upstream archives/packages directly on the user's machine and retains extracted supporting files. Splatypus does not relabel or relicense them. The macOS environment's transitive builds are resolved at installation time, so a static plugin inventory cannot identify every eventual package license.

Do not add external tool binaries to Food4Rhino downloads, Yak packages or mirrors without auditing each exact build, preserving complete licenses/NOTICE files and satisfying corresponding-source obligations where required. A link to a license summary alone does not satisfy binary redistribution obligations. Executable startup validation does not audit licensing.

## Sources and source-code links

- [FFmpeg legal and licensing](https://ffmpeg.org/legal.html); [FFmpeg source](https://ffmpeg.org/download.html#get-sources); [Gyan 8.0 build](https://github.com/GyanD/codexffmpeg/releases/tag/8.0).
- [COLMAP 3.11.1 license](https://github.com/colmap/colmap/blob/3.11.1/COPYING.txt), [tagged source](https://github.com/colmap/colmap/tree/3.11.1), [release](https://github.com/colmap/colmap/releases/tag/3.11.1).
- [Brush 0.3.0 Apache license](https://github.com/ArthurBrussee/brush/blob/v0.3.0/LICENSE), [tagged source](https://github.com/ArthurBrussee/brush/tree/v0.3.0), [release](https://github.com/ArthurBrussee/brush/releases/tag/v0.3.0).
- [mamba licensing](https://github.com/mamba-org/mamba/blob/main/LICENSE), [micromamba 2.3.3 release](https://github.com/mamba-org/micromamba-releases/releases/tag/2.3.3-0).
- [conda-forge FFmpeg feedstock](https://github.com/conda-forge/ffmpeg-feedstock), [COLMAP feedstock](https://github.com/conda-forge/colmap-feedstock).
- [RhinoCommon NuGet](https://www.nuget.org/packages/RhinoCommon/8.34.26223.11001), [Grasshopper NuGet](https://www.nuget.org/packages/Grasshopper/8.34.26223.11001), [System.Drawing.Common](https://www.nuget.org/packages/System.Drawing.Common/8.0.0).

## Artwork, references and trademarks

Publication artwork uses the existing editable Splatypus platypus mark and component icon masters in Assets/Icons-Splatypus. The asset README describes them as original interpretations of tool-family references. That design note is provenance, not proof of third-party permissions or trademark clearance.

Official/reference logos in outputs/official-icons, downloaded Krill packages and old workflow graphics containing official logos are not selected for publication. The new listing graphics use Splatypus artwork and text names for dependencies. No example photographs or scenes belonging to others are bundled.

Rhino, Rhinoceros, Grasshopper and Food4Rhino identify McNeel products/services. FFmpeg is a trademark of Fabrice Bellard. Other names/marks belong to their owners. References describe interoperability and do not imply endorsement, partnership or inclusion of a third-party renderer. The owner's Splatypus trademark/name clearance remains a publisher check.
