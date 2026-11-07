namespace RhinoSplat.Core;

public static class ColmapModelExportRunner
{
    public static async Task<ColmapRunResult> RunAsync(string executable, string projectRoot, bool showTerminal, bool replaceExisting, CancellationToken cancellationToken)
    {
        var log = string.Empty;
        var output = string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataset = new ActiveDataset(projectRoot);
            output = dataset.Text;
            using var lease = DatasetLease.Acquire(dataset.Root);
            var validation = ToolValidation.Colmap(executable);
            if (!validation.IsValid) throw new IOException(validation.Message);
            dataset.Validate();
            using var transaction = new DatasetTransaction(dataset);
            var text = Path.Combine(transaction.Work, "text");
            var logPath = Path.Combine(dataset.Colmap, "model-export.log");
            ColmapLiveLog.Create(logPath);
            if (showTerminal) ColmapLiveLog.OpenTerminal(logPath, "Splatypus — Export active model");
            log = await DatasetColmapProcess.Export(validation.ExecutablePath, dataset.Sparse, text, logPath, cancellationToken);
            ActiveDataset.ValidateText(text, dataset.Images);
            transaction.Commit(cancellationToken, (text, dataset.Text));
            var saved = $"COLMAP model saved to: {dataset.Text}";
            ColmapLiveLog.AppendLine(logPath, saved);
            log += Environment.NewLine + saved + Environment.NewLine;
            return new(true, false, 0, output, log, saved);
        }
        catch (Exception error)
        {
            return new(false, error is OperationCanceledException || cancellationToken.IsCancellationRequested, (error as DatasetColmapException)?.ExitCode, output, log + error,
                "Text export did not replace the active text model: " + error.Message);
        }
    }
}
