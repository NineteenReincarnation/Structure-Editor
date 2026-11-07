using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace StructureEditor;

public partial class StructureCanvas
{
    private List<PlacedItem> previewSource, previewOrder;
    private StructureIndex previewLinks;
    private Bitmap previewImage;
    private RectangleF previewBounds;
    private float previewScale;
    private string previewLayers;
    private int pastePreviewBuildCount;
    private void ClearPastePreview()
    {
        previewImage?.Dispose(); previewImage = null;
        previewSource = previewOrder = null; previewLinks = null;
    }
    private void PreparePastePreview()
    {
        string layers = string.Join("/", VisibleLayers.OrderBy(s => s));
        if (ReferenceEquals(previewSource, stamp) && previewScale == cellSize && previewLayers == layers) return;
        ClearPastePreview(); previewSource = stamp; previewScale = cellSize; previewLayers = layers;
        previewLinks = new StructureIndex(Catalog);
        foreach (var item in stamp) previewLinks.Add(item);
        previewOrder = stamp.Where(i => VisibleLayers.Contains(i.Kind)).OrderBy(i => i.Kind == "Terrain" ? 0 : i.Kind == "Pawn" ? 2 : 1).ThenByDescending(i => i.Z).ToList();
        if (previewOrder.Count == 0) return;
        previewBounds = VisualBounds(previewOrder[0]);
        foreach (var item in previewOrder.Skip(1)) previewBounds = RectangleF.Union(previewBounds, VisualBounds(item));
        double width = Math.Ceiling(previewBounds.Width * cellSize) + 4, height = Math.Ceiling(previewBounds.Height * cellSize) + 4;
        // Bound one cached image to32MiB. Huge or sparse copies still draw visible objects;
        // they never become an empty frame or require an unbounded bitmap allocation.
        if (width > 4096 || height > 4096 || width * height > 8 * 1024 * 1024) return;
        previewImage = new((int)width, (int)height, PixelFormat.Format32bppPArgb);
        PointF previousOrigin = origin;
        try
        {
            using Graphics graphics = Graphics.FromImage(previewImage);
            graphics.Clear(Color.Transparent); graphics.InterpolationMode = InterpolationMode.Bilinear;
            origin = new(2 - previewBounds.Left * cellSize, 2 + previewBounds.Bottom * cellSize);
            foreach (var item in previewOrder) DrawItem(graphics, item, 1, previewLinks);
            pastePreviewBuildCount++;
        }
        finally { origin = previousOrigin; }
    }
    private void DrawPasteContents(Graphics graphics, Point anchor, Rectangle viewport)
    {
        PreparePastePreview();
        if (previewImage != null)
        {
            PointF topLeft = Screen(anchor.X + previewBounds.Left, anchor.Y + previewBounds.Bottom);
            DrawTinted(graphics, previewImage, new(topLeft.X - 2, topLeft.Y - 2, previewImage.Width, previewImage.Height), new(0, 0, previewImage.Width, previewImage.Height), Color.White, .9f);
            return;
        }
        PointF previousOrigin = origin;
        try
        {
            origin = Screen(anchor.X, anchor.Y);
            foreach (var item in previewOrder)
                if (VisualScreenBounds(item).IntersectsWith(viewport)) DrawItem(graphics, item, .9f, previewLinks);
        }
        finally { origin = previousOrigin; }
    }
}
