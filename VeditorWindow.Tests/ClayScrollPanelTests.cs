using System.Runtime.ExceptionServices;
using System.Drawing;
using System.Windows.Forms;
using VeditorWindow.UI;

namespace VeditorWindow.Tests;

public sealed class ClayScrollPanelTests
{
    [Theory]
    [InlineData(330)]
    [InlineData(360)]
    public void NativeViewport_UsesVerticalOverflowWithoutHorizontalOverflow(int width)
    {
        RunInSta(() =>
        {
            //== test arrangement =============================================
            using var viewport = CreateViewport(width, 240, contentHeight: 900);
            //=================================================================

            //== assertions ===================================================
            Assert.True(viewport.UsesNativeScrolling);
            Assert.True(viewport.IsVerticalScrollVisible);
            Assert.True(viewport.IsStyledScrollBarVisible);
            Assert.False(viewport.IsHorizontalScrollVisible);
            //=================================================================
        });
    }

    [Fact]
    public void ResetScroll_ReturnsViewportToTop()
    {
        RunInSta(() =>
        {
            //== test arrangement =============================================
            using var viewport = CreateViewport(330, 240, contentHeight: 900);
            viewport.SetVerticalOffset(260);
            Application.DoEvents();
            Assert.True(GetVerticalOffset(viewport) > 0);
            //=================================================================

            //== state transition =============================================
            viewport.ResetScroll();
            Application.DoEvents();
            //=================================================================

            //== assertions ===================================================
            Assert.Equal(0, GetVerticalOffset(viewport));
            //=================================================================
        });
    }

    [Fact]
    public void Relayout_PreservesRequestedVerticalOffset()
    {
        RunInSta(() =>
        {
            //== test arrangement =============================================
            using var viewport = CreateViewport(360, 240, contentHeight: 900);
            viewport.SetVerticalOffset(280);
            Application.DoEvents();
            var expectedOffset = GetVerticalOffset(viewport);
            //=================================================================

            //== dynamic layout change ========================================
            viewport.Height = 300;
            viewport.ContentControls[0].Height = 1040;
            viewport.PerformLayout();
            Application.DoEvents();
            //=================================================================

            //== assertions ===================================================
            Assert.Equal(expectedOffset, GetVerticalOffset(viewport));
            //=================================================================
        });
    }

    [Fact]
    public void Relayout_ClampsOffsetWhenContentBecomesShorter()
    {
        RunInSta(() =>
        {
            //== test arrangement =============================================
            using var viewport = CreateViewport(330, 240, contentHeight: 900);
            viewport.SetVerticalOffset(520);
            Application.DoEvents();
            Assert.True(GetVerticalOffset(viewport) > 0);
            //=================================================================

            //== content contraction ==========================================
            viewport.ContentControls[0].Height = 320;
            viewport.PerformLayout();
            Application.DoEvents();
            //=================================================================

            //== assertions ===================================================
            var maximumOffset = viewport.MaximumVerticalOffset;
            Assert.InRange(GetVerticalOffset(viewport), 0, maximumOffset);
            //=================================================================
        });
    }

    [Fact]
    public void KeyboardCommands_ScrollAndResetNativeViewport()
    {
        RunInSta(() =>
        {
            //== test arrangement =============================================
            using var viewport = CreateViewport(330, 240, contentHeight: 900);
            //=================================================================

            //== keyboard scrolling ===========================================
            Assert.True(viewport.ApplyKeyboardScroll(Keys.Down));
            Assert.Equal(36, GetVerticalOffset(viewport));
            Assert.Equal(36, viewport.StyledScrollOffset);

            Assert.True(viewport.ApplyKeyboardScroll(Keys.PageDown));
            Assert.True(GetVerticalOffset(viewport) > 36);

            Assert.True(viewport.ApplyKeyboardScroll(Keys.End));
            Assert.Equal(
                viewport.MaximumVerticalOffset,
                GetVerticalOffset(viewport));

            Assert.True(viewport.ApplyKeyboardScroll(Keys.Home));
            Assert.Equal(0, GetVerticalOffset(viewport));
            Assert.Equal(0, viewport.StyledScrollOffset);
            Assert.False(viewport.ApplyKeyboardScroll(Keys.Tab));
            //=================================================================
        });
    }

    private static ClayScrollPanel CreateViewport(int width, int height, int contentHeight)
    {
        //== native viewport fixture ==========================================
        var viewport = new ClayScrollPanel
        {
            Padding = new Padding(20, 12, 14, 20),
            Size = new Size(width, height)
        };
        var content = new Panel
        {
            Dock = DockStyle.Top,
            Height = contentHeight,
            Margin = Padding.Empty
        };

        viewport.ContentControls.Add(content);
        viewport.CreateControl();
        content.CreateControl();
        viewport.PerformLayout();
        Application.DoEvents();
        return viewport;
        //=====================================================================
    }

    private static int GetVerticalOffset(ClayScrollPanel viewport)
    {
        return viewport.VerticalOffset;
    }

    private static void RunInSta(Action action)
    {
        //== sta execution =====================================================
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The WinForms scroll-panel test did not complete within 10 seconds.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        //=====================================================================
    }
}
