using System.ComponentModel;

namespace StructureEditor;

public enum CanvasTool { Place, Select, Rectangle, Erase, Hand, Zoom }

public partial class StructureCanvas : Control
{
    private Catalog catalog;
    private CatalogItem brush;
    private StructureDocument document;
    private StructureIndex index;
    private CanvasTool tool;
    private readonly HashSet<PlacedItem> selection = new(), removals = new();
    private readonly HashSet<Point> visited = new();
    private List<PlacedItem> stamp;
    private Dictionary<PlacedItem, PlacedItem> dragOriginal;
    private List<PlacedItem> selectionBeforeBox;
    private PointF origin, panOrigin;
    private Point lastMouse, panStart, gestureStart, lastGrid;
    private float cellSize = 44;
    private Size previousViewSize;
    private bool panning, painting, erasing, dragging, boxing, rectangle, transaction, spaceHeld;
    private bool boxAdd;
    private string editLayer;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Catalog Catalog { get => catalog; set { catalog = value; RebuildIndex(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public StructureDocument Document { get => document; set { document = value; selection.Clear(); Selected = null; RebuildIndex(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public CatalogItem Brush
    {
        get => brush;
        set { brush = value; BrushDormant = value?.CanBeDormant == true ? false : null; }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string BrushStuff { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int BrushRotation { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool? BrushDormant { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public CanvasTool Tool
    {
        get => tool;
        set
        {
            tool = value; stamp = null; ClearPastePreview();
            if (value is not (CanvasTool.Select or CanvasTool.Hand or CanvasTool.Zoom) && selection.Count > 0) ClearSelection();
            Cursor = value == CanvasTool.Hand ? Cursors.Hand : value == CanvasTool.Zoom ? Cursors.Cross : Cursors.Default;
            ToolChanged?.Invoke(); Invalidate();
        }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool SelectionMode { get => Tool == CanvasTool.Select; set => Tool = value ? CanvasTool.Select : CanvasTool.Place; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string EditLayer { get => editLayer; set { editLayer = value; ClearSelection(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool ShowGrid { get; set; } = true;
    public HashSet<string> VisibleLayers { get; } = new() { "Terrain", "Building", "Pawn" };
    public PlacedItem Selected { get; private set; }
    public IReadOnlyCollection<PlacedItem> Selection => selection;
    public bool HasStamp => stamp?.Count > 0;
    public event Action BeforeChange, Changed, SelectionChanged, ToolChanged;
    public event Action<string> Hint;
    protected virtual Keys InputModifiers => ModifierKeys;

    public StructureCanvas()
    {
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        DoubleBuffered = true; BackColor = MP_StudioTheme.Canvas; Dock = DockStyle.Fill; TabStop = true;
        Resize += (_, _) =>
        {
            if (origin == PointF.Empty) CenterView();
            else if (!previousViewSize.IsEmpty) origin = new(origin.X + (Width - previousViewSize.Width) / 2f, origin.Y + (Height - previousViewSize.Height) / 2f);
            previousViewSize = ClientSize; Invalidate();
        };
    }
    private void RebuildIndex()
    {
        index = new StructureIndex(Catalog);
        if (Document != null && Catalog != null) foreach (var item in Document.Items) index.Add(item);
        removals.Clear(); solidDirty = true; ClearTerrainCache(); ClearPastePreview(); Invalidate();
    }
    internal (PointF Origin, float Scale) CaptureView() => (origin, cellSize);
    internal void RestoreView(PointF position, float scale) { origin = position; cellSize = scale; Invalidate(); }
    public void CenterView() => CenterItems(Document?.Items.Where(i => VisibleLayers.Contains(i.Kind)));
    public void CenterSelection() => CenterItems(selection.Count > 0 ? selection : Document?.Items.Where(i => VisibleLayers.Contains(i.Kind)));
    public void GoToOrigin() { origin = new(Width / 2f, Height / 2f); Invalidate(); }
    private void CenterItems(IEnumerable<PlacedItem> items)
    {
        var visible = items?.ToList();
        if (visible?.Count > 0 && Catalog != null)
        {
            RectangleF bounds = VisualBounds(visible[0]);
            foreach (var item in visible.Skip(1)) bounds = RectangleF.Union(bounds, VisualBounds(item));
            cellSize = Math.Clamp(Math.Min(Math.Max(1, Width - 96f) / Math.Max(1, bounds.Width), Math.Max(1, Height - 96f) / Math.Max(1, bounds.Height)), 1, 80);
            origin = new(Width / 2f - (bounds.Left + bounds.Width / 2) * cellSize, Height / 2f + (bounds.Top + bounds.Height / 2) * cellSize);
        }
        else origin = new(Width / 2f, Height / 2f);
        Invalidate();
    }
    private RectangleF VisualBounds(PlacedItem item)
    {
        Rectangle cells = StructureDocument.Footprint(item, Catalog);
        RectangleF result = new(cells.Left - .5f, cells.Top - .5f, cells.Width, cells.Height);
        CatalogItem entry = Catalog.Find(item.Kind, item.DefName);
        if (entry == null || item.Kind == "Terrain") return result;
        float w = entry.DrawX, h = entry.DrawZ;
        if (item.Rotation % 2 != 0 && entry.Rotatable && entry.GraphicClass != "Graphic_Multi" && !entry.Linked) (w, h) = (h, w);
        float x = cells.Left + (cells.Width - 1) / 2f, z = cells.Top + (cells.Height - 1) / 2f;
        result = RectangleF.Union(result, new(x + entry.OffsetX - w / 2, z + entry.OffsetZ - h / 2, w, h));
        if (!string.IsNullOrEmpty(entry.TopTexture)) result = RectangleF.Union(result, new(x - entry.TopSize / 2, z - entry.TopSize / 2, entry.TopSize, entry.TopSize));
        return result;
    }
    internal RectangleF VisualScreenBounds(PlacedItem item)
    {
        var bounds = VisualBounds(item); PointF corner = Screen(bounds.Left, bounds.Bottom);
        return new(corner.X, corner.Y, bounds.Width * cellSize, bounds.Height * cellSize);
    }
    private Rectangle BoundsFor(IEnumerable<PlacedItem> items)
    {
        Rectangle bounds = Rectangle.Empty; bool first = true;
        foreach (var item in items) { var r = StructureDocument.Footprint(item, Catalog); bounds = first ? r : Rectangle.Union(bounds, r); first = false; }
        return bounds;
    }
    private Point Grid(Point p) => new((int)Math.Floor((p.X - origin.X) / cellSize + .5f), (int)Math.Floor((origin.Y - p.Y) / cellSize + .5f));
    private PointF Screen(float x, float z) => new(origin.X + x * cellSize, origin.Y - z * cellSize);
    internal Point ClientFromCell(Point cell) => Point.Round(Screen(cell.X, cell.Y));
    private RectangleF ScreenRect(Rectangle grid)
    {
        PointF p = Screen(grid.X - .5f, grid.Bottom - .5f);
        return new(p.X, p.Y, grid.Width * cellSize, grid.Height * cellSize);
    }
    private static Rectangle Between(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
    private bool Editable(PlacedItem item) => VisibleLayers.Contains(item.Kind) && (EditLayer == null || EditLayer == item.Kind);
    private PlacedItem At(Point cell, bool respectLayer = true)
    {
        string layer = respectLayer ? EditLayer : null;
        var item = index.Hit(cell, layer);
        if (item != null && VisibleLayers.Contains(item.Kind)) return item;
        return (layer == null || layer == "Terrain") && VisibleLayers.Contains("Terrain") ? index.Terrain.GetValueOrDefault(cell) : null;
    }
    public bool CanPlace(PlacedItem item, PlacedItem except = null) => index.CanPlace(new[] { item }, except == null ? null : new() { except }, item.Kind == "Terrain" && except == null, replaceSameBuildings: except == null);
    private void SelectItems(IEnumerable<PlacedItem> items)
    {
        ICollection<PlacedItem> next = items as ICollection<PlacedItem> ?? items.ToArray();
        if (selection.SetEquals(next)) return;
        selection.Clear();
        foreach (var item in next) selection.Add(item);
        Selected = selection.LastOrDefault();
        if (selection.Count > 0) MP_UiSound.Play(MP_UiSound.Cue.Select);
        SelectionChanged?.Invoke();
        Invalidate();
    }
    public void ClearSelection()
    {
        if (selection.Count != 0 || Selected != null)
            SelectItems(Array.Empty<PlacedItem>());
    }
    public void SelectAll() { Tool = CanvasTool.Select; SelectItems(Document.Items.Where(Editable)); }
    // Alt + left click selects all placements of the same XML thing definition,
    // not the same material, rotation or current selection rectangle.
    public int MP_SelectMatchingDefinition(PlacedItem item)
    {
        if (item == null || Document == null) return 0;
        Tool = CanvasTool.Select;
        SelectItems(Document.Items.Where(candidate =>
            candidate.Kind == item.Kind &&
            string.Equals(candidate.DefName, item.DefName, StringComparison.Ordinal)));
        return selection.Count;
    }
    public void SelectArea(Rectangle bounds, bool additive = false)
    {
        var items = Document.Items.Where(i => Editable(i) && bounds.IntersectsWith(StructureDocument.Footprint(i, Catalog))).ToList();
        if (additive) items.InsertRange(0, selection);
        SelectItems(items);
    }
    public void Cancel()
    {
        if (dragging && dragOriginal != null)
        {
            foreach (var item in selection) { index.Remove(item); MarkChanged(item); }
            foreach (var pair in dragOriginal) { CopyState(pair.Key, pair.Value); index.Add(pair.Key); MarkChanged(pair.Key); }
        }
        bool committedStroke = transaction && !dragging;
        EndGesture(); stamp = null; ClearPastePreview(); ClearSelection();
        if (committedStroke) Hint?.Invoke("已结束当前笔画；可用 Ctrl+Z 撤销。");
    }
    public void FinishEdit() => EndGesture();
    private void BeforeEdit()
    {
        if (transaction) return;
        BeforeChange?.Invoke(); transaction = true;
    }
    private void EndGesture()
    {
        if (removals.Count > 0) { Document.Items.RemoveAll(removals.Contains); removals.Clear(); }
        bool changed = transaction;
        transaction = painting = erasing = dragging = boxing = rectangle = panning = false;
        Capture = false; dragOriginal = null; visited.Clear();
        if (changed) { Changed?.Invoke(); SelectionChanged?.Invoke(); }
        Invalidate();
    }
    private void RemoveItem(PlacedItem item)
    {
        BeforeEdit(); index.Remove(item); removals.Add(item); selection.Remove(item); MarkChanged(item);
        if (Selected == item) Selected = selection.LastOrDefault();
    }
    private void AddItem(PlacedItem item)
    {
        BeforeEdit(); Document.Items.Add(item); index.Add(item); MarkChanged(item);
    }
    private void PaintAt(Point cell)
    {
        if (!visited.Add(cell)) return;
        if (erasing || Tool == CanvasTool.Erase) { var hit = At(cell, respectLayer: !erasing); if (hit != null) RemoveItem(hit); return; }
        if (HasStamp) { PlaceStamp(cell); return; }
        if (Brush == null || !VisibleLayers.Contains(Brush.Kind)) return;
        var item = new PlacedItem { Kind = Brush.Kind, DefName = Brush.DefName, Stuff = BrushStuff, Rotation = BrushRotation, X = cell.X, Z = cell.Y, StartDormant = Brush.CanBeDormant ? BrushDormant : null };
        if (!CanPlace(item)) { Hint?.Invoke("占地重叠，未放置。"); return; }
        var replaced = index.ReplacedBuildings(new[] { item });
        if (replaced.Count == 1)
        {
            var existing = replaced.Single();
            if (existing.X == item.X && existing.Z == item.Z && existing.Rotation == item.Rotation && existing.Stuff == item.Stuff
                && existing.StartDormant == item.StartDormant && (existing.FrameworkModifiers ?? new()).SequenceEqual(item.FrameworkModifiers)) return;
        }
        foreach (var oldBuilding in replaced) RemoveItem(oldBuilding);
        if (item.Kind == "Terrain" && index.Terrain.TryGetValue(cell, out var old))
        {
            if (old.DefName == item.DefName) return;
            RemoveItem(old);
        }
        AddItem(item);
    }
    private void PaintLine(Point from, Point to)
    {
        int x = from.X, z = from.Y, dx = Math.Abs(to.X - x), dz = Math.Abs(to.Y - z), sx = x < to.X ? 1 : -1, sz = z < to.Y ? 1 : -1, error = dx - dz;
        while (true)
        {
            PaintAt(new(x, z)); if (x == to.X && z == to.Y) break;
            int twice = 2 * error; if (twice > -dz) { error -= dz; x += sx; } if (twice < dx) { error += dx; z += sz; }
        }
    }
    public void PaintRectangle(Rectangle cells)
    {
        if (Brush?.Kind != "Terrain" && Tool != CanvasTool.Erase) { Hint?.Invoke("矩形铺设用于地板；建筑请使用放置工具。"); return; }
        if ((long)cells.Width * cells.Height > 65536) { Hint?.Invoke("单次矩形最多 65536 格，请分区域铺设。"); return; }
        visited.Clear(); foreach (Point cell in cells.GetCells()) PaintAt(cell); EndGesture();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus(); lastMouse = e.Location;
        if (e.Button == MouseButtons.Middle || (spaceHeld || Tool == CanvasTool.Hand) && e.Button == MouseButtons.Left)
        { panning = true; panStart = e.Location; panOrigin = origin; Capture = true; return; }
        if (Document == null || Catalog == null) return;
        if (Tool == CanvasTool.Zoom && e.Button == MouseButtons.Left) { ZoomAt(e.Location, (InputModifiers & Keys.Alt) != 0 ? 1 / 1.2f : 1.2f); return; }
        gestureStart = lastGrid = Grid(e.Location); visited.Clear();
        if (e.Button == MouseButtons.Right)
        {
            if (selection.Count > 0) { DeleteSelected(); return; }
            erasing = true; Capture = true; PaintAt(lastGrid); Invalidate(); return;
        }
        if (e.Button != MouseButtons.Left) return;
        if ((InputModifiers & Keys.Alt) != 0)
        {
            PlacedItem matching = At(lastGrid);
            if (matching != null)
            {
                int count = MP_SelectMatchingDefinition(matching);
                Hint?.Invoke($"已选中 {count} 个 {matching.DefName}（当前 XML 中同类对象）");
                MP_UiSound.Play(MP_UiSound.Cue.Confirm);
            }
            return;
        }
        if (e.Clicks >= 2 && InputModifiers == Keys.None && PickBrushAt(lastGrid)) return;
        bool shift = (InputModifiers & Keys.Shift) != 0, ctrl = (InputModifiers & Keys.Control) != 0;
        if (shift || ctrl || Tool == CanvasTool.Select)
        {
            var hit = At(lastGrid);
            if (shift || hit == null)
            {
                boxing = true; boxAdd = shift || ctrl; selectionBeforeBox = boxAdd ? selection.ToList() : new();
                Capture = true; Invalidate(); return;
            }
            if (ctrl) { Tool = CanvasTool.Select; var next = selection.ToList(); if (!next.Remove(hit)) next.Add(hit); SelectItems(next); return; }
            if (!selection.Contains(hit)) SelectItems(new[] { hit });
            Selected = hit; SelectionChanged?.Invoke();
            dragging = true; dragOriginal = selection.ToDictionary(i => i, i => i.Copy()); Capture = true;
        }
        else if (Tool == CanvasTool.Rectangle)
        {
            if (Brush?.Kind != "Terrain") { Hint?.Invoke("先选择地板素材，再拖动矩形。"); return; }
            rectangle = true; Capture = true;
        }
        else
        {
            painting = true; Capture = true; PaintAt(lastGrid);
            MP_UiSound.Play(MP_UiSound.Cue.Place);
        }
        Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); lastMouse = e.Location;
        if (panning) { origin = new(panOrigin.X + e.X - panStart.X, panOrigin.Y + e.Y - panStart.Y); Invalidate(); return; }
        if (Catalog == null || Document == null) return;
        Point grid = Grid(e.Location);
        if (grid == lastGrid) return;
        Hint?.Invoke($"({grid.X}, {grid.Y})  ·  {cellSize / 44:P0}  ·  选中 {selection.Count}  ·  " + (HasStamp ? "Q 逆时针 / E 顺时针旋转 · 左键放置" : "Shift 框选 / 空格拖动平移"));
        if (erasing || painting && !HasStamp && (Brush?.Kind == "Terrain" || Brush?.Width == 1 && Brush?.Height == 1)) PaintLine(lastGrid, grid);
        if (dragging && dragOriginal != null)
        {
            var next = dragOriginal.Values.Select(i => { var c = i.Copy(); c.X += grid.X - gestureStart.X; c.Z += grid.Y - gestureStart.Y; return c; }).ToList();
            Transform(next, finish: false);
        }
        lastGrid = grid; Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && boxing)
        {
            Rectangle area = Between(gestureStart, Grid(e.Location));
            var items = Document.Items.Where(i => Editable(i) && area.IntersectsWith(StructureDocument.Footprint(i, Catalog))).ToList();
            if (boxAdd) items.InsertRange(0, selectionBeforeBox);
            Tool = CanvasTool.Select;
            SelectItems(items);
        }
        if (e.Button == MouseButtons.Left && rectangle)
        {
            PaintRectangle(Between(gestureStart, Grid(e.Location)));
            MP_UiSound.Play(MP_UiSound.Cue.Place);
        }
        EndGesture();
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e); ZoomAt(e.Location, e.Delta > 0 ? 1.15f : 1 / 1.15f);
    }
    private void ZoomAt(Point position, float factor)
    {
        float old = cellSize; cellSize = Math.Clamp(cellSize * factor, 1, 112);
        origin = new(position.X + (origin.X - position.X) * cellSize / old, position.Y + (origin.Y - position.Y) * cellSize / old); Invalidate();
    }
    public void ZoomAtCenter(float factor, bool actualSize = false) => ZoomAt(new(Width / 2, Height / 2), actualSize ? 44 / cellSize : factor);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { spaceHeld = true; Cursor = Cursors.Hand; e.Handled = true; } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { spaceHeld = false; Cursor = Cursors.Default; } base.OnKeyUp(e); }
    protected override void OnLostFocus(EventArgs e) { spaceHeld = false; Cursor = Cursors.Default; base.OnLostFocus(e); }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && (painting || erasing || dragging || boxing || rectangle || panning)) EndGesture();
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space || base.IsInputKey(keyData);
    private static void CopyState(PlacedItem target, PlacedItem source) { target.X = source.X; target.Z = source.Z; target.Rotation = source.Rotation; target.Stuff = source.Stuff; target.StartDormant = source.StartDormant; }
    private bool Transform(List<PlacedItem> next, bool finish = true)
    {
        var originals = selection.ToList();
        if (!index.CanPlace(next, selection)) { Hint?.Invoke("与其他对象冲突，整组修改未应用。"); return false; }
        if (originals.Zip(next).All(p => p.First.X == p.Second.X && p.First.Z == p.Second.Z && p.First.Rotation == p.Second.Rotation && p.First.Stuff == p.Second.Stuff && p.First.StartDormant == p.Second.StartDormant)) return true;
        BeforeEdit();
        foreach (var original in originals) { index.Remove(original); MarkChanged(original); }
        for (int i = 0; i < next.Count; i++) { CopyState(originals[i], next[i]); index.Add(originals[i]); MarkChanged(originals[i]); }
        if (finish) EndGesture(); Invalidate(); return true;
    }
    private List<PlacedItem> Rotated(IEnumerable<PlacedItem> items, Point pivot, int turns)
    {
        return items.Select(i =>
        {
            var n = i.Copy(); int x = n.X - pivot.X, z = n.Z - pivot.Y;
            for (int t = 0; t < turns; t++) (x, z) = (z, -x);
            n.X = pivot.X + x; n.Z = pivot.Y + z;
            if (Catalog.Find(n.Kind, n.DefName)?.Rotatable == true) n.Rotation = (n.Rotation + turns) % 4;
            return n;
        }).ToList();
    }
    public void Rotate() => Rotate(1);
    public void Rotate(int turns)
    {
        turns = (turns % 4 + 4) % 4;
        if (HasStamp) stamp = Rotated(stamp, Point.Empty, turns);
        else if (selection.Count > 0) Transform(Rotated(selection, new(Selected.X, Selected.Z), turns));
        else if (Brush?.Rotatable == true) { BrushRotation = (BrushRotation + turns) % 4; SelectionChanged?.Invoke(); }
        Invalidate();
    }
    public void DeleteSelected()
    {
        foreach (var item in selection.ToList()) RemoveItem(item);
        Selected = null; EndGesture(); SelectionChanged?.Invoke();
    }
    public void MoveSelected(int dx, int dz)
    {
        if (selection.Count == 0) return;
        Transform(selection.Select(i => { var n = i.Copy(); n.X += dx; n.Z += dz; return n; }).ToList());
    }
    public void ApplyProperties(int x, int z, int rotation, string stuff, bool? startDormant = null, bool applyDormancy = false)
    {
        if (Selected == null) return;
        int dx = x - Selected.X, dz = z - Selected.Z, turns = (rotation - Selected.Rotation + 4) % 4;
        var next = Rotated(selection, new(Selected.X, Selected.Z), turns);
        foreach (var item in next)
        {
            item.X += dx; item.Z += dz;
            var entry = Catalog.Find(item.Kind, item.DefName);
            if (entry?.MaterialCategories.Count > 0 && Catalog.MaterialsFor(entry).Any(m => m.DefName == stuff)) item.Stuff = stuff;
            if (applyDormancy && entry?.CanBeDormant == true) item.StartDormant = startDormant;
        }
        Transform(next);
    }
    public void SetSelectionDormancy(bool? dormant)
    {
        if (Selected == null) return;
        Transform(selection.Select(i => { var copy = i.Copy(); if (Catalog.Find(i.Kind, i.DefName)?.CanBeDormant == true) copy.StartDormant = dormant; return copy; }).ToList());
    }
    public void SetSelectionMaterial(string stuff)
    {
        if (Selected == null) return;
        Transform(selection.Select(i => { var copy = i.Copy(); var entry = Catalog.Find(i.Kind, i.DefName);
            if (entry?.MaterialCategories.Count > 0 && Catalog.MaterialsFor(entry).Any(m => m.DefName == stuff)) copy.Stuff = stuff;
            return copy; }).ToList());
    }
    public void SetSelectionRotation(int rotation)
    {
        if (Selected != null) Transform(Rotated(selection, new(Selected.X, Selected.Z), (rotation - Selected.Rotation + 4) % 4));
    }
    public List<PlacedItem> CopySelection()
    {
        if (Selected == null) return new();
        return selection.Select(i => { var n = i.Copy(); n.X -= Selected.X; n.Z -= Selected.Z; return n; }).ToList();
    }
    public void SetStamp(IEnumerable<PlacedItem> items)
    {
        var copies = items.Select(i => i.Copy()).ToList(); Tool = CanvasTool.Place; stamp = copies; ClearSelection(); ToolChanged?.Invoke(); Invalidate();
    }
    private List<PlacedItem> StampAt(Point cell) => stamp.Select(i => { var n = i.Copy(); n.X += cell.X; n.Z += cell.Y; return n; }).ToList();
    public bool PlaceStamp(Point cell)
    {
        if (!HasStamp) return false;
        var next = StampAt(cell);
        if (!index.CanPlace(next, replaceTerrain: true, replaceSameBuildings: true)) { Hint?.Invoke("粘贴位置冲突，未放置。"); return false; }
        // Validate the entire stamp first, then replace whole old buildings in one undo step.
        foreach (var oldBuilding in index.ReplacedBuildings(next)) RemoveItem(oldBuilding);
        foreach (var item in next)
        {
            if (item.Kind == "Terrain" && index.Terrain.TryGetValue(new(item.X, item.Z), out var old)) RemoveItem(old);
            AddItem(item);
        }
        SelectItems(next); if (!painting) EndGesture(); return true;
    }
    public void Eyedropper()
    {
        PickBrushAt(Grid(lastMouse));
    }
    private bool PickBrushAt(Point cell)
    {
        var item = At(cell, respectLayer: false); if (item == null) return false;
        FinishEdit();
        Brush = Catalog.Find(item.Kind, item.DefName); BrushStuff = item.Stuff; BrushRotation = item.Rotation; BrushDormant = item.StartDormant;
        Tool = CanvasTool.Place; ClearSelection(); SelectionChanged?.Invoke();
        Focus(); Hint?.Invoke("已吸取 " + (Brush?.Label ?? item.DefName) + "，左键继续放置。");
        return true;
    }
}
