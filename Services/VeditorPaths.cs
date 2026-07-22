namespace VeditorWindow.Services;

public sealed class VeditorPaths
{
    public VeditorPaths(string? localApplicationDataRoot = null)
    {
        //== path normalization ================================================
        var configuredRoot = string.IsNullOrWhiteSpace(localApplicationDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataRoot;

        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException("The Local Application Data directory is unavailable.");
        }

        LocalApplicationDataRoot = Path.GetFullPath(configuredRoot);
        VeditorRoot = Path.Combine(LocalApplicationDataRoot, "Veditor");
        WatermarkAiRuntimeDirectory = Path.Combine(VeditorRoot, "WatermarkAI");
        PythonExecutablePath = Path.Combine(WatermarkAiRuntimeDirectory, "python", "python.exe");
        WorkerScriptPath = Path.Combine(WatermarkAiRuntimeDirectory, "worker.py");
        ModelsDirectory = Path.Combine(VeditorRoot, "Models");
        HuggingFaceModelDirectory = Path.Combine(ModelsDirectory, "HuggingFace");
        TorchModelDirectory = Path.Combine(ModelsDirectory, "Torch");
        LogsDirectory = Path.Combine(WatermarkAiRuntimeDirectory, "logs");
        TemporaryProcessingDirectory = Path.Combine(WatermarkAiRuntimeDirectory, "temp");
        InstallationMarkerPath = Path.Combine(WatermarkAiRuntimeDirectory, "installation.json");
        BackgroundRemovalRuntimeDirectory = Path.Combine(VeditorRoot, "BackgroundRemoval");
        BackgroundRemovalPythonExecutablePath = Path.Combine(BackgroundRemovalRuntimeDirectory, "python", "python.exe");
        BackgroundRemovalWorkerScriptPath = Path.Combine(BackgroundRemovalRuntimeDirectory, "worker.py");
        BackgroundRemovalModelsDirectory = Path.Combine(ModelsDirectory, "Rembg");
        BackgroundRemovalDownloadCacheDirectory = Path.Combine(VeditorRoot, "Downloads", "BackgroundRemoval");
        BackgroundRemovalTemporaryDirectory = Path.Combine(BackgroundRemovalRuntimeDirectory, "temp");
        BackgroundRemovalInstallationMarkerPath = Path.Combine(BackgroundRemovalRuntimeDirectory, "installation.json");
        //=====================================================================
    }

    public string LocalApplicationDataRoot { get; }

    public string VeditorRoot { get; }

    public string WatermarkAiRuntimeDirectory { get; }

    public string PythonExecutablePath { get; }

    public string WorkerScriptPath { get; }

    public string ModelsDirectory { get; }

    public string HuggingFaceModelDirectory { get; }

    public string TorchModelDirectory { get; }

    public string LogsDirectory { get; }

    public string TemporaryProcessingDirectory { get; }

    public string InstallationMarkerPath { get; }

    public string BackgroundRemovalRuntimeDirectory { get; }

    public string BackgroundRemovalPythonExecutablePath { get; }

    public string BackgroundRemovalWorkerScriptPath { get; }

    public string BackgroundRemovalModelsDirectory { get; }

    public string BackgroundRemovalDownloadCacheDirectory { get; }

    public string BackgroundRemovalTemporaryDirectory { get; }

    public string BackgroundRemovalInstallationMarkerPath { get; }
}
