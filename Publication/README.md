# Splatypus publication kit

First public release: **0.1.0**. MIT License, copyright (c) 2026 Zeming Li.

Support: [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com). Website: [zeming.li](https://zeming.li/).

The kit contains the complete README, five-page quick-start PDF, Grasshopper example, Food4Rhino listing copy, copyright notices, artwork and final Windows/macOS Yak and manual ZIP packages with SHA-256 checksums.

Use [Publish-guide.md](Publish-guide.md) for upload steps and [Asset-register.md](Asset-register.md) for image dimensions and captions. Packages are under dist/publication. Each installable package includes one README.md, the PDF and Examples/Splatypus_Workflow.gh.

To regenerate from the source checkout, run `Publication/Build-Release.ps1 -Platforms windows,macos`, then `Package-Splatypus.ps1` and `Publication/Create-publication-bundle.ps1`.

The repository landing page is maintained separately from Package-README.md, the frozen manual shipped as README.md inside the 0.1.0 packages. The listing is live at https://www.food4rhino.com/en/app/splatypus (11 October 2026). See ../Documentation/Development.md for version-control and release guidance.
