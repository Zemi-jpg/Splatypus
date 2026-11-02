# Run in an isolated Rhino test instance, never in the user's working document.
import clr
import Rhino
from System import Array, String, Object, Console
from System.IO import StringWriter, File, Directory, Path
from System.Reflection import Assembly

root = Path.GetDirectoryName(Path.GetDirectoryName(__file__))
report = Path.Combine(root, "bin", "VideoPlayback", "rhino-test-report.txt")
Directory.CreateDirectory(Path.GetDirectoryName(report))
Rhino.RhinoApp.RunScript("_Grasshopper", False)
clr.AddReference("Grasshopper")
original = Console.Out
capture = StringWriter()
try:
    Console.SetOut(capture)
    assembly = Assembly.LoadFrom(Path.Combine(Path.GetTempPath(), "RhinoSplatBuild",
        "RhinoSplat.VideoPlaybackRhino.SmokeTest", "bin", "Release", "net8.0-windows",
        "RhinoSplat.VideoPlaybackRhino.SmokeTest.dll"))
    assembly.EntryPoint.Invoke(None, Array[Object]([Array[String]([])]))
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
