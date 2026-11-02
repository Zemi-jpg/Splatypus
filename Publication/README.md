# Splatypus publication kit

Prepared release: **0.2.0**, 2 November 2026. MIT License, copyright (c) 2026 Zeming Li.

Support: [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com). Website: [zeming.li](https://zeming.li/).

The installation archives are under `dist/publication`: separate Windows and macOS Yak packages and manual ZIPs, with `SHA256SUMS.txt` and `release-inventory.json`. The four 0.1.0 archives remain available there unchanged. Only the current release is listed in the active checksums and inventory.

Every installable package retains the 0.1.0 file layout: matched plugin/Core libraries, README.md, Splatypus-Quick-Start.pdf, Examples/Splatypus_Workflow.gh, icon.png, manifest.yml, LICENSE.txt and NOTICE.txt. The example is the publisher-supplied SP_020 template, copied unchanged. The package README contains the full updated workflow manual; the PDF is an eight-page quick start.

[Validation report](Validation-report.md) records build, template, archive and test results. [Update log](Update-log-0.2.0.md) contains the short publication copy. Packaging is complete; uploads remain publisher actions.

To regenerate, run `Publication/Build-Release.ps1 -Platforms windows,macos`. It builds both targets, creates the four archives and runs `Verify-Release.ps1`. Regenerate the PDF with `Publication/Generate-guide.py` before building if its content changes.

The repository landing page and existing listing artwork are maintained separately from the README shipped inside the packages. The older `dist/Splatypus-Publication-Kit.zip`, if present, is a 0.1.0 artifact and is not the 0.2.0 installer.
