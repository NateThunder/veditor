using System.Text.Json;
using VeditorWindow.Models;

namespace VeditorWindow.Services;

public static class BackgroundRemovalWorkerProtocol
{
    public static BackgroundRemovalWorkerMessage? Parse(string? line)
    {
        //== input validation =================================================
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }
        //=====================================================================

        try
        {
            //== normalization ================================================
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return FromLog(line);
            }

            var type = GetString(root, "type")?.Trim().ToLowerInvariant();
            var message = GetString(root, "message");
            //=================================================================

            //== output shaping ===============================================
            return type switch
            {
                "status" => new(BackgroundRemovalWorkerMessageType.Status, message, RawLine: line),
                "progress" => new(BackgroundRemovalWorkerMessageType.Progress, message, GetDouble(root, "percent"), RawLine: line),
                "completed" => new(BackgroundRemovalWorkerMessageType.Completed, message, 100D, GetString(root, "outputPath"), RawLine: line),
                "runtime_status" => new(
                    BackgroundRemovalWorkerMessageType.RuntimeStatus,
                    message,
                    Installed: GetBoolean(root, "installed"),
                    InstallationMode: GetString(root, "installationMode"),
                    CudaAvailable: GetBoolean(root, "cudaAvailable"),
                    GpuName: GetString(root, "gpuName"),
                    PythonVersion: GetString(root, "pythonVersion"),
                    RembgVersion: GetString(root, "rembgVersion"),
                    MissingComponents: GetStrings(root, "missingComponents"),
                    RawLine: line),
                "error" => new(BackgroundRemovalWorkerMessageType.Error, message, RawLine: line),
                _ => FromLog(line)
            };
            //=================================================================
        }
        catch (JsonException)
        {
            return FromLog(line);
        }
    }

    private static BackgroundRemovalWorkerMessage FromLog(string line) =>
        new(BackgroundRemovalWorkerMessageType.Log, line, RawLine: line);

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static double? GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : null;

    private static IReadOnlyList<string> GetStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }
}
