# Package validation report

Splatypus 0.1.0. Updated 2026-10-11.

| Check | Result |
| --- | --- |
| Windows/macOS release builds | Matched GHA/Core libraries; .NET 8 |
| Host SDK references | Grasshopper and RhinoCommon 8.34.26223.11001 |
| Assembly version | 0.1.0.0 in both libraries |
| Informational/package version | 0.1.0 |
| Package identity | Splatypus; existing library GUID retained |
| Embedded icons | 21 component resources per GHA |
| Included example | Current component IDs; packaged bytes match Examples/Splatypus_Workflow.gh |
| Documentation | One README and five-page quick-start PDF per installable package |
| Copyright and license | MIT, copyright 2026 Zeming Li |
| Contact | lizeming2021@gmail.com; https://zeming.li/ |
| Payload consistency | Yak/manual ZIP bytes compared; explicit file allowlist |
| Checksums | SHA-256 inventory for final packages |
| Windows local installation | Successful installation reported by the publisher |

Automated verification covers builds and archive consistency. Historical test records are retained separately in the local build archive; this report does not claim additional native Rhino workflow execution.
