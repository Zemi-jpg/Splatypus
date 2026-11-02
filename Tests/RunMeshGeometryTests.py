# Run in a separate Rhino test instance; does not add document geometry.
import clr
from System import Array, String, Object, Console
from System.IO import StringWriter, File
from System.Reflection import Assembly

root = r"C:\Users\basar\RhinoSplatDev\bin\MeshReconstruction\validation"
report = root + r"\rhino-test-report.txt"
original_output = Console.Out
original_error = Console.Error
capture = StringWriter()
try:
    Console.SetOut(capture)
    Console.SetError(capture)
    assembly = Assembly.LoadFrom(r"C:\Users\basar\AppData\Local\Temp\RhinoSplatBuild\RhinoSplat.MeshGeometry.SmokeTest\bin\Debug\net8.0-windows\RhinoSplat.MeshGeometry.SmokeTest.dll")
    assembly.EntryPoint.Invoke(None, Array[Object]([Array[String]([root + r"\native-poisson.ply", "--in-rhino"])]))
    File.WriteAllText(report, capture.ToString())
except Exception as error:
    File.WriteAllText(report, capture.ToString() + "\nFAILED: " + str(error))
finally:
    Console.SetOut(original_output)
    Console.SetError(original_error)
    capture.Dispose()
