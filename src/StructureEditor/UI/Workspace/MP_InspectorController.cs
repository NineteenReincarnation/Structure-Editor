namespace StructureEditor;

// Inspector display and commit actions; kept as MainForm partial for compatibility.
public partial class MainForm
{
    private void ShowSelection()
    {
        if (canvas.Selected != null && catalog != null) ShowItem(catalog.Find(canvas.Selected.Kind, canvas.Selected.DefName), canvas.Selected);
        else if (canvas.Brush != null) ShowItem(canvas.Brush, null);
        else
        {
            if (selectedName.Text != "未选择对象") selectedName.Text = "未选择对象";
            if (selectedInfo.Text.Length != 0) selectedInfo.Text = "";
            MP_materialLabel.Visible = materialBox.Visible = false;
            MP_rotationLabel.Visible = rotationBox.Visible = false;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = false;
        }
       
    }
    private void ShowItem(CatalogItem item, PlacedItem placed)
    {
        if (item == null)
        {
            MP_materialLabel.Visible = materialBox.Visible = false;
            MP_rotationLabel.Visible = rotationBox.Visible = false;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = false;
            return;
        }
        updating = true;
        try
        {
            string name = item.Label + "\n" + item.DefName;
            if (selectedName.Text != name) selectedName.Text = name;
            string info = $"来源：{item.Pack}\n占地 {item.Width} × {item.Height}  ·  {(placed == null ? "当前笔刷" : $"已选 {canvas.Selection.Count} 个对象")}";
            if (selectedInfo.Text != info) selectedInfo.Text = info;
            if (shownItem != item)
            {
                materialBox.BeginUpdate(); materialBox.Items.Clear();
                foreach (MaterialItem m in catalog.MaterialsFor(item).OrderBy(m => m.DefName == "Steel" ? 0 : 1).ThenBy(m => m.Label)) materialBox.Items.Add(m);
                materialBox.EndUpdate(); shownItem = item;
            }
            MaterialItem preferred = materialBox.Items.Cast<MaterialItem>().FirstOrDefault(m => m.DefName == (placed?.Stuff ?? canvas.BrushStuff));
            if (materialBox.Items.Count > 0)
            {
                object nextMaterial = preferred ?? materialBox.Items[0];
                if (!ReferenceEquals(materialBox.SelectedItem, nextMaterial))
                    materialBox.SelectedItem = nextMaterial;
            }
            bool hasMaterial = materialBox.Items.Count > 0;
            MP_materialLabel.Visible = materialBox.Visible = hasMaterial;
            materialBox.Enabled = hasMaterial;
            canvas.BrushStuff = hasMaterial
                ? (materialBox.SelectedItem as MaterialItem)?.DefName : null;
            bool canRotate = placed != null && canvas.Selection.Count > 1
                ? canvas.Selection.Any(selected => catalog.Find(selected.Kind, selected.DefName)?.Rotatable == true)
                : item.Rotatable;
            MP_rotationLabel.Visible = rotationBox.Visible = canRotate;
            rotationBox.Enabled = canRotate;
            if (canRotate)
            {
                int rotation = placed?.Rotation ?? canvas.BrushRotation;
                if (rotationBox.SelectedIndex != rotation) rotationBox.SelectedIndex = rotation;
            }
            var supported = placed == null ? new List<PlacedItem>() : canvas.Selection.Where(i => catalog.Find(i.Kind, i.DefName)?.CanBeDormant == true).ToList();
            bool showDormancy = placed == null ? item.CanBeDormant : supported.Count > 0;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = showDormancy;
            dormancyBox.Enabled = showDormancy;
            var states = supported.Select(i => i.StartDormant).Distinct().ToList();
            bool? setting = placed == null ? canvas.BrushDormant : supported.FirstOrDefault()?.StartDormant;
            int nextDormancy = states.Count > 1 ? 3 : setting == true ? 1 : setting == false ? 2 : 0;
            if (dormancyBox.SelectedIndex != nextDormancy)
                dormancyBox.SelectedIndex = nextDormancy;
            dormancyInfo.Text = ""; dormancyInfo.Visible = false;
        }
        finally { updating = false; }
    }
    private static void MP_FitInspectorLabel(Label label, int minimumHeight)
    {
        if (label.Width < 40 || label.IsDisposed) return;
        int measured = TextRenderer.MeasureText(label.Text, label.Font,
            new Size(Math.Max(40, label.ClientSize.Width - 6), 10000),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        int required = Math.Max(minimumHeight, measured + 6);
        if (label.Height != required) label.Height = required;
    }

    private void CommitInspectorEdit()
    {
        if (updating) return;
        canvas.FinishEdit();
        ShowSelection();
    }
}
