using System.Drawing.Drawing2D;

namespace StructureEditor;

// Read-only semantic indicator: 0 OK, 1 warning, 2 blocked, 3 loading.
internal sealed class MP_ExportIndicator : Control
{
    private int state = 3;

    internal MP_ExportIndicator()
    {
        Size = new Size(18, 20);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void SetState(int errors, int warnings)
    {
        int next = errors > 0 ? 2 : warnings > 0 ? 1 : 0;
        if (next == state) return;
        state = next;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = state switch
        {
            0 => Color.FromArgb(129, 206, 166),
            1 => Color.FromArgb(218, 185, 120),
            2 => Color.FromArgb(239, 151, 139),
            _ => Color.FromArgb(126, 151, 166)
        };
        using SolidBrush fill = new(color);
        int diameter = Math.Min(13, Math.Min(Width - 3, Height - 3));
        Rectangle rect = new((Width - diameter) / 2,
            (Height - diameter) / 2, diameter, diameter);
        g.FillEllipse(fill, rect);
        if (state == 0 && diameter >= 10)
        {
            using Pen check = new(Color.FromArgb(32, 52, 47), 1.8f);
            g.DrawLines(check, new[]
            {
                new Point(rect.X + 3, rect.Y + diameter / 2),
                new Point(rect.X + 5, rect.Bottom - 4),
                new Point(rect.Right - 3, rect.Y + 3)
            });
        }
        else if (state is 1 or 2 && diameter >= 10)
        {
            using Pen accent = new(Color.FromArgb(45, 42, 39), 1.7f);
            int center = rect.X + diameter / 2;
            g.DrawLine(accent, center, rect.Y + 3, center, rect.Y + diameter - 5);
            g.DrawLine(accent, center, rect.Bottom - 3, center, rect.Bottom - 3);
        }
    }
}
