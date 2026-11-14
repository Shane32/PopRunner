using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PopRunner;

/// <summary>A premultiplied, top-down 32-bit DIB presented as a per-pixel-alpha Win32 window.</summary>
internal sealed unsafe class LayeredSurface : IDisposable
{
    private nint memoryDc;
    private nint bitmap;
    private nint previousBitmap;
    private byte* data;
    private readonly int byteCount;
    private bool disposed;

    public int Width { get; }
    public int Height { get; }

    public ReadOnlySpan<byte> Pixels
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new ReadOnlySpan<byte>(data, byteCount);
        }
    }

    public LayeredSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        byteCount = checked(width * height * 4);
        nint screenDc = RenderingNative.GetDC(0);
        if (screenDc == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot acquire the desktop device context.");
        try
        {
            memoryDc = RenderingNative.CreateCompatibleDC(screenDc);
            if (memoryDc == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create the sprite device context.");
            RenderingNative.BitmapInfoHeader header = new()
            {
                Size = (uint)sizeof(RenderingNative.BitmapInfoHeader),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                SizeImage = (uint)byteCount
            };
            void* bits = null;
            bitmap = RenderingNative.CreateDIBSection(screenDc, &header, 0, &bits, 0, 0);
            if (bitmap == 0 || bits == null)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot allocate the sprite bitmap.");
            data = (byte*)bits;
            previousBitmap = RenderingNative.SelectObject(memoryDc, bitmap);
            if (previousBitmap == 0 || previousBitmap == -1)
            {
                previousBitmap = 0;
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot select the sprite bitmap.");
            }
            Clear();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            RenderingNative.ReleaseDC(0, screenDc);
        }
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        new Span<byte>(data, byteCount).Clear();
    }

    /// <summary>Draws with nearest-neighbor scaling, clipping and premultiplied source-over blending.
    /// Scaled dimensions round up; each destination pixel samples floor(position / scale).</summary>
    public void Draw(PngImage image, int left, int top, double scaleX, double scaleY, bool mirror)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        if (!double.IsFinite(scaleX) || scaleX <= 0)
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Image scale must be positive and finite.");
        if (!double.IsFinite(scaleY) || scaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(scaleY), "Image scale must be positive and finite.");
        int startX = (int)Math.Clamp((long)left, 0, Width);
        int startY = (int)Math.Clamp((long)top, 0, Height);
        int endX = (int)Math.Clamp(left + Math.Ceiling(image.Width * scaleX), 0, Width);
        int endY = (int)Math.Clamp(top + Math.Ceiling(image.Height * scaleY), 0, Height);
        ReadOnlySpan<byte> source = image.Pixels;
        for (int y = startY; y < endY; y++)
        {
            int sourceY = Math.Min(image.Height - 1, (int)Math.Floor(((double)y - top) / scaleY));
            byte* destination = data + (y * Width + startX) * 4;
            for (int x = startX; x < endX; x++, destination += 4)
            {
                int sourceX = Math.Min(image.Width - 1, (int)Math.Floor(((double)x - left) / scaleX));
                if (mirror)
                    sourceX = image.Width - 1 - sourceX;
                int offset = (sourceY * image.Width + sourceX) * 4;
                int alpha = source[offset + 3];
                if (alpha == 0)
                    continue;
                if (alpha == 255)
                {
                    destination[0] = source[offset];
                    destination[1] = source[offset + 1];
                    destination[2] = source[offset + 2];
                    destination[3] = 255;
                    continue;
                }
                int inverseAlpha = 255 - alpha;
                destination[0] = (byte)(source[offset] + (destination[0] * inverseAlpha + 127) / 255);
                destination[1] = (byte)(source[offset + 1] + (destination[1] * inverseAlpha + 127) / 255);
                destination[2] = (byte)(source[offset + 2] + (destination[2] * inverseAlpha + 127) / 255);
                destination[3] = (byte)(alpha + (destination[3] * inverseAlpha + 127) / 255);
            }
        }
    }

    public void Present(nint hwnd, int screenX, int screenY)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        nint screenDc = RenderingNative.GetDC(0);
        if (screenDc == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot acquire the desktop device context.");
        try
        {
            RenderingNative.Point destination = new() { X = screenX, Y = screenY };
            RenderingNative.Point source = default;
            RenderingNative.Size size = new() { Width = Width, Height = Height };
            RenderingNative.BlendFunction blend = new() { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (RenderingNative.UpdateLayeredWindow(hwnd, screenDc, &destination, &size, memoryDc, &source, 0, &blend, 2) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot present the transparent sprite window.");
        }
        finally
        {
            RenderingNative.ReleaseDC(0, screenDc);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (memoryDc != 0 && previousBitmap != 0)
            RenderingNative.SelectObject(memoryDc, previousBitmap);
        if (bitmap != 0)
            RenderingNative.DeleteObject(bitmap);
        if (memoryDc != 0)
            RenderingNative.DeleteDC(memoryDc);
        previousBitmap = bitmap = memoryDc = 0;
        data = null;
    }
}

internal static unsafe partial class RenderingNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int PixelsPerMeterX;
        internal int PixelsPerMeterY;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X; internal int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size { internal int Width; internal int Height; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        internal byte BlendOperation;
        internal byte BlendFlags;
        internal byte SourceConstantAlpha;
        internal byte AlphaFormat;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetDC(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage,
        void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint SelectObject(nint dc, nint value);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial int DeleteObject(nint value);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial int DeleteDC(nint dc);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int UpdateLayeredWindow(nint window, nint destinationDc, Point* destination,
        Size* size, nint sourceDc, Point* source, uint colorKey, BlendFunction* blend, uint flags);
}
