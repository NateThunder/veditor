using System.Diagnostics;
using VeditorWindow.Models;

namespace VeditorWindow.Services;

public sealed class BackgroundRemovalService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"
    };

    private readonly VeditorPaths _paths;

    public BackgroundRemovalService(VeditorPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public static bool IsSupportedPicture(string? path) =>
        !string.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    public async Task<BackgroundRemovalRuntimeStatus> CheckRuntimeAsync(
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        //== runtime file checks ==============================================
        var missing = new List<string>();
        if (!File.Exists(_paths.BackgroundRemovalPythonExecutablePath))
        {
            missing.Add("portable Python");
        }

        if (!File.Exists(_paths.BackgroundRemovalWorkerScriptPath))
        {
            missing.Add("background-removal worker");
        }

        if (!File.Exists(_paths.BackgroundRemovalInstallationMarkerPath))
        {
            missing.Add("installation marker");
        }

        if (missing.Count > 0)
        {
            return new(false, null, false, null, null, null, missing, "The background-removal runtime is not installed.");
        }
        //=====================================================================

        BackgroundRemovalWorkerMessage? runtimeMessage = null;
        BackgroundRemovalWorkerMessage? workerError = null;

        try
        {
            //== external service call ========================================
            using var process = new Process
            {
                StartInfo = BuildStartInfo("--check")
            };
            process.Start();
            using var registration = cancellationToken.Register(() => TryKill(process));
            var outputTask = ReadLinesAsync(process.StandardOutput, line =>
            {
                var message = BackgroundRemovalWorkerProtocol.Parse(line);
                if (message?.Type == BackgroundRemovalWorkerMessageType.RuntimeStatus)
                {
                    runtimeMessage = message;
                }
                else if (message?.Type == BackgroundRemovalWorkerMessageType.Error)
                {
                    workerError = message;
                }
                else if (!string.IsNullOrWhiteSpace(message?.Message))
                {
                    ReportSafely(log, message.Message);
                }
            });
            var errorTask = ReadLinesAsync(process.StandardError, line => ReportSafely(log, line));
            await AwaitProcessAsync(process, outputTask, errorTask, cancellationToken);
            //=================================================================

            //== output shaping ===============================================
            if (runtimeMessage is null)
            {
                var error = workerError?.Message ?? $"Runtime check exited with code {process.ExitCode}.";
                return new(false, null, false, null, null, null, Array.Empty<string>(), error);
            }

            return new(
                runtimeMessage.Installed == true,
                runtimeMessage.InstallationMode,
                runtimeMessage.CudaAvailable == true,
                runtimeMessage.GpuName,
                runtimeMessage.PythonVersion,
                runtimeMessage.RembgVersion,
                runtimeMessage.MissingComponents ?? Array.Empty<string>(),
                runtimeMessage.Installed == true ? null : runtimeMessage.Message);
            //=================================================================
        }
        catch (Exception ex)
        {
            return new(false, null, false, null, null, null, Array.Empty<string>(),
                cancellationToken.IsCancellationRequested ? "Runtime check canceled." : ex.Message);
        }
    }

    public async Task<BackgroundRemovalResult> RemoveAsync(
        string inputPath,
        BackgroundRemovalOptions options,
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string? operationDirectory = null;

        try
        {
            //== input validation =============================================
            if (options is null)
            {
                return Failure("Background-removal options are required.", stopwatch);
            }

            var normalizedInput = File.Exists(inputPath) ? Path.GetFullPath(inputPath) : null;
            if (normalizedInput is null || !IsSupportedPicture(normalizedInput))
            {
                return Failure("Choose a supported picture before removing its background.", stopwatch);
            }

            if (!File.Exists(_paths.BackgroundRemovalPythonExecutablePath) ||
                !File.Exists(_paths.BackgroundRemovalWorkerScriptPath))
            {
                return Failure("The background-removal runtime is not installed.", stopwatch);
            }
            //=================================================================

            //== temporary output =============================================
            operationDirectory = Path.Combine(_paths.BackgroundRemovalTemporaryDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(operationDirectory);
            var outputPath = Path.Combine(operationDirectory, "background-removed.png");
            //=================================================================

            //== external service call ========================================
            var arguments = new List<string>
            {
                "--input", normalizedInput,
                "--output", outputPath,
                "--model", options.ModelName
            };
            if (options.RefineEdges)
            {
                arguments.Add("--alpha-matting");
            }

            using var process = new Process { StartInfo = BuildStartInfo(arguments.ToArray()) };
            BackgroundRemovalWorkerMessage? completed = null;
            BackgroundRemovalWorkerMessage? workerError = null;
            process.Start();
            using var registration = cancellationToken.Register(() => TryKill(process));
            var outputTask = ReadLinesAsync(process.StandardOutput, line =>
            {
                var message = BackgroundRemovalWorkerProtocol.Parse(line);
                switch (message?.Type)
                {
                    case BackgroundRemovalWorkerMessageType.Progress:
                        if (message.Percent.HasValue)
                        {
                            ReportSafely(progress, message.Percent.Value);
                        }
                        if (!string.IsNullOrWhiteSpace(message.Message))
                        {
                            ReportSafely(status, message.Message);
                        }
                        break;
                    case BackgroundRemovalWorkerMessageType.Status:
                        if (!string.IsNullOrWhiteSpace(message.Message))
                        {
                            ReportSafely(status, message.Message);
                        }
                        break;
                    case BackgroundRemovalWorkerMessageType.Completed:
                        completed = message;
                        break;
                    case BackgroundRemovalWorkerMessageType.Error:
                        workerError = message;
                        break;
                    case BackgroundRemovalWorkerMessageType.Log:
                        if (!string.IsNullOrWhiteSpace(message.Message))
                        {
                            ReportSafely(log, message.Message);
                        }
                        break;
                }
            });
            var errorTask = ReadLinesAsync(process.StandardError, line => ReportSafely(log, line));
            await AwaitProcessAsync(process, outputTask, errorTask, cancellationToken);
            //=================================================================

            //== output validation ============================================
            if (cancellationToken.IsCancellationRequested)
            {
                CleanupDirectory(operationDirectory);
                return new(false, true, null, "Background removal was canceled.", stopwatch.Elapsed);
            }

            if (process.ExitCode != 0 || workerError is not null)
            {
                CleanupDirectory(operationDirectory);
                return Failure(workerError?.Message ?? $"Background removal exited with code {process.ExitCode}.", stopwatch);
            }

            var reportedOutput = completed?.OutputPath;
            var finalOutput = !string.IsNullOrWhiteSpace(reportedOutput) && File.Exists(reportedOutput)
                ? Path.GetFullPath(reportedOutput)
                : outputPath;
            if (!File.Exists(finalOutput) || new FileInfo(finalOutput).Length == 0)
            {
                CleanupDirectory(operationDirectory);
                return Failure("The background-removal worker did not produce a valid PNG.", stopwatch);
            }

            return new(true, false, finalOutput, null, stopwatch.Elapsed);
            //=================================================================
        }
        catch (OperationCanceledException)
        {
            CleanupDirectory(operationDirectory);
            return new(false, true, null, "Background removal was canceled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            CleanupDirectory(operationDirectory);
            return Failure(ex.Message, stopwatch);
        }
    }

    public static void CleanupResult(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        CleanupDirectory(Path.GetDirectoryName(outputPath));
    }

    private ProcessStartInfo BuildStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _paths.BackgroundRemovalPythonExecutablePath,
            WorkingDirectory = _paths.BackgroundRemovalRuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(_paths.BackgroundRemovalWorkerScriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["U2NET_HOME"] = _paths.BackgroundRemovalModelsDirectory;
        return startInfo;
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            onLine(line);
        }
    }

    private static async Task AwaitProcessAsync(Process process, Task outputTask, Task errorTask, CancellationToken token)
    {
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cancellation of the isolated worker process.
        }
    }

    private static void CleanupDirectory(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Temporary data is safe to leave for a later cleanup attempt.
        }
    }

    private static BackgroundRemovalResult Failure(string message, Stopwatch stopwatch) =>
        new(false, false, null, message, stopwatch.Elapsed);

    private static void ReportSafely<T>(IProgress<T>? progress, T value)
    {
        try
        {
            progress?.Report(value);
        }
        catch
        {
            // A UI progress subscriber must not terminate worker stream reading.
        }
    }
}
