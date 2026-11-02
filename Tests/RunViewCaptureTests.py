# Run in a separate Rhino instance with _-RunPythonScript.
import clr
import Rhino
from System import Array, String, Object, Console
from System.IO import StringWriter, File, Directory, Path
from System.Reflection import Assembly
clr.AddReference("System.Windows.Forms")
from System.Windows.Forms import Timer

root = Path.GetDirectoryName(Path.GetDirectoryName(__file__))
report = Path.Combine(root, "bin", "ViewCapture", "rhino-test-report.txt")
Directory.CreateDirectory(Path.GetDirectoryName(report))
File.WriteAllText(report, "Starting native viewport capture tests.\n")
Assembly.LoadFrom(Path.Combine(root, "bin", "ViewCapture", "tests", "Splatypus.Core.dll"))
Rhino.RhinoApp.RunScript("_Grasshopper", False)
clr.AddReference("Grasshopper")
# Run outside RunPythonScript: RunScript's synchronous/asynchronous behavior
# differs inside a script-runner command and during normal Grasshopper use.
timer = Timer()
timer.Interval = 1000
def run_tests(sender, event):
    timer.Stop()
    original = Console.Out
    capture = StringWriter()
    try:
        Console.SetOut(capture)
        assembly = Assembly.LoadFrom(Path.Combine(root, "bin", "ViewCapture", "tests", "RhinoSplat.ViewCapture.SmokeTest.dll"))
        assembly.EntryPoint.Invoke(None, Array[Object]([Array[String]([Path.Combine(root, "bin", "ViewCapture", "validation"), "--in-rhino"])]))
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
        timer.Dispose()
timer.Tick += run_tests
timer.Start()
