# Run through Rhino's _-RunPythonScript. Uses a temporary floating viewport and
# a temporary GH document; the active model and GH document are preserved.
import clr
import Rhino
from System import Array, String, Object, Console
from System.IO import File, Path, StringWriter
from System.Reflection import Assembly
clr.AddReference("System.Windows.Forms")
from System.Windows.Forms import Timer

root = Path.GetDirectoryName(Path.GetDirectoryName(__file__))
report = Path.Combine(root, "bin", "AutoBackground", "rhino-test-report.txt")
File.WriteAllText(report, "Waiting for the Rhino script command to finish.\n")
timer = Timer()
timer.Interval = 500
def run_tests(sender, event):
    timer.Stop()
    capture = StringWriter()
    original = Console.Out
    try:
        Console.SetOut(capture)
        assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(root, "bin", "AutoBackground", "tests", "RhinoSplat.AutoBackgroundRhino.SmokeTest.dll")))
        assembly.EntryPoint.Invoke(None, Array[Object]([Array[String](["--in-rhino", "--capture"])]))
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
    Rhino.RhinoApp.WriteLine("SP background checks finished. Report: " + report)
timer.Tick += run_tests
timer.Start()
