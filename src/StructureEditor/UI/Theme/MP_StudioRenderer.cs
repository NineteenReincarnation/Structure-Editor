namespace StructureEditor;

// Shared WinForms menu and toolbar rendering, decoupled from MainForm's
// editor commands and workspace layout. Keep this class presentation-only.
internal sealed class MP_StudioRenderer : ToolStripProfessionalRenderer
    {
        private sealed class ItemFeedback
        {
            internal float Level;
            internal bool Hovering;
        }

        // ToolStripItem is not a Control, so animate its background on the
        // owning ToolStrip without spawning per-item timers.
        private readonly Dictionary<ToolStripItem, ItemFeedback> feedback = new();

        public MP_StudioRenderer() : base(new MP_StudioColors()) { RoundedEdges = false; }

        private float FeedbackLevel(ToolStripItem item, ToolStrip owner)
        {
            if (!feedback.TryGetValue(item, out ItemFeedback state))
            {
                state = new ItemFeedback();
                feedback[item] = state;
                item.Disposed += (_, _) => feedback.Remove(item);
            }
            bool hovered = item.Selected || item.Pressed;
            if (state.Hovering != hovered)
            {
                state.Hovering = hovered;
                float from = state.Level;
                MP_Motion.Tween(owner, "toolbar-" +
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item),
                    from, hovered ? 1f : 0f, value =>
                    {
                        state.Level = value;
                        if (!owner.IsDisposed && !item.IsDisposed)
                            owner.Invalidate(item.Bounds);
                    }, hovered ? 145 : 110);
            }
            return state.Level;
        }

        private void PaintFeedback(ToolStripItemRenderEventArgs e)
        {
            if (e.Item == null || e.ToolStrip == null) return;
            float level = FeedbackLevel(e.Item, e.ToolStrip);
            bool checkedItem = e.Item is ToolStripButton button && button.Checked;
            Rectangle bounds = new(2, 1, Math.Max(1, e.Item.Width - 4),
                Math.Max(1, e.Item.Height - 2));
            // lilt's controls draw a foreground stroke at rest. Focus/press
            // inverts the glyph and the rectangle rather than tinting it teal.
            bool isButton = e.Item is ToolStripButton or ToolStripDropDownButton;
            if (isButton && bounds.Width > 2 && bounds.Height > 2)
            {
                using Pen outline = new(MP_StudioTheme.Accent);
                e.Graphics.DrawRectangle(outline, bounds.Left, bounds.Top,
                    bounds.Width - 1, bounds.Height - 1);
            }
            if (checkedItem || (isButton && level > .001f))
            {
                int alpha = checkedItem ? 255
                    : Math.Clamp((int)(level * 255), 0, 255);
                using SolidBrush inverted = new(Color.FromArgb(alpha, MP_StudioTheme.Active));
                e.Graphics.FillRectangle(inverted, bounds);
            }
            else if (level > .001f)
            {
                using SolidBrush hoverFill = new(Color.FromArgb(
                    Math.Clamp((int)(level * 48), 0, 255), MP_StudioTheme.Text));
                e.Graphics.FillRectangle(hoverFill, bounds);
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            => PaintFeedback(e);

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
            => PaintFeedback(e);

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            => PaintFeedback(e);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) { base.OnRenderToolStripBackground(e); return; }
            Rectangle area = e.AffectedBounds;
            if (area.Width <= 0 || area.Height <= 0) return;
            // lilt-like OLED surface: flat pixels, no desktop gradient sheen.
            using var fill = new SolidBrush(MP_StudioTheme.Toolbar);
            e.Graphics.FillRectangle(fill, area);
            using var line = new Pen(MP_StudioTheme.Divider);
            e.Graphics.DrawLine(line, area.Left, area.Bottom - 1, area.Right, area.Bottom - 1);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool checkedItem = e.Item is ToolStripButton button && button.Checked;
            bool hoveringButton = e.Item is ToolStripButton or ToolStripDropDownButton
                && feedback.TryGetValue(e.Item, out ItemFeedback state)
                && state.Level >= .55f;
            e.TextColor = checkedItem || hoveringButton
                ? MP_StudioTheme.ActiveText : MP_StudioTheme.Text;
            base.OnRenderItemText(e);
        }
    }


internal sealed class MP_StudioColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => MP_StudioTheme.PanelRaised;
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color MenuItemSelected => MP_StudioTheme.Hover;
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => MenuItemSelected;
        public override Color MenuItemPressedGradientEnd => MenuItemSelected;
        public override Color ButtonSelectedGradientBegin => MenuItemSelected;
        public override Color ButtonSelectedGradientEnd => MenuItemSelected;
        public override Color ButtonCheckedGradientBegin => MP_StudioTheme.Active;
        public override Color ButtonCheckedGradientMiddle => ButtonCheckedGradientBegin;
        public override Color ButtonCheckedGradientEnd => ButtonCheckedGradientBegin;
        public override Color MenuBorder => MP_StudioTheme.Secondary;
        public override Color ToolStripBorder => MP_StudioTheme.Divider;
    }
