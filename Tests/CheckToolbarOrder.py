# Checks the actual Grasshopper ribbon after the updated plugin is installed.
import clr
import Rhino
from System.IO import File

report = r"C:\Users\basar\RhinoSplatDev\bin\ToolbarOrder\toolbar-report.txt"
expected = [
    ("01 Setup", ["Hello Splat", "RS Project", "RS Download Tools"]),
    ("02 dataset", ["RS FFmpeg Setup", "RS Media Import"]),
    ("03 COLMAP", ["RS COLMAP Setup", "RS COLMAP Feature Extract", "RS COLMAP Exhaustive Match", "RS COLMAP Sparse Reconstruct", "RS COLMAP Undistort Images", "RS COLMAP Export Model"]),
    ("04 Realignment", ["RS Sparse Point Cloud", "RS Camera Planes", "RS Geometry Translator", "RS Align COLMAP Model"]),
    ("05 Training", ["RS Training Dataset Check", "RS Brush Dataset", "RS Brush Train"]),
    ("06 Preview", ["RS Splat Preview"])
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
    tab = next(t for t in ribbon.Tabs if t.NameFull == "RhinoSplat")
    actual = [(str(panel.Name), [str(item.Proxy.Desc.Name) for item in panel.AllItems]) for panel in tab.Panels]
    for name, names in actual:
        lines.append(name + ": " + " -> ".join(names))
    if actual != expected:
        raise Exception("Ribbon order differs from the requested list.")
    lines.append("PASS: all visible components are in the requested order across six groups; Model Report is absent.")
except Exception as error:
    lines.append("FAILED: " + str(error))
finally:
    File.WriteAllText(report, "\n".join(lines))
    Rhino.RhinoApp.WriteLine(lines[-1])
