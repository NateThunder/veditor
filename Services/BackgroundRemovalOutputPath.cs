namespace VeditorWindow.Services;

public static class BackgroundRemovalOutputPath
{
    public static string CreateDefault(string sourcePath)
    {
        //== input validation =================================================
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source picture path is required.", nameof(sourcePath));
        }
        //=====================================================================

        //== output shaping ===================================================
        var fullPath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, $"{baseName}-background-removed.png");
        //=====================================================================
    }
}
