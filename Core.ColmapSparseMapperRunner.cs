namespace RhinoSplat.Core;

public static class ColmapSparseMapperRunner
{
    public static async Task<ColmapRunResult> RunAsync(string executable, string projectRoot, bool showTerminal, bool replaceExisting, CancellationToken cancellationToken)
    {
        var log = string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataset = new ActiveDataset(projectRoot);
            using var lease = DatasetLease.Acquire(dataset.Root);
            var validation = ToolValidation.Colmap(executable);
            if (!validation.IsValid) throw new IOException(validation.Message);
            var database = Path.Combine(dataset.Colmap, "database.db");
            if (!File.Exists(database)) throw new IOException("Extract features and match the active images before reconstructing.");
            if (Directory.Exists(dataset.Sparse) && !replaceExisting)
                throw new IOException("An active model already exists. Enable Replace to reconstruct it.");
            using var transaction = new DatasetTransaction(dataset);
            var sparse = Path.Combine(transaction.Work, "sparse");
            Directory.CreateDirectory(sparse);
            var logPath = Path.Combine(dataset.Colmap, "sparse-mapper.log");
            ColmapLiveLog.Create(logPath);
            if (showTerminal) ColmapLiveLog.OpenTerminal(logPath, "Splatypus — COLMAP reconstruction");
            log = await DatasetColmapProcess.Run(validation.ExecutablePath, logPath, cancellationToken,
                "mapper", "--database_path", database, "--image_path", dataset.Images, "--output_path", sparse);
            var model = Path.Combine(sparse, "0");
            ActiveDataset.ValidatePair(dataset.Images, model);
            var text = Path.Combine(transaction.Work, "text");
            log += await DatasetColmapProcess.Export(validation.ExecutablePath, model, text, logPath, cancellationToken);
            ActiveDataset.ValidateText(text, dataset.Images);
            // Publish only the selected model, never alternative disconnected reconstructions.
            foreach (var alternative in Directory.GetDirectories(sparse))
                if (Path.GetFileName(alternative) != "0") DatasetTransaction.DeleteTree(alternative);
            transaction.Commit(cancellationToken, (sparse, Path.GetDirectoryName(dataset.Sparse)!), (text, dataset.Text));
            return new(true, false, 0, dataset.Root, log, "Active dataset reconstructed. Use Dataset for optional undistortion, alignment or Brush.");
        }
        catch (Exception error)
        {
            return new(false, error is OperationCanceledException || cancellationToken.IsCancellationRequested, (error as DatasetColmapException)?.ExitCode, projectRoot, log + error,
                "Reconstruction did not replace the active model: " + error.Message);
        }
    }
}
