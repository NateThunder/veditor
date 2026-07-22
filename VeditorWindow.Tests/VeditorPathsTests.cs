using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class VeditorPathsTests
{
    [Fact]
    public void Constructor_GeneratesExpectedLocalPathsWithoutCreatingThem()
    {
        //== arrange ===========================================================
        var localRoot = Path.Combine(
            Path.GetTempPath(),
            $"VeditorPathsTests-{Guid.NewGuid():N}");
        //=====================================================================

        //== act ===============================================================
        var paths = new VeditorPaths(localRoot);
        //=====================================================================

        //== assertions =======================================================
        var veditorRoot = Path.Combine(Path.GetFullPath(localRoot), "Veditor");
        var runtimeRoot = Path.Combine(veditorRoot, "WatermarkAI");
        var modelsRoot = Path.Combine(veditorRoot, "Models");

        Assert.Equal(Path.GetFullPath(localRoot), paths.LocalApplicationDataRoot);
        Assert.Equal(veditorRoot, paths.VeditorRoot);
        Assert.Equal(runtimeRoot, paths.WatermarkAiRuntimeDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "python", "python.exe"), paths.PythonExecutablePath);
        Assert.Equal(Path.Combine(runtimeRoot, "worker.py"), paths.WorkerScriptPath);
        Assert.Equal(modelsRoot, paths.ModelsDirectory);
        Assert.Equal(Path.Combine(modelsRoot, "HuggingFace"), paths.HuggingFaceModelDirectory);
        Assert.Equal(Path.Combine(modelsRoot, "Torch"), paths.TorchModelDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "temp"), paths.TemporaryProcessingDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "installation.json"), paths.InstallationMarkerPath);
        var backgroundRuntimeRoot = Path.Combine(veditorRoot, "BackgroundRemoval");
        Assert.Equal(backgroundRuntimeRoot, paths.BackgroundRemovalRuntimeDirectory);
        Assert.Equal(Path.Combine(backgroundRuntimeRoot, "python", "python.exe"), paths.BackgroundRemovalPythonExecutablePath);
        Assert.Equal(Path.Combine(backgroundRuntimeRoot, "worker.py"), paths.BackgroundRemovalWorkerScriptPath);
        Assert.Equal(Path.Combine(modelsRoot, "Rembg"), paths.BackgroundRemovalModelsDirectory);
        Assert.Equal(Path.Combine(veditorRoot, "Downloads", "BackgroundRemoval"), paths.BackgroundRemovalDownloadCacheDirectory);
        Assert.Equal(Path.Combine(backgroundRuntimeRoot, "temp"), paths.BackgroundRemovalTemporaryDirectory);
        Assert.Equal(Path.Combine(backgroundRuntimeRoot, "installation.json"), paths.BackgroundRemovalInstallationMarkerPath);
        Assert.False(Directory.Exists(localRoot));
        //=====================================================================
    }
}
