using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VeditorWindow.Models;

namespace VeditorWindow.Services;

public sealed class WatermarkRemovalService
{
    private readonly VeditorPaths _paths;

    public WatermarkRemovalService(VeditorPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<WatermarkRuntimeStatus> CheckRuntimeAsync(
        string? ffmpegPath,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        //== runtime file checks ==============================================
        var pythonExists = File.Exists(_paths.PythonExecutablePath);
        var workerExists = File.Exists(_paths.WorkerScriptPath);
        var markerExists = File.Exists(_paths.InstallationMarkerPath);
        var normalizedFfmpegPath = TryNormalizeExistingFile(ffmpegPath);
        var ffmpegAvailable = normalizedFfmpegPath is not null;
        var dependenciesAvailable = false;
        var cudaAvailable = false;
        var markerValid = false;
        var markerOutdated = false;
        var florenceModelAvailable = false;
        var lamaModelAvailable = false;
        string? installationMode = null;
        string? gpuName = null;
        string? pythonVersion = null;
        IReadOnlyList<string> missingComponents = Array.Empty<string>();
        var runtimeReportedInstalled = false;
        var errors = new List<string>();

        if (cancellationToken.IsCancellationRequested)
        {
            errors.Add("The runtime check was canceled.");
        }

        if (!markerExists)
        {
            errors.Add("The runtime installation marker is missing.");
        }

        if (!pythonExists)
        {
            errors.Add("The portable Python executable is missing.");
        }

        if (!workerExists)
        {
            errors.Add("The watermark worker script is missing.");
        }

        if (!ffmpegAvailable)
        {
            errors.Add("FFmpeg is unavailable.");
        }
        //=====================================================================

        if (pythonExists && workerExists && !cancellationToken.IsCancellationRequested)
        {
            //== worker dependency check ======================================
            WatermarkWorkerMessage? runtimeMessage = null;
            WatermarkWorkerMessage? workerError = null;

            try
            {
                using var process = new Process
                {
                    StartInfo = BuildRuntimeCheckStartInfo(normalizedFfmpegPath)
                };

                process.Start();
                using var cancellationRegistration = cancellationToken.Register(
                    () => TryKillProcessTree(process));

                var outputTask = ReadLinesAsync(process.StandardOutput, line =>
                {
                    var message = WatermarkWorkerProtocol.Parse(line);
                    if (message is null)
                    {
                        return;
                    }

                    switch (message.Type)
                    {
                        case WatermarkWorkerMessageType.RuntimeStatus:
                            runtimeMessage = message;
                            break;
                        case WatermarkWorkerMessageType.Error:
                            workerError = message;
                            ReportSafely(log, FormatWorkerError(message));
                            break;
                        default:
                            ReportSafely(log, message.Message ?? message.RawLine ?? line);
                            break;
                    }
                });
                var errorTask = ReadLinesAsync(
                    process.StandardError,
                    line => ReportSafely(log, line));

                await AwaitProcessCompletionAsync(
                    process,
                    outputTask,
                    errorTask,
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    errors.Add("The runtime check was canceled.");
                }
                else if (process.ExitCode != 0)
                {
                    errors.Add($"The runtime check exited with code {process.ExitCode}.");
                }
                else if (workerError is not null)
                {
                    errors.Add(FormatWorkerError(workerError));
                }
                else if (runtimeMessage is null)
                {
                    errors.Add("The worker did not return runtime status.");
                }
                else
                {
                    dependenciesAvailable = runtimeMessage.DependenciesAvailable ?? false;
                    cudaAvailable = runtimeMessage.CudaAvailable ?? false;
                    markerValid = runtimeMessage.MarkerValid ?? false;
                    markerOutdated = runtimeMessage.MarkerOutdated ?? false;
                    florenceModelAvailable = runtimeMessage.FlorenceModelAvailable ?? false;
                    lamaModelAvailable = runtimeMessage.LamaModelAvailable ?? false;
                    installationMode = runtimeMessage.InstallationMode;
                    gpuName = runtimeMessage.GpuName;
                    pythonVersion = runtimeMessage.PythonVersion;
                    missingComponents = runtimeMessage.MissingComponents ?? Array.Empty<string>();
                    runtimeReportedInstalled = runtimeMessage.Installed ?? false;

                    if (!runtimeReportedInstalled)
                    {
                        errors.Add(runtimeMessage.Message ?? "Required Python dependencies are unavailable.");
                    }
                }
            }
            catch (Exception ex)
            {
                //== error handling ============================================
                errors.Add(cancellationToken.IsCancellationRequested
                    ? "The runtime check was canceled."
                    : $"The runtime check could not start: {ex.Message}");
                //=============================================================
            }
            //=================================================================
        }

        //== output shaping ===================================================
        var isInstalled = pythonExists &&
                          workerExists &&
                          runtimeReportedInstalled;

        return new WatermarkRuntimeStatus(
            isInstalled,
            pythonExists,
            workerExists,
            dependenciesAvailable,
            ffmpegAvailable,
            cudaAvailable,
            errors.Count == 0 ? null : string.Join(" ", errors.Distinct()),
            markerExists,
            markerValid,
            markerOutdated,
            florenceModelAvailable,
            lamaModelAvailable,
            installationMode,
            gpuName,
            pythonVersion,
            missingComponents);
        //=====================================================================
    }

    public async Task<WatermarkRemovalResult> RemoveAsync(
        string inputPath,
        string ffmpegPath,
        WatermarkRemovalOptions options,
        IProgress<WatermarkProgressUpdate>? progress = null,
        IProgress<string>? status = null,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string? outputPath = null;
        string? operationTemporaryDirectory = null;
        int? exitCode = null;

        try
        {
            //== input validation =============================================
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCancelledResult(stopwatch, exitCode);
            }

            if (options is null)
            {
                return CreateFailureResult(stopwatch, "Watermark removal options are required.");
            }

            var optionErrors = options.Validate();
            if (optionErrors.Count > 0)
            {
                return CreateFailureResult(stopwatch, string.Join(" ", optionErrors));
            }

            var normalizedInputPath = TryNormalizeExistingFile(inputPath);
            if (normalizedInputPath is null)
            {
                return CreateFailureResult(stopwatch, "The input media file does not exist.");
            }

            var normalizedFfmpegPath = TryNormalizeExistingFile(ffmpegPath);
            if (normalizedFfmpegPath is null)
            {
                return CreateFailureResult(stopwatch, "FFmpeg is unavailable.");
            }

            if (!File.Exists(_paths.PythonExecutablePath))
            {
                return CreateFailureResult(stopwatch, $"Portable Python was not found at {_paths.PythonExecutablePath}.");
            }

            if (!File.Exists(_paths.WorkerScriptPath))
            {
                return CreateFailureResult(stopwatch, $"The watermark worker was not found at {_paths.WorkerScriptPath}.");
            }

            if (!File.Exists(_paths.InstallationMarkerPath))
            {
                return CreateFailureResult(stopwatch, "The watermark runtime is not installed or needs repair.");
            }
            //=================================================================

            //== processing workspace ========================================
            Directory.CreateDirectory(_paths.HuggingFaceModelDirectory);
            Directory.CreateDirectory(_paths.TorchModelDirectory);
            Directory.CreateDirectory(_paths.LogsDirectory);
            Directory.CreateDirectory(_paths.TemporaryProcessingDirectory);

            operationTemporaryDirectory = Path.Combine(
                _paths.TemporaryProcessingDirectory,
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(operationTemporaryDirectory);

            outputPath = options.PreviewOnly
                ? WatermarkOutputPathGenerator.CreatePreviewOutputPath(
                    normalizedInputPath,
                    _paths.TemporaryProcessingDirectory)
                : options.SelectionPreviewOnly
                    ? WatermarkOutputPathGenerator.CreatePreviewOutputPath(
                        normalizedInputPath,
                        _paths.TemporaryProcessingDirectory)
                    : string.IsNullOrWhiteSpace(options.OutputPath)
                        ? WatermarkOutputPathGenerator.CreateProcessedOutputPath(normalizedInputPath)
                        : Path.GetFullPath(options.OutputPath);

            if (string.Equals(
                    Path.GetFullPath(outputPath),
                    normalizedInputPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CreateFailureResult(stopwatch, "The generated output path cannot overwrite the input file.");
            }
            //=================================================================

            //== worker launch ===============================================
            ReportSafely(status, "Starting watermark worker");
            ReportSafely(
                progress,
                new WatermarkProgressUpdate("checking_runtime", null, "Checking runtime"));

            var runState = new WorkerRunState();
            using var process = new Process
            {
                StartInfo = BuildWorkerStartInfo(
                    normalizedInputPath,
                    outputPath,
                    normalizedFfmpegPath,
                    operationTemporaryDirectory,
                    options)
            };

            process.Start();
            using var cancellationRegistration = cancellationToken.Register(
                () => TryKillProcessTree(process));

            var outputTask = ReadLinesAsync(
                process.StandardOutput,
                line => HandleWorkerOutputLine(line, runState, progress, status, log));
            var errorTask = ReadLinesAsync(
                process.StandardError,
                line => ReportSafely(log, line));

            await AwaitProcessCompletionAsync(
                process,
                outputTask,
                errorTask,
                cancellationToken);
            exitCode = process.ExitCode;
            //=================================================================

            //== result validation ===========================================
            if (cancellationToken.IsCancellationRequested)
            {
                TryDeleteFile(outputPath, log);
                return CreateCancelledResult(stopwatch, exitCode);
            }

            if (runState.Error is not null)
            {
                TryDeleteFile(outputPath, log);
                return CreateFailureResult(
                    stopwatch,
                    GetWorkerUserMessage(runState.Error),
                    exitCode);
            }

            if (exitCode != 0)
            {
                TryDeleteFile(outputPath, log);
                return CreateFailureResult(
                    stopwatch,
                    $"The watermark worker exited with code {exitCode}.",
                    exitCode);
            }

            if (runState.Completions.Count != 1)
            {
                TryDeleteFile(outputPath, log);
                var protocolError = runState.Completions.Count == 0
                    ? "The watermark worker exited without a completion message."
                    : "The watermark worker returned conflicting completion messages.";
                return CreateFailureResult(stopwatch, protocolError, exitCode);
            }

            var completion = runState.Completions[0];
            if (string.IsNullOrWhiteSpace(completion.OutputPath) ||
                !PathsEqual(completion.OutputPath, outputPath))
            {
                TryDeleteFile(outputPath, log);
                return CreateFailureResult(
                    stopwatch,
                    "The worker reported an unexpected output path.",
                    exitCode);
            }

            if (!File.Exists(outputPath))
            {
                return CreateFailureResult(
                    stopwatch,
                    "The worker reported completion, but the output file was not created.",
                    exitCode);
            }

            var outputValidationError = await ValidateCompletedOutputAsync(
                normalizedInputPath,
                outputPath,
                normalizedFfmpegPath,
                options.PreviewOnly || options.SelectionPreviewOnly);
            if (outputValidationError is not null)
            {
                TryDeleteFile(outputPath, log);
                return CreateFailureResult(stopwatch, outputValidationError, exitCode);
            }

            ReportSafely(
                progress,
                new WatermarkProgressUpdate("completed", 100D, "Completed"));
            ReportSafely(status, "Completed");

            return new WatermarkRemovalResult(
                true,
                outputPath,
                exitCode,
                null,
                completion.UsedGpu ?? false,
                stopwatch.Elapsed,
                false,
                runState.Preview?.Detections,
                runState.Preview?.SourceFrame,
                runState.Preview?.SourceWidth,
                runState.Preview?.SourceHeight,
                runState.Preview?.NoRegionDetected ?? false);
            //=================================================================
        }
        catch (Exception ex)
        {
            //== error handling ===============================================
            TryDeleteFile(outputPath, log);

            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCancelledResult(stopwatch, exitCode);
            }

            return CreateFailureResult(stopwatch, ex.Message, exitCode);
            //=================================================================
        }
        finally
        {
            //== cleanup ======================================================
            TryDeleteDirectory(operationTemporaryDirectory, log);
            //=================================================================
        }
    }

    private ProcessStartInfo BuildRuntimeCheckStartInfo(string? ffmpegPath)
    {
        var startInfo = CreateBaseStartInfo(ffmpegPath, temporaryDirectory: null);
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(_paths.WorkerScriptPath);
        startInfo.ArgumentList.Add("--check-runtime");
        return startInfo;
    }

    private ProcessStartInfo BuildWorkerStartInfo(
        string inputPath,
        string outputPath,
        string ffmpegPath,
        string temporaryDirectory,
        WatermarkRemovalOptions options)
    {
        //== process configuration ============================================
        var startInfo = CreateBaseStartInfo(ffmpegPath, temporaryDirectory);
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(_paths.WorkerScriptPath);
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--detection-prompt");
        startInfo.ArgumentList.Add(options.DetectionPrompt.Trim());
        startInfo.ArgumentList.Add("--max-bbox-percent");
        startInfo.ArgumentList.Add(options.MaxBoundingBoxPercent.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--detection-skip");
        startInfo.ArgumentList.Add(options.DetectionSkip.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--fade-in");
        startInfo.ArgumentList.Add(options.FadeInSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--fade-out");
        startInfo.ArgumentList.Add(options.FadeOutSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(options.UseGpuWhenAvailable ? "auto" : "cpu");

        if (options.PreviewFrameIndex.HasValue)
        {
            startInfo.ArgumentList.Add("--frame-index");
            startInfo.ArgumentList.Add(options.PreviewFrameIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (options.MaskPaddingPercent > 0D)
        {
            startInfo.ArgumentList.Add("--mask-padding-percent");
            startInfo.ArgumentList.Add(options.MaskPaddingPercent.ToString("0.###", CultureInfo.InvariantCulture));
        }

        if (options.Regions.Count > 0)
        {
            startInfo.ArgumentList.Add("--regions-json");
            startInfo.ArgumentList.Add(System.Text.Json.JsonSerializer.Serialize(options.Regions));
        }

        if (options.PreviewOnly)
        {
            startInfo.ArgumentList.Add("--preview");
        }

        if (options.SelectionPreviewOnly)
        {
            startInfo.ArgumentList.Add("--selection-preview");
        }

        return startInfo;
        //=====================================================================
    }

    private ProcessStartInfo CreateBaseStartInfo(string? ffmpegPath, string? temporaryDirectory)
    {
        //== process configuration ============================================
        var startInfo = new ProcessStartInfo
        {
            FileName = _paths.PythonExecutablePath,
            WorkingDirectory = _paths.WatermarkAiRuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.Environment["HF_HOME"] = _paths.HuggingFaceModelDirectory;
        startInfo.Environment["TORCH_HOME"] = _paths.TorchModelDirectory;
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["HF_HUB_OFFLINE"] = "1";
        startInfo.Environment["TRANSFORMERS_OFFLINE"] = "1";

        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            startInfo.Environment["VEDITOR_FFMPEG_PATH"] = ffmpegPath;
            var ffmpegDirectory = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
            {
                var existingPath = startInfo.Environment.TryGetValue("PATH", out var configuredPath)
                    ? configuredPath
                    : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(existingPath)
                    ? ffmpegDirectory
                    : $"{ffmpegDirectory}{Path.PathSeparator}{existingPath}";
            }
        }

        if (!string.IsNullOrWhiteSpace(temporaryDirectory))
        {
            startInfo.Environment["TEMP"] = temporaryDirectory;
            startInfo.Environment["TMP"] = temporaryDirectory;
        }

        return startInfo;
        //=====================================================================
    }

    private static void HandleWorkerOutputLine(
        string line,
        WorkerRunState state,
        IProgress<WatermarkProgressUpdate>? progress,
        IProgress<string>? status,
        IProgress<string>? log)
    {
        //== protocol dispatch ================================================
        var message = WatermarkWorkerProtocol.Parse(line);
        if (message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case WatermarkWorkerMessageType.Log:
                ReportSafely(log, message.Message ?? line);
                break;
            case WatermarkWorkerMessageType.Status:
                ReportSafely(
                    progress,
                    new WatermarkProgressUpdate(
                        message.Stage ?? string.Empty,
                        null,
                        message.Message));
                ReportSafely(status, message.Message ?? message.Stage ?? "Processing");
                if (!string.IsNullOrWhiteSpace(message.Message))
                {
                    ReportSafely(log, message.Message);
                }
                break;
            case WatermarkWorkerMessageType.Progress:
                ReportSafely(
                    progress,
                    new WatermarkProgressUpdate(
                        message.Stage ?? string.Empty,
                        message.Percent,
                        message.Message));
                ReportSafely(status, message.Message ?? message.Stage ?? "Processing");
                break;
            case WatermarkWorkerMessageType.Completed:
                state.Completions.Add(message);
                break;
            case WatermarkWorkerMessageType.Error:
                state.Error ??= message;
                ReportSafely(log, FormatWorkerError(message));
                break;
            case WatermarkWorkerMessageType.RuntimeStatus:
                ReportSafely(log, message.Message ?? message.RawLine ?? line);
                break;
            case WatermarkWorkerMessageType.Preview:
                state.Preview = message;
                break;
            default:
                ReportSafely(log, line);
                break;
        }
        //=====================================================================
    }

    private static async Task ReadLinesAsync(TextReader reader, Action<string> handleLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            handleLine(line);
        }
    }

    private static async Task AwaitProcessCompletionAsync(
        Process process,
        Task outputTask,
        Task errorTask,
        CancellationToken cancellationToken)
    {
        //== process lifecycle ===============================================
        var exitTask = process.WaitForExitAsync();
        if (cancellationToken.CanBeCanceled && !exitTask.IsCompleted)
        {
            var cancellationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                () => cancellationSignal.TrySetResult(true));
            if (await Task.WhenAny(exitTask, cancellationSignal.Task) == cancellationSignal.Task)
            {
                TryKillProcessTree(process);
                if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(10))) != exitTask)
                {
                    throw new TimeoutException("The watermark process did not exit after cancellation.");
                }
            }
        }

        await exitTask;
        var readTask = Task.WhenAll(outputTask, errorTask);
        if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(10))) != readTask)
        {
            throw new TimeoutException("The watermark process output streams did not close after exit.");
        }
        await readTask;
        //=====================================================================
    }

    private static WatermarkRemovalResult CreateFailureResult(
        Stopwatch stopwatch,
        string errorMessage,
        int? exitCode = null)
    {
        return new WatermarkRemovalResult(
            false,
            null,
            exitCode,
            errorMessage,
            false,
            stopwatch.Elapsed,
            false);
    }

    private static WatermarkRemovalResult CreateCancelledResult(
        Stopwatch stopwatch,
        int? exitCode)
    {
        return new WatermarkRemovalResult(
            false,
            null,
            exitCode,
            "Watermark processing was canceled.",
            false,
            stopwatch.Elapsed,
            true);
    }

    private static string? TryNormalizeExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(firstPath.Trim().Trim('"')),
                Path.GetFullPath(secondPath.Trim().Trim('"')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatWorkerError(WatermarkWorkerMessage message)
    {
        var errorMessage = string.IsNullOrWhiteSpace(message.Message)
            ? "The watermark worker reported an error."
            : message.Message;

        return string.IsNullOrWhiteSpace(message.Code)
            ? errorMessage
            : $"{message.Code}: {errorMessage}";
    }

    private static string GetWorkerUserMessage(WatermarkWorkerMessage message)
    {
        //== output shaping ===================================================
        var reportedMessage = string.IsNullOrWhiteSpace(message.Message)
            ? "Watermark processing could not be completed."
            : message.Message.Trim();
        return message.Code?.Trim().ToUpperInvariant() switch
        {
            "CUDA_OUT_OF_MEMORY" => "The GPU ran out of memory. Try CPU mode or process smaller media.",
            "CUDA_UNAVAILABLE" => "CUDA is unavailable. Choose CPU mode or repair the CUDA runtime on a compatible NVIDIA system.",
            "DEPENDENCY_MISSING" => "Required AI packages are missing or damaged. Repair the local AI runtime.",
            "MODEL_NOT_AVAILABLE" or "MODEL_LOAD_FAILED" => "The local AI models could not be loaded. Repair the runtime and try again.",
            "FFMPEG_MISSING" => "FFmpeg is required for video cleanup and could not be found.",
            "FFMPEG_FAILED" => "FFmpeg could not finalize the cleaned video or restore its audio.",
            "OUTPUT_NOT_WRITABLE" => "The selected output folder is not writable. Choose another folder.",
            "OUTPUT_NOT_CREATED" => "The cleanup finished without creating a valid output file.",
            "UNSUPPORTED_INPUT" => "This image or video format is not supported for watermark cleanup.",
            "NO_REGION_DETECTED" => reportedMessage,
            _ => reportedMessage
        };
        //=====================================================================
    }

    private static async Task<string?> ValidateCompletedOutputAsync(
        string inputPath,
        string outputPath,
        string ffmpegPath,
        bool previewOnly)
    {
        //== output validation ===============================================
        var outputInfo = new FileInfo(outputPath);
        if (!outputInfo.Exists || outputInfo.Length <= 0)
        {
            return "The worker created an empty output file.";
        }

        if (previewOnly || IsImageExtension(outputPath))
        {
            try
            {
                using var image = Image.FromFile(outputPath);
                return image.Width > 0 && image.Height > 0
                    ? null
                    : "The generated image has invalid dimensions.";
            }
            catch (Exception ex)
            {
                return $"The generated image is corrupted or unsupported: {ex.Message}";
            }
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return "The cleaned video output must use an MP4 container.";
        }

        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, "ffprobe.exe");
        if (!File.Exists(ffprobePath))
        {
            return "FFprobe is required to validate the cleaned video output.";
        }

        try
        {
            var inputStreams = await ProbeMediaAsync(ffprobePath, inputPath);
            var outputStreams = await ProbeMediaAsync(ffprobePath, outputPath);
            if (!outputStreams.Any(stream => stream.CodecType == "video"))
            {
                return "The cleaned output does not contain a valid video stream.";
            }

            var outputVideo = outputStreams.First(stream => stream.CodecType == "video");
            if (!string.Equals(outputVideo.CodecName, "h264", StringComparison.OrdinalIgnoreCase))
            {
                return "The cleaned video was not finalized with the expected H.264 codec.";
            }

            var inputAudioCount = inputStreams.Count(stream => stream.CodecType == "audio");
            var outputAudioCount = outputStreams.Count(stream => stream.CodecType == "audio");
            if (outputAudioCount < inputAudioCount)
            {
                return "One or more source audio tracks are missing from the cleaned video.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"The cleaned video could not be validated with FFprobe: {ex.Message}";
        }
        //=====================================================================
    }

    private static async Task<IReadOnlyList<MediaStreamInfo>> ProbeMediaAsync(
        string ffprobePath,
        string mediaPath)
    {
        //== external service call ============================================
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-show_entries");
        process.StartInfo.ArgumentList.Add("stream=codec_type,codec_name,pix_fmt");
        process.StartInfo.ArgumentList.Add("-of");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add(mediaPath);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error)
                ? $"FFprobe exited with code {process.ExitCode}."
                : error.Trim());
        }

        using var document = JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaStreamInfo>();
        }

        return streams.EnumerateArray()
            .Select(stream => new MediaStreamInfo(
                stream.TryGetProperty("codec_type", out var codecType) ? codecType.GetString() : null,
                stream.TryGetProperty("codec_name", out var codecName) ? codecName.GetString() : null,
                stream.TryGetProperty("pix_fmt", out var pixelFormat) ? pixelFormat.GetString() : null))
            .ToArray();
        //=====================================================================
    }

    private static bool IsImageExtension(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff";
    }

    private static void TryKillProcessTree(Process process)
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
            // Best effort process-tree cleanup during cancellation.
        }
    }

    private static void TryDeleteFile(string? path, IProgress<string>? log)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        //== partial output cleanup ===========================================
        foreach (var candidatePath in new[] { path, $"{path}.veditor-partial" })
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            try
            {
                File.Delete(candidatePath);
            }
            catch (Exception ex)
            {
                ReportSafely(log, $"Could not delete partial watermark output: {ex.Message}");
            }
        }
        //=====================================================================
    }

    private static void TryDeleteDirectory(string? path, IProgress<string>? log)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            ReportSafely(log, $"Could not delete temporary watermark files: {ex.Message}");
        }
    }

    private static void ReportSafely<T>(IProgress<T>? progress, T value)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress.Report(value);
        }
        catch
        {
            // A callback must not destabilize process cleanup.
        }
    }

    private sealed class WorkerRunState
    {
        public List<WatermarkWorkerMessage> Completions { get; } = [];

        public WatermarkWorkerMessage? Error { get; set; }

        public WatermarkWorkerMessage? Preview { get; set; }
    }

    private sealed record MediaStreamInfo(
        string? CodecType,
        string? CodecName,
        string? PixelFormat);
}
