namespace RhinoSplat.Core;

public static class ColmapImageUndistorterRunner
{
    // Legacy replaceExisting argument retained; Run now explicitly replaces the active pair.
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
            dataset.Validate();
            using var transaction = new DatasetTransaction(dataset);
            var workspace = Path.Combine(transaction.Work, "undistorted");
            Directory.CreateDirectory(workspace);
            var logPath = Path.Combine(dataset.Colmap, "image-undistorter.log");
            ColmapLiveLog.Create(logPath);
            if (showTerminal) ColmapLiveLog.OpenTerminal(logPath, "Splatypus — Undistort active dataset");
            log = await DatasetColmapProcess.Run(validation.ExecutablePath, logPath, cancellationToken,
                "image_undistorter", "--image_path", dataset.Images, "--input_path", dataset.Sparse,
                "--output_path", workspace, "--output_type", "COLMAP");
            var images = Path.Combine(workspace, "images");
            var sparse = Path.Combine(workspace, "sparse");
            ActiveDataset.ValidatePair(images, sparse);
            var text = Path.Combine(transaction.Work, "text");
            log += await DatasetColmapProcess.Export(validation.ExecutablePath, sparse, text, logPath, cancellationToken);
            ActiveDataset.ValidateText(text, images);
            var sparseRoot = Path.Combine(transaction.Work, "sparse");
            Directory.CreateDirectory(sparseRoot);
            Directory.Move(sparse, Path.Combine(sparseRoot, "0"));
            transaction.Commit(cancellationToken, (images, dataset.Images), (sparseRoot, Path.GetDirectoryName(dataset.Sparse)!),
                (text, dataset.Text), (null, Path.Combine(dataset.Colmap, "database.db")),
                (null, Path.Combine(dataset.Colmap, "database.db-wal")), (null, Path.Combine(dataset.Colmap, "database.db-shm")));
            return new(true, false, 0, dataset.Root, log, "Active dataset overwritten with undistorted images and matching cameras. Alignment and Brush now use this dataset. Re-extract features before reconstructing again.");
        }
        catch (Exception error)
        {
            return new(false, error is OperationCanceledException || cancellationToken.IsCancellationRequested, (error as DatasetColmapException)?.ExitCode, projectRoot, log + error,
                "Undistortion did not replace the active dataset: " + error.Message);
        }
    }
}
