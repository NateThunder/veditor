using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VeditorWindow.UI;

internal sealed class ClayCheckBox : CheckBox
{
    internal ClayCheckBox()
    {
        AutoSize = true;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        ForeColor = StudioTheme.TextPrimary;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var measured = TextRenderer.MeasureText(Text, Font, proposedSize, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        return new Size(measured.Width + 34 + Padding.Horizontal, Math.Max(28, measured.Height + 8 + Padding.Vertical));
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        //== selected-state indicator ========================================
        base.OnPaintBackground(pevent);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var glyphBounds = new Rectangle(3, Math.Max(3, (Height - 22) / 2), 22, 22);
        ClayDrawing.DrawSurface(
            pevent.Graphics,
            glyphBounds,
            7,
            Checked ? StudioTheme.Accent : StudioTheme.SurfaceInput,
            Checked ? StudioTheme.AccentPressed : StudioTheme.WindowBackgroundDeep,
            !Checked);

        if (Checked)
        {
            using var checkPen = new Pen(Enabled ? StudioTheme.TextPrimary : StudioTheme.TextDisabled, 2.2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            pevent.Graphics.DrawLines(checkPen,
            [
                new Point(glyphBounds.Left + 7, glyphBounds.Top + 11),
                new Point(glyphBounds.Left + 10, glyphBounds.Top + 14),
                new Point(glyphBounds.Left + 16, glyphBounds.Top + 7)
            ]);
        }

        var textBounds = new Rectangle(34, 0, Math.Max(1, Width - 34), Height);
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? ForeColor : StudioTheme.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
        {
            ClayDrawing.DrawFocusRing(pevent.Graphics, ClientRectangle, 8);
        }
        //=====================================================================
    }
}

internal sealed class ClayRadioButton : RadioButton
{
    internal ClayRadioButton()
    {
        AutoSize = true;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        ForeColor = StudioTheme.TextPrimary;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var measured = TextRenderer.MeasureText(Text, Font, proposedSize, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(measured.Width + 32 + Padding.Horizontal, Math.Max(28, measured.Height + 8 + Padding.Vertical));
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        //== selected-state indicator ========================================
        base.OnPaintBackground(pevent);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var glyphBounds = new Rectangle(4, Math.Max(4, (Height - 20) / 2), 20, 20);
        using var outerBrush = new SolidBrush(StudioTheme.SurfaceInput);
        using var outerPen = new Pen(Checked ? StudioTheme.AccentHover : StudioTheme.BorderSoft, 1.5F);
        pevent.Graphics.FillEllipse(outerBrush, glyphBounds);
        pevent.Graphics.DrawEllipse(outerPen, glyphBounds);
        if (Checked)
        {
            var innerBounds = Rectangle.Inflate(glyphBounds, -5, -5);
            using var innerBrush = new SolidBrush(Enabled ? StudioTheme.Accent : StudioTheme.TextDisabled);
            pevent.Graphics.FillEllipse(innerBrush, innerBounds);
        }

        var textBounds = new Rectangle(32, 0, Math.Max(1, Width - 32), Height);
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? ForeColor : StudioTheme.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
        {
            ClayDrawing.DrawFocusRing(pevent.Graphics, ClientRectangle, 8);
        }
        //=====================================================================
    }
}
