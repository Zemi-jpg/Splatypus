# Run with Rhino's _-RunPythonScript command in a separate test instance.
# The harness creates and disposes its own test document.
import clr
import Rhino
from System import Array, String, Object, Console
from System.IO import StringWriter, File, Directory, Path
from System.Reflection import Assembly

root = Path.GetDirectoryName(Path.GetDirectoryName(__file__))
interactive = globals().get("QUICK_ALIGN_INTERACTIVE", False)
report = Path.Combine(root, "bin", "QuickAlign", "interactive-test-report.txt" if interactive else "rhino-test-report.txt")
Directory.CreateDirectory(Path.GetDirectoryName(report))
Rhino.RhinoApp.RunScript("_Grasshopper", False)
clr.AddReference("Grasshopper")
original = Console.Out
capture = StringWriter()
try:
    Console.SetOut(capture)
    assembly = Assembly.LoadFrom(Path.Combine(Path.GetTempPath(), "RhinoSplatBuild",
        "RhinoSplat.QuickAlignRhino.SmokeTest", "bin", "Release", "net8.0-windows",
        "RhinoSplat.QuickAlignRhino.SmokeTest.dll"))
    test_args = ["--in-rhino", "--interactive"] if interactive else ["--in-rhino"]
    assembly.EntryPoint.Invoke(None, Array[Object]([Array[String](test_args)]))
    File.WriteAllText(report, capture.ToString())
except Exception as error:
    details = str(error)
    managed = getattr(error, "clsException", error)
    while managed is not None:
        details += "\n" + str(managed)
        managed = getattr(managed, "InnerException", None)
    File.WriteAllText(report, capture.ToString() + "\nFAILED: " + details)
finally:
    Console.SetOut(original)
    capture.Dispose()
