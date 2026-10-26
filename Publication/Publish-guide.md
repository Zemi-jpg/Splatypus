# Publish Splatypus

## Build the packages

From the source checkout:

```powershell
.\Publication\Build-Release.ps1 -Platforms windows,macos
.\Package-Splatypus.ps1
.\Publication\Create-publication-bundle.ps1
```

Final packages are under dist/publication. The GHA and Core library are rebuilt together. Each target uses a clean staging directory and explicit file list. External tools and Rhino host assemblies are excluded.

The name is Splatypus, matching GH_AssemblyInfo.Name. Package keywords include its existing GUID, 7dca094a-626a-4b5b-ae1c-a626641fcf3c, for Grasshopper package restore. The 8.34 SDK produces `rh8_34-win` / `rh8_34-mac` tags. Never rename them to rh8_0: that would advertise an unsupported minimum service release. Separate GHA binaries require separate platform packages, not an any package.

## Local installation

Close Rhino before installation. Choose Yak or manual installation and keep one installed copy. Use Rhino's Yak tool to install an absolute local package path:

```powershell
& 'C:\Program Files\Rhino 8\System\Yak.exe' install 'C:\absolute\path\Splatypus-0.1.0-rh8_34-win.yak'
```

This changes that machine's Rhino package installation. Restart Rhino and confirm SP Hello reports Ready. On macOS the CLI is `/Applications/Rhino 8.app/Contents/Resources/bin/yak`.

## Public server and Food4Rhino

Check name availability immediately before first upload:

```powershell
& 'C:\Program Files\Rhino 8\System\Yak.exe' search --all Splatypus
```

Log in with the intended Rhino Account, then push the final package:

```powershell
& 'C:\Program Files\Rhino 8\System\Yak.exe' login
& 'C:\Program Files\Rhino 8\System\Yak.exe' push 'C:\absolute\path\Splatypus-0.1.0-rh8_34-win.yak'
& 'C:\Program Files\Rhino 8\System\Yak.exe' search --all Splatypus
```

The first uploader becomes owner. Versions cannot be overwritten/deleted; release a new version for corrections, or use yak yank to unlist an incorrect version. Protect account/token files and never include them in this kit.

Create the Food4Rhino page from the Yak package or link an existing page. Use Food4Rhino-listing.md and Asset-register.md. Select the matching version, platform and MIT license fields and accept the submission terms in your publisher account.

## Releases after the first publication

Edit release.json version/assemblyVersion and release notes, rebuild both matching libraries, rerun relevant native checks and publish a new package version. Keep the package version, assembly metadata and documentation synchronized.

Sources: [manifest](https://developer.rhino3d.com/guides/yak/the-package-manifest/), [Grasshopper packages](https://developer.rhino3d.com/guides/yak/creating-a-grasshopper-plugin-package/), [publishing/ownership](https://developer.rhino3d.com/guides/yak/pushing-a-package-to-the-server/), [CLI](https://developer.rhino3d.com/guides/yak/yak-cli-reference/).
