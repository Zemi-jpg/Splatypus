# Run through Rhino's _-RunPythonScript command; no document geometry is changed.
import clr
import Rhino
from System import Array, String, Object, Console
from System.IO import StringWriter, File, Directory
from System.Reflection import Assembly

report = r"C:\Users\basar\RhinoSplatDev\bin\GeometryTranslator\test-report.txt"
Directory.CreateDirectory(r"C:\Users\basar\RhinoSplatDev\bin\GeometryTranslator")
Rhino.RhinoApp.RunScript("_Grasshopper", False)
clr.AddReference("Grasshopper")
original_output = Console.Out
capture = StringWriter()
try:
    Console.SetOut(capture)
    assembly = Assembly.LoadFrom(r"C:\Users\basar\AppData\Local\Temp\RhinoSplatBuild\RhinoSplat.GeometryTranslator.SmokeTest\bin\Debug\net8.0-windows\RhinoSplat.GeometryTranslator.SmokeTest.dll")
    assembly.EntryPoint.Invoke(None, Array[Object]([Array[String](["--in-rhino"])]))
    File.WriteAllText(report, capture.ToString())
    Rhino.RhinoApp.WriteLine("Geometry Translator checks passed. " + report)
except Exception as error:
    File.WriteAllText(report, capture.ToString() + "\nFAILED: " + str(error))
    Rhino.RhinoApp.WriteLine("Geometry Translator checks failed. " + str(error))
finally:
    Console.SetOut(original_output)
    capture.Dispose()
