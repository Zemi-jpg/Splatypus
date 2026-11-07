# Scripts

Run these from the repository root. Close Rhino before running an installer.

| Script | Purpose |
| --- | --- |
| `Build-Mac.sh` | Build the macOS plugin into `dist/macos` |
| `Install-Mac.sh` | Install a local Mac build into a supplied Grasshopper Libraries folder |
| `Install-Splatypus.ps1` | Install a local Windows build, backing up the previous plugin |
| `Package-Splatypus.ps1` | Build manual workflow ZIPs for both platforms |
| `Run-SmokeTest.ps1` | Compile and run the original standalone Core smoke test |
| Other `Install-*.ps1` scripts | Historical development installers for specific feature builds |

```powershell
./Scripts/Run-SmokeTest.ps1
```

```sh
bash Scripts/Build-Mac.sh
bash Scripts/Install-Mac.sh "/absolute/path/to/Grasshopper/Libraries"
```

For versioned release packaging, use [Publication/Build-Release.ps1](../Publication/Build-Release.ps1). See the [development guide](../Documentation/Development.md).
