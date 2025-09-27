using MinimalBrowser.Services;
using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
namespace TransformerNavigator.Services
{
    public sealed class ProcessImageGenerator : IImageGenerator
    {
        public string ToolPath { get; }
        public string ArgumentsTemplate { get; } // e.g.: --prompt {prompt} --out {out} --steps 40
        public string WorkingDirectory { get; }
        public string OutputDirectory { get; }
        public string OutputExtension { get; }   // e.g. ".png", ".jpg"
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan FileWaitTimeout { get; set; } = TimeSpan.FromSeconds(45);
        public bool KillProcessTreeOnCancel { get; set; } = true;
        public TimeSpan FallbackEstimate { get; set; } = TimeSpan.FromMinutes(2); // used if no progress lines

        public ProcessImageGenerator(
            string toolPath,
            string argumentsTemplate,
            string outputDirectory,
            string workingDirectory = null,
            string outputExtension = ".png")
        {
            if (string.IsNullOrWhiteSpace(toolPath)) throw new ArgumentNullException(nameof(toolPath));
            if (string.IsNullOrWhiteSpace(argumentsTemplate)) throw new ArgumentNullException(nameof(argumentsTemplate));
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentNullException(nameof(outputDirectory));

            ToolPath = toolPath;
            ArgumentsTemplate = argumentsTemplate;
            OutputDirectory = outputDirectory;
            WorkingDirectory = workingDirectory;
            OutputExtension = outputExtension.StartsWith(".") ? outputExtension : "." + outputExtension;

            Directory.CreateDirectory(OutputDirectory);
        }

        public async Task<byte[]> GenerateAsync(string prompt, IProgress<double> progress = null, CancellationToken ct = default)
        {
            if (prompt == null) prompt = "";

            string outPath = Path.Combine(
                OutputDirectory,
                $"img_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{OutputExtension}");

            string args = BuildArguments(prompt, outPath);

            var psi = new ProcessStartInfo
            {
                FileName = ToolPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory)
                    ? Path.GetDirectoryName(ToolPath) ?? Environment.CurrentDirectory
                    : WorkingDirectory
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            proc.Exited += (s, e) => exitTcs.TrySetResult(proc.ExitCode);

            var rePercent = new Regex(@"(?:^|\s)(\d{1,3})\s?%(?:\s|$)", RegexOptions.Compiled);
            var reSteps = new Regex(@"(\d+)\s*/\s*(\d+)\s*(?:steps?|it(?:erations?)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            DateTime start = DateTime.UtcNow;
            bool sawExplicitProgress = false;

            void HandleLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                // Try parse "NN%" pattern
                var m = rePercent.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int pct))
                {
                    pct = Math.Max(0, Math.Min(100, pct));
                    progress?.Report(pct / 100d);
                    sawExplicitProgress = true;
                    return;
                }

                // Try parse "X/Y steps"
                var s = reSteps.Match(line);
                if (s.Success &&
                    double.TryParse(s.Groups[1].Value, out double cur) &&
                    double.TryParse(s.Groups[2].Value, out double max) &&
                    max > 0)
                {
                    var pct2 = Math.Max(0.0, Math.Min(1.0, cur / max));
                    progress?.Report(pct2);
                    sawExplicitProgress = true;
                    return;
                }
            }

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) HandleLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) HandleLine(e.Data); };

            try
            {
                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start image generator process.");

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var ctr = ct.Register(() =>
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            if (KillProcessTreeOnCancel) proc.Kill(entireProcessTree: true);
                            else proc.Kill();
                        }
                    }
                    catch { /* ignore */ }
                });

                // Fallback progress loop, in case the tool prints no progress
                _ = Task.Run(async () =>
                {
                    while (!proc.HasExited && !ct.IsCancellationRequested)
                    {
                        if (!sawExplicitProgress && FallbackEstimate.TotalMilliseconds > 0)
                        {
                            var elapsed = DateTime.UtcNow - start;
                            var frac = Math.Min(0.99, elapsed.TotalMilliseconds / FallbackEstimate.TotalMilliseconds);
                            progress?.Report(Math.Max(0.0, Math.Min(0.99, frac)));
                        }
                        await Task.Delay(500, ct).ConfigureAwait(false);
                    }
                }, ct);

                // Wait for process completion or timeout
                var finishedTask = await Task.WhenAny(exitTcs.Task, Task.Delay(Timeout, ct)).ConfigureAwait(false);
                if (finishedTask != exitTcs.Task)
                {
                    throw new OperationCanceledException(ct.IsCancellationRequested
                        ? "Canceled by user."
                        : $"Image generation timed out after {Timeout}.");
                }

                int exitCode = await exitTcs.Task.ConfigureAwait(false);
                // Even if exit code is non-zero, try to use the file if exists.
                // You can make non-zero exit fail hard if you prefer.

                // Wait for output file to appear and become stable
                string finalPath = await WaitForFileStableAsync(outPath, FileWaitTimeout, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(finalPath))
                    throw new FileNotFoundException("Image output file was not produced.", outPath);

                // Read bytes robustly
                byte[] bytes = await ReadAllBytesRobustAsync(finalPath, ct).ConfigureAwait(false);

                progress?.Report(1.0);
                return bytes;
            }
            finally
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            }
        }

        private string BuildArguments(string prompt, string outPath)
        {
            // Replace placeholders with safely quoted values
            string qp = QuoteArg(prompt);
            string qo = QuoteArg(outPath);
            return (ArgumentsTemplate ?? string.Empty)
                .Replace("{prompt}", qp)
                .Replace("{out}", qo);
        }

        private static string QuoteArg(string s)
        {
            if (s == null) return "\"\"";
            // Basic quoting for Windows command line
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private static async Task<string> WaitForFileStableAsync(string path, TimeSpan timeout, CancellationToken ct)
        {
            var start = DateTime.UtcNow;
            long lastLen = -1;
            int stableCount = 0;

            while (DateTime.UtcNow - start < timeout)
            {
                ct.ThrowIfCancellationRequested();

                if (File.Exists(path))
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        if (fi.Length > 0)
                        {
                            if (fi.Length == lastLen)
                            {
                                // same length twice -> likely complete
                                if (++stableCount >= 2) return path;
                            }
                            else
                            {
                                stableCount = 0;
                            }
                            lastLen = fi.Length;
                        }
                    }
                    catch { /* ignore transient IO issues */ }
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            return null;
        }

        private static async Task<byte[]> ReadAllBytesRobustAsync(string path, CancellationToken ct)
        {
            // Retry a few times in case another process still holds the file
            for (int i = 0; i < 5; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }
            // last try
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
    }
}
