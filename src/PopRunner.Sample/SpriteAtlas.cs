using PopRunner.Core;

namespace PopRunner.Sample;

public sealed class SpriteAtlas : IDisposable
{
    private readonly Dictionary<int, (Bitmap Normal, Bitmap Mirrored)> images = [];
    public SpriteAtlas(AnimationConfig config)
    {
        try
        {
            foreach (var (frame, sprite) in config.Sprites)
            {
                // GDI+ needs its source stream alive while decoding; own detached copies afterward.
                using var stream = AnimationResources.OpenImage(sprite.Image);
                using var source = new Bitmap(stream);
                var normal = new Bitmap(source);
                Bitmap? mirrored = null;
                try
                {
                    mirrored = new Bitmap(normal);
                    mirrored.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    images.Add(frame, (normal, mirrored));
                }
                catch
                {
                    normal.Dispose();
                    mirrored?.Dispose();
                    throw;
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Bitmap Get(int frame, int direction) => direction > 0 ? images[frame].Mirrored : images[frame].Normal;

    public void Dispose()
    {
        foreach (var (normal, mirrored) in images.Values)
        {
            normal.Dispose();
            mirrored.Dispose();
        }
        images.Clear();
    }
}
