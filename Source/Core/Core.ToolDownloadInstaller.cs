using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RhinoSplat.Core;

/// <summary>Per-user portable installs. A receipt is published only after the tools execute successfully.</summary>
public sealed class ToolDownloadInstaller
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly HttpClient _client;
    private readonly Func<ToolPackage, string, CancellationToken, Task> _validate;
    internal const string CpuMarker = ".rhinosplat-colmap-cpu";
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhinoSplat", "Tools");

    public ToolDownloadInstaller() : this(SharedClient, ValidateAsync) { }
    internal ToolDownloadInstaller(HttpClient client, Func<ToolPackage, string, CancellationToken, Task> validate)
    { _client = client; _validate = validate; }

    public Task<ToolDownloadResult> RunAsync(ToolDownloadRequest request, bool install, Action<string>? progress, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return Task.FromResult(new ToolDownloadResult("", "", "", "", "Automatic installation supports Windows and macOS only."));
        try { return RunPackagesAsync(request.Folder, ToolDownloadCatalog.Get(OperatingSystem.IsWindows(), RuntimeInformation.OSArchitecture, request.ColmapCuda), install, progress, token, request.ShowTerminal); }
        catch (Exception e) { return Task.FromResult(new ToolDownloadResult("", "", "", "", e.Message)); }
    }

    internal async Task<ToolDownloadResult> RunPackagesAsync(string folder, ToolPackage[] packages, bool install, Action<string>? progress, CancellationToken token, bool showTerminal = false)
    {
        var paths = new Dictionary<string, string>();
        var messages = new List<string>();
        var versions = string.Join("; ", packages.Select(p => p.Name + " " + p.Version + (p.Name == "COLMAP" ? p.Cpu ? " (CPU)" : " (CUDA)" : "")));
        FileStream? installLock = null;
        ToolDownloadLog? log = null;
        void Report(string text) { log?.Line(text); progress?.Invoke(text); }
        try
        {
            if (string.IsNullOrWhiteSpace(folder)) folder = DefaultFolder;
            var root = Path.GetFullPath(folder.Trim().Trim('"'));
            if (install)
            {
                Directory.CreateDirectory(root);
                log = new ToolDownloadLog(root);
                log.Line(versions);
                if (showTerminal) ColmapLiveLog.OpenTerminal(log.Path, "Splatypus — Installing tools");
                Report("Waiting for the tools folder…");
                installLock = await LockAsync(root, token).ConfigureAwait(false);
            }
            foreach (var package in packages)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Report("Checking " + package.Name + " " + package.Version + "…");
                    var executable = await ReadReceiptAsync(root, package, token).ConfigureAwait(false);
                    if (executable is null && install)
                        executable = await InstallAsync(root, package, Report, token, text => log?.Append(text)).ConfigureAwait(false);
                    if (executable is not null) paths[package.Name] = executable;
                    messages.Add(executable is null ? package.Name + ": press Download to install." : package.Name + " " + package.Version + " ready.");
                    log?.Line(messages[^1] + (executable is null ? "" : " " + executable));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { messages.Add(package.Name + ": " + e.Message); log?.Line(messages[^1]); }
            }
        }
        catch (OperationCanceledException) { messages.Add("Cancelled. Completed installations are kept; press Download to resume."); }
        catch (Exception e) { messages.Add(e.Message); }
        finally
        {
            installLock?.Dispose();
            if (log is not null)
            {
                try
                {
                    log.Line(string.Join(Environment.NewLine, messages));
                    log.Line(token.IsCancellationRequested ? "Installation cancelled. You can close this terminal." : "Installation attempt finished. You can close this terminal.");
                }
                finally { log.Dispose(); }
                if (log.Error is not null) messages.Add(log.Error);
            }
        }
        return new(paths.GetValueOrDefault("FFmpeg", ""), paths.GetValueOrDefault("COLMAP", ""), paths.GetValueOrDefault("Brush", ""), versions,
            string.Join(Environment.NewLine, messages), token.IsCancellationRequested, log?.Path ?? "");
    }

    private async Task<string?> ReadReceiptAsync(string root, ToolPackage package, CancellationToken token)
    {
        var receipt = Path.Combine(root, package.Id + ".json");
        if (!File.Exists(receipt)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receipt, token).ConfigureAwait(false));
            if (data is null || data.Id != package.Id || data.Files.Count == 0) return null;
            var directory = Inside(root, data.Directory);
            foreach (var file in data.Files)
            {
                var info = new FileInfo(Inside(directory, file.Path));
                if (!info.Exists || info.Length != file.Length) return null;
            }
            var executable = Inside(directory, package.Executable);
            await _validate(package, executable, token).ConfigureAwait(false);
            return executable;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // Missing or damaged installations can be repaired into a fresh directory.
    }

    private async Task<string> InstallAsync(string root, ToolPackage package, Action<string>? progress, CancellationToken token, Action<string>? output)
    {
        var relative = Path.Combine("installations", package.Id + "-" + Guid.NewGuid().ToString("N"));
        var directory = Inside(root, relative);
        Directory.CreateDirectory(directory);
        var committed = false;
        try
        {
            if (package.Kind == "conda")
            {
                // Conda embeds absolute prefixes: install at the final path, then publish a receipt.
                var mamba = await GetMicromambaAsync(root, progress, token).ConfigureAwait(false);
                progress?.Invoke("Installing " + package.Name + " " + package.Version + " and its macOS dependencies…");
                await ExecuteAsync(mamba, new[] { "--no-rc", "--root-prefix", Path.Combine(root, "mamba"), "create", "--yes", "--prefix", directory,
                    "--override-channels", "--channel", "https://conda.anaconda.org/conda-forge", "--strict-channel-priority", "--platform", "osx-arm64",
                    package.Name.ToLowerInvariant() + "=" + package.Version }, TimeSpan.FromMinutes(30), token, output).ConfigureAwait(false);
            }
            else
            {
                var archive = Path.Combine(directory, "download.archive");
                await DownloadAsync(package.Url, package.Sha256, archive, package.Name, progress, token).ConfigureAwait(false);
                progress?.Invoke("Extracting " + package.Name + "…");
                if (package.Kind == "zip") await ExtractZipAsync(archive, directory, token, progress).ConfigureAwait(false);
                else
                {
                    // The official archive is checksum verified before invoking the system extractor.
                    await ExecuteAsync("/usr/bin/tar", new[] { "-xvf", archive, "-C", directory }, TimeSpan.FromMinutes(5), token, output).ConfigureAwait(false);
                    var candidates = Directory.GetFiles(directory, package.Executable, SearchOption.AllDirectories);
                    if (candidates.Length != 1) throw new IOException("Brush archive did not contain exactly one executable.");
                    var expected = Inside(directory, package.Executable);
                    if (candidates[0] != expected) File.Move(candidates[0], expected);
                    MakeExecutable(expected);
                }
                File.Delete(archive);
            }
            var executable = Inside(directory, package.Executable);
            if (package.Cpu) await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(executable)!, CpuMarker), package.Id, token).ConfigureAwait(false);
            progress?.Invoke("Validating " + package.Name + "…");
            await _validate(package, executable, token).ConfigureAwait(false);
            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => new InstalledFile(Path.GetRelativePath(directory, path), new FileInfo(path).Length)).ToList();
            var receipt = new Receipt(package.Id, relative, files);
            var temporary = Path.Combine(directory, "receipt.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(root, package.Id + ".json"), true);
            committed = true;
            return executable;
        }
        finally
        {
            // Only this attempt's uniquely created directory is eligible for cleanup.
            if (!committed) TryDeleteDirectory(directory);
        }
    }

    private async Task<string> GetMicromambaAsync(string root, Action<string>? progress, CancellationToken token)
    {
        const string hash = "aa23d0e01d6f492f43aa86720c0f4c8db91978b81f8af46a852f6c4fcf6737d5";
        var destination = Path.Combine(root, "micromamba-2.3.3-osx-arm64");
        if (!File.Exists(destination) || !await HasHashAsync(destination, hash, token).ConfigureAwait(false))
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await DownloadAsync("https://github.com/mamba-org/micromamba-releases/releases/download/2.3.3-0/micromamba-osx-arm64", hash, temporary, "macOS package installer", progress, token).ConfigureAwait(false);
                MakeExecutable(temporary);
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return destination;
    }

    private async Task DownloadAsync(string url, string hash, string destination, string name, Action<string>? progress, CancellationToken token)
    {
        progress?.Invoke("Downloading " + name + "…");
        using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
        {
            var buffer = new byte[131072]; long received = 0; var timer = Stopwatch.StartNew();
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                received += read;
                if (timer.ElapsedMilliseconds >= 1000)
                {
                    var total = response.Content.Headers.ContentLength;
                    progress?.Invoke($"Downloading {name}: {received / 1048576} MB" + (total > 0 ? $" / {total / 1048576} MB" : ""));
                    timer.Restart();
                }
            }
        }
        progress?.Invoke("Download complete. Verifying " + name + " checksum…");
        if (!await HasHashAsync(destination, hash, token).ConfigureAwait(false)) throw new IOException(name + " download checksum mismatch. Retry Download.");
    }

    internal static async Task ExtractZipAsync(string archive, string directory, CancellationToken token, Action<string>? progress = null)
    {
        using var zip = ZipFile.OpenRead(archive);
        var timer = Stopwatch.StartNew(); var extracted = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Archive contains an unsupported symbolic link.");
            var destination = Inside(directory, entry.FullName);
            if (entry.Name.Length == 0) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await source.CopyToAsync(target, token).ConfigureAwait(false);
            extracted++;
            if (timer.ElapsedMilliseconds >= 1000)
            { progress?.Invoke($"Extracting: {extracted} / {zip.Entries.Count} entries — {entry.FullName}"); timer.Restart(); }
        }
    }

    internal static string Inside(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (Path.IsPathRooted(relative) || !full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Tool path escapes its installation folder.");
        return full;
    }

    private static async Task<bool> HasHashAsync(string path, string hash, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<FileStream> LockAsync(string root, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(root, ".download-tools.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromMinutes(30)) { await Task.Delay(500, token).ConfigureAwait(false); }
        }
    }

    internal static async Task ValidateAsync(ToolPackage package, string executable, CancellationToken token)
    {
        if (!File.Exists(executable)) throw new IOException("Expected executable is missing: " + executable);
        var args = package.Name == "FFmpeg" ? new[] { "-version" } : new[] { "--help" };
        var output = await ExecuteAsync(executable, args, TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        var expected = package.Name == "FFmpeg" ? "ffmpeg version " + package.Version : package.Name == "COLMAP" ? "COLMAP " + package.Version : "brush";
        if (!output.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected " + package.Name + " version/help output.");
        if (package.Name == "FFmpeg")
        {
            var probe = Path.Combine(Path.GetDirectoryName(executable)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            var help = await ExecuteAsync(probe, new[] { "-version" }, TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            if (!help.Contains("ffprobe version " + package.Version, StringComparison.Ordinal)) throw new IOException("Matching ffprobe is missing.");
        }
        if (package.Name == "Brush")
            foreach (var option in new[] { "--total-steps", "--export-path", "--export-name", "--with-viewer" })
                if (!output.Contains(option, StringComparison.Ordinal)) throw new IOException("Brush does not support " + option + ".");
    }

    internal static async Task<string> ExecuteAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token, Action<string>? output = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["PATH"] = start.WorkingDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
        var stdout = ReadTailAsync(process.StandardOutput, output);
        var stderr = ReadTailAsync(process.StandardError, output);
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException(Path.GetFileName(executable) + " timed out.");
        }
        var text = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new IOException(Path.GetFileName(executable) + " exited with " + process.ExitCode + ": " + text);
        return text;
    }

    private static async Task<string> ReadTailAsync(StreamReader reader, Action<string>? output)
    {
        // Read available pipe bytes without waiting for a full text buffer or newline.
        var tail = new StringBuilder(); var buffer = new byte[4096];
        var decoder = reader.CurrentEncoding.GetDecoder();
        var characters = new char[reader.CurrentEncoding.GetMaxCharCount(buffer.Length)];
        int count;
        void Publish(int length)
        {
            if (length == 0) return;
            output?.Invoke(new string(characters, 0, length));
            tail.Append(characters, 0, length);
            if (tail.Length > 16000) tail.Remove(0, tail.Length - 16000);
        }
        while ((count = await reader.BaseStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            Publish(decoder.GetChars(buffer, 0, count, characters, 0, false));
        Publish(decoder.GetChars(buffer, 0, 0, characters, 0, true));
        return tail.ToString();
    }

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
    private static void TryDeleteDirectory(string directory)
    { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private sealed record InstalledFile(string Path, long Length);
    private sealed record Receipt(string Id, string Directory, List<InstalledFile> Files);
}
