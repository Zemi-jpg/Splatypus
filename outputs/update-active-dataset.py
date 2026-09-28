from pathlib import Path
root = Path(__file__).resolve().parent.parent
p = root / 'Core.ColmapModelTransformRunner.cs'
s = p.read_text(encoding='utf-8-sig')
start = s.index('    public static async Task<ColmapModelTransformResult> RunAsync(')
end = s.index('    private static void TransformImages', start)
s = s[:start] + '''    public static async Task<ColmapModelTransformResult> RunAsync(
        string colmapExecutable, string projectRoot, ColmapSimilarityTransform transform,
        CancellationToken cancellationToken)
    {
        var log = string.Empty;
        var textPath = string.Empty; var sparsePath = string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataset = new ActiveDataset(projectRoot);
            textPath = dataset.Text; sparsePath = dataset.Sparse;
            using var lease = DatasetLease.Acquire(dataset.Root);
            dataset.Validate();
            var validation = ToolValidation.Colmap(colmapExecutable);
            if (!validation.IsValid) throw new IOException(validation.Message);
            if (!double.IsFinite(transform.Scale) || transform.Scale <= 0)
                throw new ArgumentException("Alignment requires a positive uniform scale.");
            using var transaction = new DatasetTransaction(dataset);
            var source = Path.Combine(transaction.Work, "source-text");
            var text = Path.Combine(transaction.Work, "text");
            var sparse = Path.Combine(transaction.Work, "sparse");
            Directory.CreateDirectory(text);
            Directory.CreateDirectory(Path.Combine(sparse, "0"));
            var logPath = Path.Combine(dataset.Colmap, "alignment.log");
            ColmapLiveLog.Create(logPath);
            // Always read the current binary model, including corrected intrinsics after undistortion.
            log = await DatasetColmapProcess.Export(validation.ExecutablePath, dataset.Sparse, source, logPath, cancellationToken);
            ActiveDataset.ValidateText(source, dataset.Images);
            File.Copy(Path.Combine(source, "cameras.txt"), Path.Combine(text, "cameras.txt"));
            TransformImages(Path.Combine(source, "images.txt"), Path.Combine(text, "images.txt"), transform);
            TransformPoints(Path.Combine(source, "points3D.txt"), Path.Combine(text, "points3D.txt"), transform);
            log += await DatasetColmapProcess.Run(validation.ExecutablePath, logPath, cancellationToken,
                "model_converter", "--input_path", text, "--output_path", Path.Combine(sparse, "0"), "--output_type", "BIN");
            ActiveDataset.ValidatePair(dataset.Images, Path.Combine(sparse, "0"));
            ActiveDataset.ValidateText(text, dataset.Images);
            transaction.Commit(cancellationToken, (sparse, Path.GetDirectoryName(dataset.Sparse)!), (text, dataset.Text));
            return new(true, false, textPath, sparsePath, log, "Active dataset aligned in place. Brush uses this model with its current images.");
        }
        catch (Exception error)
        {
            return new(false, error is OperationCanceledException || cancellationToken.IsCancellationRequested,
                textPath, sparsePath, log + error, "Alignment did not replace the active model: " + error.Message);
        }
    }

''' + s[end:]
s=s.replace('        foreach (var line in File.ReadLines(sourcePath))\n        {', '        var observation = false;\n        foreach (var line in File.ReadLines(sourcePath))\n        {\n            if (observation) { writer.WriteLine(line); observation = false; continue; }',1)
s=s.replace('            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);','            observation = true;\n            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);',1)
p.write_text(s,encoding='utf-8')
# Protect the older processing runners before they touch project data.
for filename in ['Core.ColmapFeatureExtractionRunner.cs','Core.ColmapExhaustiveMatchingRunner.cs']:
    p=root/filename; s=p.read_text(encoding='utf-8-sig')
    marker='        var validation = ToolValidation.Colmap(executable);'
    s=s.replace(marker,'''        using var lease = DatasetLease.Acquire(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();
''' + marker,1)
    p.write_text(s,encoding='utf-8')
p=root/'Core.FFmpegMediaImporter.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('            return await RunCoreAsync(', '            using var lease = DatasetLease.Acquire(projectRoot);\n            return await RunCoreAsync(',1)
p.write_text(s,encoding='utf-8')
p=root/'Core.BrushTrainingRunner.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('        var fullDataset = Path.GetFullPath(dataset);','        using var lease = DatasetLease.Acquire(projectRoot);\n        var fullDataset = Path.GetFullPath(dataset);',1)
p.write_text(s,encoding='utf-8')
# Pipeline keeps a single project dataset address; per-stage results remain diagnostics only.
p=root/'Core.ColmapPipelineRunner.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('bool Undistort = true', 'bool Undistort = false')
s=s.replace('    string Log, string Status, int Completed, int Total);','    string Log, string Status, int Completed, int Total)\n{\n    public string DatasetPath { get; init; } = string.Empty;\n}')
s=s.replace('new(success, cancelled, code, database, sparse, dense, text, log.ToString(), status, completed, total);', 'new(success, cancelled, code, database, sparse, dense, text, log.ToString(), status, completed, total) { DatasetPath = request.ProjectRoot };')
s=s.replace('            dense = Path.Combine(colmap, "dense");','            dense = root; // Legacy result property now aliases the active dataset; no dense copy is produced.')
s=s.replace('            ownedRoot = root;','            ownedRoot = root;\n            using var lease = DatasetLease.Acquire(root);')
s=s.replace('new[] { (request.Sparse, sparse), (request.Undistort, dense), (request.Export, text) }','new[] { (request.Sparse, sparse) }')
p.write_text(s,encoding='utf-8')
