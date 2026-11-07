using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino.Geometry;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class ColmapModelAlignmentComponent : SpTaskCapableComponent<ColmapModelTransformResult>
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("align-model");

    private bool _previousWrite;
    private ColmapModelTransformResult? _lastResult;
    public ColmapModelAlignmentComponent()
        : base("SP Align COLMAP Model", "Align Model", "Writes a separate aligned model from the active original or undistorted base model.", "Splatypus", "04 Realignment") { }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        MigrateDatasetPorts();
    }
    internal void MigrateDatasetPorts()
    {
        if (DatasetPorts.RemoveInputs(this, "Source Text Model", "Output Name", "Replace Existing")) _previousWrite = true;
        foreach (var param in Params.Output.ToArray())
            if (param.Name == "Active Dataset") Params.UnregisterOutputParameter(param, true);
        DatasetPorts.EnsureTextOutput(this, "Aligned Text Model", "Text", "Separate aligned text model for geometry previews.", 0);
        DatasetPorts.EnsureTextOutput(this, "Aligned Sparse Model", "Sparse", "Separate aligned binary model for Brush's optional model input.", 1);
        DatasetPorts.RenameInput(this, 1, "Active Dataset", "Dataset", "Connect the project Root or Active Dataset. Each Write reads this base model without replacing it.");
        Params.Input[3].Description = "Press to replace the separate aligned output using the active base model. Repeated writes do not compound alignment.";
        Params.Input[2].Access = GH_ParamAccess.tree;
        Params.Input[2].DataMapping = GH_DataMapping.None;
    }
    public override bool Read(GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        _previousWrite = true;
        return base.Read(reader);
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        p.AddTextParameter("Active Dataset", "Dataset", "Connect Root from Project. Uses the current model, including any undistortion.", GH_ParamAccess.item);
        p.AddTransformParameter("Transforms", "Xforms", "One ordered transform sequence. Identical branches are applied once.", GH_ParamAccess.tree);
        p.AddBooleanParameter("Write", "Write", "Press to replace the separate aligned output using the active base model. Repeated writes do not compound alignment.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Aligned Text Model", "Text", "Separate aligned text model for geometry previews.", GH_ParamAccess.item);
        p.AddTextParameter("Aligned Sparse Model", "Sparse", "Separate aligned binary model for Brush's optional model input.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "Alignment log.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Execution status.", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            var exe = string.Empty; var root = string.Empty; var write = false;
            da.GetData(3, ref write);
            var edge = write && !_previousWrite; _previousWrite = write;
            if (!edge) return;
            if (!da.GetData(0, ref exe) || !da.GetData(1, ref root) || !da.GetDataTree(2, out GH_Structure<GH_Transform> tree)) return;
            if (!TryGetTransformSequence(tree, out var xforms, out var error) || !TryGetSimilarity(xforms, out var similarity, out error))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error); return; }
            Message = "Aligning…";
            TaskList.Add(Task.Run(() => ColmapModelTransformRunner.RunAsync(exe, root, similarity, CancelToken), CancelToken));
            return;
        }
        if (GetSolveResults(da, out ColmapModelTransformResult result)) _lastResult = result;
        if (_lastResult is null) { da.SetData(3, "Press Write to save a separate aligned model from the active base dataset."); return; }
        var latest = _lastResult;
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        if (latest.Succeeded) { da.SetData(0, latest.TextModelPath); da.SetData(1, latest.SparseModelPath); }
        da.SetData(2, latest.Log); da.SetData(3, latest.Status);
    }
    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override Guid ComponentGuid => new("BD8D1415-FE3B-4A33-A77B-6874AADC5967");

    internal static bool TryGetTransformSequence(GH_Structure<GH_Transform> tree, out List<Transform> sequence, out string error)
    {
        sequence = new List<Transform>();
        error = string.Empty;
        foreach (var branch in tree.Branches)
        {
            if (branch.Count == 0) continue;
            var candidate = new List<Transform>(branch.Count);
            foreach (var item in branch)
            {
                if (item is null || !item.IsValid)
                {
                    error = "Xforms contains an invalid transform. No alignment was written.";
                    return false;
                }
                candidate.Add(item.Value);
            }
            if (sequence.Count == 0)
            {
                sequence = candidate;
                continue;
            }
            // List-access geometry may solve once per branch, emitting the same
            // complete sequence for a cloud and again for camera planes.
            // Compare whole branches: repeated steps within a sequence are intentional.
            var identical = sequence.Count == candidate.Count;
            for (var i = 0; identical && i < sequence.Count; i++)
                identical = sequence[i].Equals(candidate[i]);
            if (!identical)
            {
                error = "Xforms contains different transform sequences. A COLMAP model needs one alignment. Select one branch, or flatten only if you intend to apply all branches in sequence.";
                return false;
            }
        }
        if (sequence.Count == 0)
        {
            error = "At least one valid Transform is required.";
            return false;
        }
        return true;
    }

    internal static bool TryGetSimilarity(IReadOnlyList<Transform> transforms, out ColmapSimilarityTransform result, out string error)
    {
        result = default; error = string.Empty;
        const double tolerance = 1e-8;
        var x = Compose(transforms);
        if (Math.Abs(x.M30) > tolerance || Math.Abs(x.M31) > tolerance || Math.Abs(x.M32) > tolerance || Math.Abs(x.M33 - 1.0) > tolerance)
        {
            error = "Transform must be affine (no perspective)."; return false;
        }
        var a = new Vector3d(x.M00, x.M10, x.M20);
        var b = new Vector3d(x.M01, x.M11, x.M21);
        var c = new Vector3d(x.M02, x.M12, x.M22);
        var la = a.Length; var lb = b.Length; var lc = c.Length;
        if (la <= tolerance || lb <= tolerance || lc <= tolerance)
        {
            error = "Transform cannot have a zero scale."; return false;
        }
        var scale = (la + lb + lc) / 3.0;
        if (Math.Abs(la - scale) > scale * tolerance || Math.Abs(lb - scale) > scale * tolerance || Math.Abs(lc - scale) > scale * tolerance
            || Math.Abs(Dot(a, b)) > scale * scale * tolerance || Math.Abs(Dot(a, c)) > scale * scale * tolerance || Math.Abs(Dot(b, c)) > scale * scale * tolerance)
        {
            error = "Only Move, Rotate, and uniform Scale are valid. Do not use non-uniform Scale, Shear, or Mirror with a COLMAP model."; return false;
        }
        a /= scale; b /= scale; c /= scale;
        if (Dot(Vector3d.CrossProduct(a, b), c) < 0.0)
        {
            error = "Mirror transforms are not valid for COLMAP camera poses."; return false;
        }
        result = new ColmapSimilarityTransform(scale, a.X, b.X, c.X, a.Y, b.Y, c.Y, a.Z, b.Z, c.Z, x.M03, x.M13, x.M23);
        return true;
    }

    private static double Dot(Vector3d left, Vector3d right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static Transform Compose(IReadOnlyList<Transform> transforms)
    {
        var combined = Transform.Identity;
        foreach (var next in transforms)
            combined = Multiply(next, combined);
        return combined;
    }

    private static Transform Multiply(Transform left, Transform right) => new()
    {
        M00 = left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20 + left.M03 * right.M30,
        M01 = left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21 + left.M03 * right.M31,
        M02 = left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22 + left.M03 * right.M32,
        M03 = left.M00 * right.M03 + left.M01 * right.M13 + left.M02 * right.M23 + left.M03 * right.M33,
        M10 = left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20 + left.M13 * right.M30,
        M11 = left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31,
        M12 = left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32,
        M13 = left.M10 * right.M03 + left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33,
        M20 = left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20 + left.M23 * right.M30,
        M21 = left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31,
        M22 = left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32,
        M23 = left.M20 * right.M03 + left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33,
        M30 = left.M30 * right.M00 + left.M31 * right.M10 + left.M32 * right.M20 + left.M33 * right.M30,
        M31 = left.M30 * right.M01 + left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31,
        M32 = left.M30 * right.M02 + left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32,
        M33 = left.M30 * right.M03 + left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33
    };
}
