using System.Text.Json;
using VeditorWindow.Models;

namespace VeditorWindow.Services;

public static class BackgroundRemovalInstallerProtocol
{
    public static BackgroundRemovalInstallerProgress? ParseProgress(string? line)
    {
        //== input validation =================================================
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
        {
            return null;
        }
        //=====================================================================

        try
        {
            //== protocol parsing =============================================
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (GetString(root, "type") != "installer_progress")
            {
                return null;
            }

            var percent = Math.Clamp(GetDouble(root, "percent") ?? 0D, 0D, 100D);
            return new BackgroundRemovalInstallerProgress(
                percent,
                GetString(root, "stage") ?? "installation",
                GetString(root, "message") ?? "Installing Background Removal runtime.",
                GetLong(root, "bytesReceived"),
                GetLong(root, "bytesTotal"));
            //=================================================================
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var result)
            ? result
            : null;

    private static long? GetLong(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;
}
