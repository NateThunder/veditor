using System.Globalization;
using System.Text.Json;
using VeditorWindow.Models;

namespace VeditorWindow.Services;

public static class WatermarkWorkerProtocol
{
    public static WatermarkWorkerMessage? Parse(string? line)
    {
        //== input validation ==================================================
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }
        //=====================================================================

        try
        {
            //== protocol parsing =============================================
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return WatermarkWorkerMessage.FromLog(line);
            }

            var root = document.RootElement;
            var type = ReadString(root, "type")?.Trim().ToLowerInvariant();

            return type switch
            {
                "log" => new WatermarkWorkerMessage(
                    WatermarkWorkerMessageType.Log,
                    Message: ReadString(root, "message") ?? line,
                    RawLine: line),
                "status" => new WatermarkWorkerMessage(
                    WatermarkWorkerMessageType.Status,
                    Stage: ReadString(root, "stage"),
                    Message: ReadString(root, "message"),
                    RawLine: line),
                "progress" => new WatermarkWorkerMessage(
                    WatermarkWorkerMessageType.Progress,
                    Stage: ReadString(root, "stage"),
                    Percent: ClampPercent(ReadDouble(root, "percent")),
                    Message: ReadString(root, "message"),
                    RawLine: line),
                "completed" => new WatermarkWorkerMessage(
                    WatermarkWorkerMessageType.Completed,
                    Stage: ReadString(root, "stage"),
                    Message: ReadString(root, "message"),
                    OutputPath: ReadString(root, "outputPath"),
                    UsedGpu: ReadBoolean(root, "usedGpu"),
                    RawLine: line),
                "error" => new WatermarkWorkerMessage(
                    WatermarkWorkerMessageType.Error,
                    Stage: ReadString(root, "stage"),
                    Message: ReadString(root, "message"),
                    Code: ReadString(root, "code"),
                    RawLine: line),
                "runtime_status" => new WatermarkWorkerMessage(
                    WatermarkWorkerMessageType.RuntimeStatus,
                    Message: ReadString(root, "message"),
                    DependenciesAvailable: ReadBoolean(root, "dependenciesAvailable"),
                    CudaAvailable: ReadBoolean(root, "cudaAvailable"),
                    RawLine: line,
                    Installed: ReadBoolean(root, "installed"),
                    MarkerExists: ReadBoolean(root, "markerExists"),
                    MarkerValid: ReadBoolean(root, "markerValid"),
                    MarkerOutdated: ReadBoolean(root, "markerOutdated"),
                    FlorenceModelAvailable: ReadBoolean(root, "florenceModelAvailable"),
                    LamaModelAvailable: ReadBoolean(root, "lamaModelAvailable"),
                    FfmpegAvailable: ReadBoolean(root, "ffmpegAvailable"),
                    InstallationMode: ReadString(root, "installationMode"),
                    GpuName: ReadString(root, "gpuName"),
                    PythonVersion: ReadString(root, "pythonVersion"),
                    MissingComponents: ReadStringArray(root, "missingComponents")),
                "preview" => ParsePreview(root, line),
                _ => WatermarkWorkerMessage.FromLog(line)
            };
            //=================================================================
        }
        catch (Exception)
        {
            //== malformed protocol fallback ==================================
            return WatermarkWorkerMessage.FromLog(line);
            //=================================================================
        }
    }

    private static WatermarkWorkerMessage ParsePreview(JsonElement root, string line)
    {
        //== protocol parsing ================================================
        var sourceWidth = ReadInt32(root, "sourceWidth");
        var sourceHeight = ReadInt32(root, "sourceHeight");
        var detections = new List<WatermarkDetection>();

        if (sourceWidth > 0 &&
            sourceHeight > 0 &&
            TryGetProperty(root, "detections", out var detectionArray) &&
            detectionArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var detectionElement in detectionArray.EnumerateArray())
            {
                if (detectionElement.ValueKind != JsonValueKind.Object ||
                    !TryReadBoundingBox(detectionElement, sourceWidth.Value, sourceHeight.Value, out var region))
                {
                    continue;
                }

                detections.Add(new WatermarkDetection(
                    region,
                    ReadBoolean(detectionElement, "accepted") ?? false,
                    ReadString(detectionElement, "label")));
            }
        }

        return new WatermarkWorkerMessage(
            WatermarkWorkerMessageType.Preview,
            Message: ReadString(root, "message"),
            OutputPath: ReadString(root, "previewPath"),
            RawLine: line,
            Detections: detections,
            SourceFrame: ReadInt32(root, "sourceFrame"),
            SourceWidth: sourceWidth,
            SourceHeight: sourceHeight,
            NoRegionDetected: ReadBoolean(root, "noRegionDetected") ?? detections.All(item => !item.Accepted));
        //=====================================================================
    }

    private static bool TryReadBoundingBox(
        JsonElement detection,
        int sourceWidth,
        int sourceHeight,
        out WatermarkRegion region)
    {
        region = new WatermarkRegion(0D, 0D, 0D, 0D);
        if (!TryGetProperty(detection, "bbox", out var bbox) ||
            bbox.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = bbox.EnumerateArray()
            .Select(element => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value)
                ? (double?)value
                : null)
            .ToArray();
        if (values.Length != 4 || values.Any(value => !value.HasValue))
        {
            return false;
        }

        var left = Math.Clamp(values[0]!.Value / sourceWidth, 0D, 1D);
        var top = Math.Clamp(values[1]!.Value / sourceHeight, 0D, 1D);
        var right = Math.Clamp(values[2]!.Value / sourceWidth, left, 1D);
        var bottom = Math.Clamp(values[3]!.Value / sourceHeight, top, 1D);
        region = new WatermarkRegion(left, top, right - left, bottom - top);
        return region.IsValid;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
        {
            return double.IsFinite(number) ? number : null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
            double.IsFinite(number))
        {
            return number;
        }

        return null;
    }

    private static bool? ReadBoolean(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out var value))
        {
            return value;
        }

        return null;
    }

    private static int? ReadInt32(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString())
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static double? ClampPercent(double? percent)
    {
        return percent.HasValue
            ? Math.Clamp(percent.Value, 0D, 100D)
            : null;
    }
}
