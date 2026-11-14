using System.Drawing.Drawing2D;
using PopRunner.Core;

namespace PopRunner.Sample;

public sealed class FrameStripView : Control
{
    private SpriteAtlas? atlas;
    private int[] frames = [];
    private int direction;
    private int selectedFrame;

    public FrameStripView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(24, 28, 39);
        ForeColor = Color.WhiteSmoke;
    }

    public void SetFrames(AnimationConfig config, SpriteAtlas nextAtlas, AnimationEngine engine)
    {
        atlas = nextAtlas;
        frames = config.Clips[engine.ClipName].Frames.Select(f => f.Frame).Distinct().ToArray();
        direction = engine.Direction;
        selectedFrame = engine.Frame;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (atlas == null || frames.Length == 0) return;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var cellWidth = Math.Max(42, (int)(50 * DeviceDpi / 96f));
        var visible = Math.Max(1, ClientSize.Width / cellWidth);
        var selectedIndex = Array.IndexOf(frames, selectedFrame);
        var first = Math.Clamp(selectedIndex - visible + 1, 0, Math.Max(0, frames.Length - visible));
        for (var index = first; index < frames.Length && index < first + visible; index++)
        {
            var x = (index - first) * cellWidth;
            var selected = frames[index] == selectedFrame;
            var bounds = new Rectangle(x + 1, 1, cellWidth - 3, Math.Max(1, Height - 2));
            using var fill = new SolidBrush(selected ? Color.FromArgb(50, 55, 67) : BackColor);
            g.FillRectangle(fill, bounds);
            ControlPaint.DrawBorder(g, bounds, selected ? SystemColors.Highlight : SystemColors.ControlLight, ButtonBorderStyle.Solid);
            var image = atlas.Get(frames[index], direction);
            var labelHeight = TextRenderer.MeasureText("F000", Font).Height;
            var availableHeight = Math.Max(1, Height - labelHeight - 8);
            var scale = Math.Min((cellWidth - 8f) / image.Width, (float)availableHeight / image.Height);
            var width = Math.Max(1, (int)(image.Width * scale));
            var height = Math.Max(1, (int)(image.Height * scale));
            g.DrawImage(image, new Rectangle(x + (cellWidth - width) / 2, 4 + (availableHeight - height) / 2, width, height),
                0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, $"F{frames[index]:000}", Font,
                new Rectangle(x, Height - labelHeight - 3, cellWidth, labelHeight), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}
