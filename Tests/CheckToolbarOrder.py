# Checks the actual Grasshopper ribbon after the updated plugin is installed.
import clr
import Rhino
from System.IO import File, Directory, Path

report = r"C:\Users\basar\RhinoSplatDev\bin\ToolbarOrder\toolbar-report.txt"
expected = [
    ("01 Setup", ["SP Download Tools", "SP Hello", "SP Project"]),
    ("02 dataset", ["SP FFmpeg Setup", "SP Media Import"]),
    ("03 COLMAP", ["SP COLMAP Export Model", "SP COLMAP Feature Extract", "SP COLMAP Match", "SP COLMAP Pipeline", "SP COLMAP Setup", "SP COLMAP Sparse Reconstruct", "SP COLMAP Undistort Images"]),
    ("04 Realignment", ["SP Align COLMAP Model", "SP Camera Planes", "SP Geometry Translator", "SP Quick Align", "SP Sparse Point Cloud"]),
    ("05 Training", ["SP Active Dataset", "SP Brush Dataset", "SP Brush Train"]),
    ("06 Preview", ["SP Auto Background", "SP Splat Preview", "SP Video Frames", "SP Video Playback", "SP View Capture"])
]
lines = []
try:
    Rhino.RhinoApp.RunScript("_Grasshopper", False)
    clr.AddReference("Grasshopper")
    import Grasshopper
    def controls(parent):
        for child in parent.Controls:
            yield child
            for descendant in controls(child):
                yield descendant
    ribbon = next(c for c in controls(Grasshopper.Instances.DocumentEditor)
                  if c.GetType().FullName == "Grasshopper.GUI.Ribbon.GH_Ribbon")
    tab = next(t for t in ribbon.Tabs if t.NameFull == "Splatypus")
    actual = [(str(panel.Name), [str(item.Proxy.Desc.Name) for item in panel.AllItems]) for panel in tab.Panels]
    for name, names in actual:
        lines.append(name + ": " + " -> ".join(names))
    if actual != expected:
        raise Exception("Ribbon order differs from the requested list.")
    lines.append("PASS: all 25 visible components are alphabetical across six groups; Model Report is absent.")
except Exception as error:
    lines.append("FAILED: " + str(error))
finally:
    Directory.CreateDirectory(Path.GetDirectoryName(report))
    File.WriteAllText(report, "\n".join(lines))
    Rhino.RhinoApp.WriteLine(lines[-1])
