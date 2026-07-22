using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class BackgroundRemovalOutputPathTests
{
    [Fact]
    public void CreateDefault_PreservesDirectoryAndAlwaysUsesPng()
    {
        //== arrange ===========================================================
        var source = Path.Combine(Path.GetTempPath(), "portrait.final.jpeg");
        //=====================================================================

        //== act ===============================================================
        var output = BackgroundRemovalOutputPath.CreateDefault(source);
        //=====================================================================

        //== assertions =======================================================
        Assert.Equal(Path.Combine(Path.GetTempPath(), "portrait.final-background-removed.png"), output);
        //=====================================================================
    }

    [Fact]
    public void CreateDefault_RejectsBlankInput()
    {
        //== assertions =======================================================
        Assert.Throws<ArgumentException>(() => BackgroundRemovalOutputPath.CreateDefault(" "));
        //=====================================================================
    }
}
