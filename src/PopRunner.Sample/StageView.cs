using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PopRunner.Core;

namespace PopRunner.Sample;

/// <summary>Native Windows Forms sprite surface. All scene coordinates stay in DOS pixels.</summary>
public sealed class StageView : Control
{
    private AnimationConfig? config;
    private SpriteAtlas? atlas;
    private Bitmap? nativeScene;
    public AnimationEngine? Engine { get; private set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowGuides { get; set; }

    public StageView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(9, 12, 20);
        TabStop = true;
    }

    public void SetScene(AnimationConfig nextConfig, SpriteAtlas nextAtlas, AnimationEngine engine)
    {
        config = nextConfig;
        atlas = nextAtlas;
        Engine = engine;
        if (nativeScene == null || nativeScene.Width != config.Stage.Width || nativeScene.Height != config.Stage.Height)
        {
            nativeScene?.Dispose();
            nativeScene = new Bitmap(config.Stage.Width, config.Stage.Height, PixelFormat.Format32bppPArgb);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (config == null || Engine == null || atlas == null || nativeScene == null || ClientSize.Width == 0 || ClientSize.Height == 0) return;
        using (var sceneGraphics = Graphics.FromImage(nativeScene)) DrawScene(sceneGraphics);
        var stage = config.Stage;
        var scale = Math.Min((double)ClientSize.Width / stage.Width,
            ClientSize.Height / (stage.Height * stage.PixelAspectY));
        var width = Math.Max(1, (int)Math.Round(stage.Width * scale));
        var height = Math.Max(1, (int)Math.Round(stage.Height * stage.PixelAspectY * scale));
        var destination = new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        e.Graphics.DrawImage(nativeScene, destination, 0, 0, stage.Width, stage.Height, GraphicsUnit.Pixel, attributes);
    }

    private void DrawScene(Graphics g)
    {
        var stage = config!.Stage;
        var engine = Engine!;
        var floor = stage.FloorY + 1;
        g.Clear(BackColor);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceOver;
        using var stone = new SolidBrush(Color.FromArgb(18, 23, 34));
        using var mortar = new SolidBrush(Color.FromArgb(27, 34, 48));
        using var arch = new SolidBrush(Color.FromArgb(12, 16, 26));
        using var floorBrush = new SolidBrush(Color.FromArgb(36, 38, 53));
        using var edge = new SolidBrush(Color.FromArgb(102, 96, 113));
        using var floorLine = new Pen(Color.FromArgb(57, 58, 74), 0.5f);
        using var archPen = new Pen(mortar, 2);
        using var gold = new SolidBrush(Color.FromArgb(238, 181, 110));
        for (var column = 0; column < 3; column++)
        {
            var center = stage.Width * (column + 0.5f) / 3;
            using var opening = new GraphicsPath();
            opening.AddLine(center - 30, floor, center - 30, 66);
            opening.AddBezier(center - 30, 66, center - 30, 26, center + 30, 26, center + 30, 66);
            opening.AddLine(center + 30, 66, center + 30, floor);
            opening.CloseFigure();
            g.FillPath(arch, opening);
            g.DrawPath(archPen, opening);
            foreach (var side in new[] { -1, 1 })
            {
                var x = center + side * 34 - 4;
                if (floor > 60) g.FillRectangle(stone, x, 60, 8, floor - 60);
                for (var y = 60; y < floor; y += 12) g.FillRectangle(mortar, x, y, 8, 1);
                g.FillRectangle(mortar, x - 2, 58, 12, 3);
                g.FillRectangle(mortar, x - 2, floor - 3, 12, 3);
            }
        }
        g.FillRectangle(floorBrush, 0, floor, stage.Width, stage.Height - floor);
        g.FillRectangle(edge, 0, floor, stage.Width, 1);
        g.DrawLine(floorLine, 0, floor + 3, stage.Width, floor + 3);
        for (var row = 0; row < 3; row++)
        {
            var y = floor + 5 + row * 9;
            if (y >= stage.Height) break;
            g.DrawLine(floorLine, 0, y, stage.Width, y);
            for (var x = row % 2 == 0 ? 0 : -14; x < stage.Width; x += 28)
                g.DrawLine(floorLine, x, y, x, Math.Min(stage.Height, y + 9));
        }
        if (engine.PotionVisible) DrawPotion(g, engine);
        if (engine.PrimaryVisible) DrawActor(g, engine);
        foreach (var actor in engine.SupportingActors) DrawActor(g, actor);
        if (ShowGuides)
        {
            using var gridPen = new Pen(Color.FromArgb(80, 88, 96, 117), 0.2f);
            using var goldPen = new Pen(gold, 0.5f);
            for (var x = 0; x < stage.Width; x += 16) g.DrawLine(gridPen, x, 0, x, floor);
            for (var y = 0; y < floor; y += 16) g.DrawLine(gridPen, 0, y, stage.Width, y);
            if (engine.PrimaryVisible) DrawActorGuides(g, engine, goldPen, floor);
            foreach (var actor in engine.SupportingActors) DrawActorGuides(g, actor, goldPen, floor);
            g.DrawLine(goldPen, 0, floor, stage.Width, floor);
            using var guideFont = new Font("Consolas", 6, GraphicsUnit.Pixel);
            g.DrawString("DOS ROOT + FRAME BOUNDS", guideFont, gold, 6, 5);
        }
    }

    private void DrawActor(Graphics g, AnimationEngine actor)
    {
        var image = atlas!.Get(actor.Frame, actor.Direction);
        var placement = actor.GetPlacement(image.Width, image.Height);
        g.DrawImage(image, new RectangleF((float)placement.Left, (float)placement.Top, image.Width, image.Height),
            new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
    }

    private void DrawActorGuides(Graphics g, AnimationEngine actor, Pen pen, int floor)
    {
        var image = atlas!.Get(actor.Frame, actor.Direction);
        var placement = actor.GetPlacement(image.Width, image.Height);
        g.DrawRectangle(pen, (float)placement.Left, (float)placement.Top, placement.Width, placement.Height);
        var root = (float)(actor.X * config!.Stage.MovementUnitPixels);
        g.DrawLine(pen, root, floor - 8, root, floor + 6);
        g.DrawLine(pen, root - 4, floor, root + 4, floor);
    }

    private void DrawPotion(Graphics g, AnimationEngine engine)
    {
        var potion = config!.PotionGraphics!;
        DrawPart(potion.BodyFrame, false);
        DrawPart(engine.PotionBubbleFrame!.Value, true);

        void DrawPart(int frame, bool bubbles)
        {
            var image = atlas!.Get(frame, -1);
            var placement = engine.GetPotionPlacement(image.Width, image.Height, bubbles);
            g.DrawImage(image, new RectangleF((float)placement.Left, (float)placement.Top, image.Width, image.Height),
                new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) nativeScene?.Dispose();
        base.Dispose(disposing);
    }
}
