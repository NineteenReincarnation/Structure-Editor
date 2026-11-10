using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WB = System.Windows.Media.Imaging;
using WM = System.Windows.Media;

namespace StructureEditor;

// Convert catalog bitmaps directly into small, immutable WPF thumbnails.
// PNG encode + BitmapImage decode was performed synchronously for every new
// row during scroll dragging, stalling the dispatcher and exposing white
// unpainted surfaces under rapid virtualization.
internal static class MP_WpfThumbnailFactory
{
    internal static WB.BitmapSource Create(Bitmap bitmap, Rectangle source, int maxSide = 96)
    {
        if (bitmap == null || source.Width <= 0 || source.Height <= 0) return null;
        float factor = Math.Min(1f, maxSide / (float)Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * factor));
        int height = Math.Max(1, (int)Math.Round(source.Height * factor));

        using var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(scaled))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bitmap, new Rectangle(0, 0, width, height),
                source, GraphicsUnit.Pixel);
        }

        var area = new Rectangle(0, 0, width, height);
        BitmapData data = scaled.LockBits(area, ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] bytes = new byte[checked(stride * height)];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = data.Stride >= 0 ? y : height - 1 - y;
                Marshal.Copy(IntPtr.Add(data.Scan0, sourceRow * data.Stride),
                    bytes, y * stride, stride);
            }
            WB.BitmapSource image = WB.BitmapSource.Create(width, height, 96, 96,
                WM.PixelFormats.Bgra32, null, bytes, stride);
            image.Freeze();
            return image;
        }
        finally { scaled.UnlockBits(data); }
    }
}
