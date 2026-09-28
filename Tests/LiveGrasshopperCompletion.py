# Run in Rhino's _-RunPythonScript command. Creates an isolated test definition.
import clr
import json
import os
import time
import Rhino
import scriptcontext as sc
from System import Activator
from System.Reflection import Assembly
from System.Drawing import PointF
from System.Windows.Forms import Timer

root = r"C:\Users\basar\RhinoSplatDev\bin\LiveCompletionTest"
report_path = os.path.join(root, "report.json")
Rhino.RhinoApp.RunScript("_Grasshopper", False)
clr.AddReference("Grasshopper")
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import GH_Panel
from Grasshopper.Kernel.Types import GH_String, GH_Number, GH_Boolean

assembly = Assembly.LoadFrom(r"C:\Users\basar\RhinoSplatDev\bin\FFmpegEstimate\RhinoSplat.gha")
component = Activator.CreateInstance(assembly.GetType("RhinoSplat.Grasshopper.FFmpegMediaImportComponent"))
doc = GH_Document()
component.CreateAttributes()
component.Attributes.Pivot = PointF(250, 200)
values = [GH_String(r"C:\Program Files\ffmpeg\ffmpeg-2026-09-14-git-6efe500d2e-full_build\bin\ffmpeg.exe"),
          GH_String(os.path.join(root, "project-" + str(int(time.time())))), GH_String(os.path.join(root, "fixture.mp4")),
          GH_Number(2), GH_Boolean(True), GH_Boolean(False), GH_Boolean(False), GH_Boolean(False)]
for index, value in enumerate(values):
    component.Params.Input[index].PersistentData.Clear()
    component.Params.Input[index].PersistentData.Append(value)
doc.AddObject(component, False)
for index, y in [(1, 100), (3, 300), (4, 500)]:
    panel = GH_Panel()
    panel.CreateAttributes()
    panel.Attributes.Pivot = PointF(550, y)
    panel.AddSource(component.Params.Output[index])
    doc.AddObject(panel, False)

Grasshopper.Instances.DocumentServer.AddDocument(doc)
Grasshopper.Instances.ActiveCanvas.Document = doc
state = {"solutions": 0, "start": time.time(), "done_at": None, "solutions_at_done": None,
         "ticks_after_done": 0, "max_tick_gap_after_done": 0, "last_tick": time.time()}

def text_output(index):
    return "\n".join(str(item) for item in component.Params.Output[index].VolatileData.AllData(True))

def solution_end(sender, event):
    state["solutions"] += 1

def tick(sender, event):
    now = time.time()
    status = text_output(4)
    # Import and estimate may finish separately. Observe idleness once both outputs are ready.
    if status.startswith("Imported") and text_output(5) == "693" and state["done_at"] is None:
        state["done_at"] = now
        state["solutions_at_done"] = state["solutions"]
    if state["done_at"] is not None:
        state["ticks_after_done"] += 1
        state["max_tick_gap_after_done"] = max(state["max_tick_gap_after_done"], now - state["last_tick"])
    state["last_tick"] = now
    finished = state["done_at"] is not None and now - state["done_at"] >= 10
    timeout = now - state["start"] > 90
    if finished or timeout:
        timer.Stop()
        doc.SolutionEnd -= solution_end
        result = dict(state)
        result["status"] = status
        result["image_count"] = text_output(2)
        result["log_characters"] = len(text_output(3))
        result["component_assembly"] = component.GetType().Assembly.Location
        result["extra_solutions_after_done"] = (state["solutions"] - state["solutions_at_done"]
                                                 if state["solutions_at_done"] is not None else None)
        result["passed"] = (finished and result["image_count"] == "693"
                            and result["extra_solutions_after_done"] == 0
                            and result["log_characters"] < 6400 and state["ticks_after_done"] >= 30)
        with open(report_path, "w") as stream:
            json.dump(result, stream, indent=2)
        Rhino.RhinoApp.WriteLine("RhinoSplat live completion test: " + str(result["passed"]))

doc.SolutionEnd += solution_end
timer = Timer()
timer.Interval = 100
timer.Tick += tick
sc.sticky["RhinoSplatLiveCompletionTest"] = (doc, component, timer, tick, solution_end, state)
timer.Start()
doc.NewSolution(False)
