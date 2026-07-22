using System.Drawing;
using VeditorWindow.UI;

namespace VeditorWindow.Tests;

public sealed class WindowChromeControllerTests
{
    private static readonly Size ClientSize = new(1200, 800);
    private static readonly Rectangle MinimizeBounds = new(1050, 0, 50, 46);
    private static readonly Rectangle MaximizeBounds = new(1100, 0, 50, 46);
    private static readonly Rectangle CloseBounds = new(1150, 0, 50, 46);

    [Theory]
    [InlineData(1, 1, (int)WindowChromeHitTarget.TopLeft)]
    [InlineData(1, 798, (int)WindowChromeHitTarget.BottomLeft)]
    [InlineData(1198, 798, (int)WindowChromeHitTarget.BottomRight)]
    [InlineData(1, 400, (int)WindowChromeHitTarget.Left)]
    [InlineData(1198, 400, (int)WindowChromeHitTarget.Right)]
    [InlineData(500, 798, (int)WindowChromeHitTarget.Bottom)]
    [InlineData(500, 20, (int)WindowChromeHitTarget.Caption)]
    [InlineData(500, 200, (int)WindowChromeHitTarget.Client)]
    public void CalculateHitTarget_ResolvesWindowRegions(int x, int y, int expected)
    {
        var result = Calculate(new Point(x, y), maximized: false);

        Assert.Equal((WindowChromeHitTarget)expected, result);
    }

    [Theory]
    [InlineData(1075, 20, (int)WindowChromeHitTarget.Minimize)]
    [InlineData(1125, 20, (int)WindowChromeHitTarget.Maximize)]
    [InlineData(1175, 20, (int)WindowChromeHitTarget.Close)]
    public void CalculateHitTarget_PrioritizesCaptionButtons(int x, int y, int expected)
    {
        Assert.Equal((WindowChromeHitTarget)expected, Calculate(new Point(x, y), maximized: false));
    }

    [Fact]
    public void CalculateHitTarget_DoesNotReturnResizeEdgeWhenMaximized()
    {
        var result = Calculate(new Point(1, 400), maximized: true);

        Assert.Equal(WindowChromeHitTarget.Client, result);
    }

    private static WindowChromeHitTarget Calculate(Point point, bool maximized)
    {
        return WindowChromeController.CalculateHitTarget(
            ClientSize,
            point,
            resizeBorder: 8,
            titleBarHeight: 46,
            MinimizeBounds,
            MaximizeBounds,
            CloseBounds,
            maximized);
    }
}
