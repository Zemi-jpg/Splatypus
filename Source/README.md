# Source

| Folder | Contents |
| --- | --- |
| `Core/` | Project data, external tool runners, alignment, and processing |
| `Grasshopper/` | Grasshopper components and Rhino viewport integration |
| `Renderer/` | Experimental renderer source, excluded from release packages |

Build from the repository root with the .NET 8 SDK:

```powershell
dotnet build Source/RhinoSplat.Grasshopper.csproj -c Release
dotnet build Source/RhinoSplat.Grasshopper.Mac.csproj -c Release
```

See the [development guide](../Documentation/Development.md) for tests and packaging.
