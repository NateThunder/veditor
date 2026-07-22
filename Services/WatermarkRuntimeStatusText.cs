using VeditorWindow.Models;

namespace VeditorWindow.Services;

public static class WatermarkRuntimeStatusText
{
    public static string GetPrimaryMessage(WatermarkRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        //== output shaping ===================================================
        if (status.IsInstalled)
        {
            return status.CudaAvailable
                ? $"WatermarkAI is ready with CUDA acceleration{FormatGpuName(status.GpuName)}."
                : "WatermarkAI is ready in CPU mode.";
        }

        if (!status.PythonExists)
        {
            return "The local AI runtime is not installed. Use Install or repair runtime.";
        }

        if (!status.WorkerExists)
        {
            return "The watermark worker is missing. Repair the local AI runtime.";
        }

        if (!status.MarkerExists)
        {
            return "The runtime installation is incomplete. Repair the local AI runtime.";
        }

        if (status.MarkerOutdated)
        {
            return "The local AI runtime needs an update. Run repair before processing media.";
        }

        if (!status.MarkerValid)
        {
            return "The runtime installation record is damaged. Repair the local AI runtime.";
        }

        if (!status.DependenciesAvailable)
        {
            return "Required AI packages are missing or damaged. Repair the local AI runtime.";
        }

        if (!status.FlorenceModelAvailable && !status.LamaModelAvailable)
        {
            return "The Florence-2 and LaMA models are missing. Repair the local AI runtime.";
        }

        if (!status.FlorenceModelAvailable)
        {
            return "The Florence-2 detection model is missing. Repair the local AI runtime.";
        }

        if (!status.LamaModelAvailable)
        {
            return "The LaMA cleanup model is missing. Repair the local AI runtime.";
        }

        if (!status.FfmpegAvailable)
        {
            return "FFmpeg is unavailable. Install FFmpeg or configure its location.";
        }

        if (string.Equals(status.InstallationMode, "CUDA", StringComparison.OrdinalIgnoreCase) &&
            !status.CudaAvailable)
        {
            return "This CUDA runtime cannot access a compatible NVIDIA GPU. Repair it in CPU mode or update the NVIDIA driver.";
        }

        return "The local AI runtime needs repair before processing media.";
        //=====================================================================
    }

    public static string DescribeMissingComponents(WatermarkRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        //== output shaping ===================================================
        var components = status.MissingComponents?
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        return components.Length == 0
            ? GetPrimaryMessage(status)
            : string.Join(", ", components);
        //=====================================================================
    }

    private static string FormatGpuName(string? gpuName)
    {
        return string.IsNullOrWhiteSpace(gpuName) ? string.Empty : $" on {gpuName.Trim()}";
    }
}
