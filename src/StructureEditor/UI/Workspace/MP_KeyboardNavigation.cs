namespace StructureEditor;

// One routing layer for WinForms controls and WPF islands. The editor canvas,
// text input and inspector retain their existing shortcuts.
public partial class MainForm
{
    private enum MP_KeyboardPane { Documents, Sections, Filters, Items, Toolbar, Canvas }
    private MP_KeyboardPane MP_activeKeyboardPane = MP_KeyboardPane.Canvas;

    private ComboBox[] MP_FilterCombos => [scopeBox, kindBox, groupBox, packBox];

    private void MP_InitializeKeyboardNavigation()
    {
        MP_documentMotionBar.MP_NavigateRegion = MP_CycleKeyboardPane;
        MP_documentMotionBar.MP_NavigationFocused =
            () => MP_activeKeyboardPane = MP_KeyboardPane.Documents;
        MP_libraryTabs.MP_NavigateRegion = MP_CycleKeyboardPane;
        MP_libraryTabs.MP_NavigationFocused =
            () => MP_activeKeyboardPane = MP_KeyboardPane.Sections;

        MP_assetView.MP_NavigateRegion = MP_CycleKeyboardPane;
        MP_assetView.MP_NavigationFocused =
            () => MP_activeKeyboardPane = MP_KeyboardPane.Items;
        MP_structureView.MP_NavigateRegion = MP_CycleKeyboardPane;
        MP_structureView.MP_NavigationFocused =
            () => MP_activeKeyboardPane = MP_KeyboardPane.Items;
        MP_cpMotionView.MP_NavigateRegion = MP_CycleKeyboardPane;
        MP_cpMotionView.MP_NavigationFocused =
            () => MP_activeKeyboardPane = MP_KeyboardPane.Items;

        foreach (ComboBox combo in MP_FilterCombos)
        {
            var navigation = (MP_DarkComboBox)combo;
            navigation.MP_NavigationKey = key => MP_HandleComboKeyboard(combo, key, false);
            combo.Enter += (_, _) => MP_activeKeyboardPane = MP_KeyboardPane.Filters;
        }
        if (MP_editLayerSelector is MP_DarkComboBox layers)
        {
            layers.MP_NavigationKey = key => MP_HandleComboKeyboard(layers, key, true);
            layers.Enter += (_, _) => MP_activeKeyboardPane = MP_KeyboardPane.Toolbar;
        }
        canvas.Enter += (_, _) => MP_activeKeyboardPane = MP_KeyboardPane.Canvas;
    }

    private void MP_CycleKeyboardPane(int direction)
    {
        if (direction == 0) return;
        int next = (int)MP_activeKeyboardPane;
        for (int i = 0; i < 6; i++)
        {
            next = (next + Math.Sign(direction) + 6) % 6;
            if (next == (int)MP_KeyboardPane.Filters && MP_libraryTabs.SelectedIndex != 0)
                continue;
            // When coming up from the asset list, land on the bottommost
            // filter ("来源"), not on the first ("集合") at the top.
            if (next == (int)MP_KeyboardPane.Filters && direction < 0)
            {
                ComboBox last = MP_FilterCombos.LastOrDefault(combo =>
                    combo.Enabled && combo.Visible);
                if (last != null)
                {
                    MP_activeKeyboardPane = MP_KeyboardPane.Filters;
                    last.Focus();
                    MP_UiSound.Play(MP_UiSound.Cue.Focus);
                    return;
                }
            }
            MP_FocusKeyboardPane((MP_KeyboardPane)next);
            return;
        }
    }

    private void MP_FocusKeyboardPane(MP_KeyboardPane pane)
    {
        MP_activeKeyboardPane = pane;
        switch (pane)
        {
            case MP_KeyboardPane.Documents:
                MP_documentMotionBar?.FocusNavigation();
                break;
            case MP_KeyboardPane.Sections:
                MP_libraryTabs?.MP_FocusNavigation();
                break;
            case MP_KeyboardPane.Filters:
                MP_FilterCombos.FirstOrDefault(combo => combo.Enabled && combo.Visible)?.Focus();
                break;
            case MP_KeyboardPane.Items:
                switch (MP_libraryTabs.SelectedIndex)
                {
                    case 0: MP_assetView?.FocusNavigation(); break;
                    case 1: MP_structureView?.FocusNavigation(); break;
                    case 2: MP_cpMotionView?.FocusNavigation(); break;
                }
                break;
            case MP_KeyboardPane.Toolbar:
                MP_editLayerSelector?.Focus();
                break;
            case MP_KeyboardPane.Canvas:
                canvas.Focus();
                break;
        }
        MP_UiSound.Play(MP_UiSound.Cue.Focus);
    }

    private bool MP_HandleComboKeyboard(ComboBox combo, Keys key, bool isToolbar)
    {
        if (!combo.Enabled) return false;
        // Native Tab would otherwise leave the dropdown without opening it.
        if (key is Keys.Tab or Keys.Enter)
        {
            bool opening = !combo.DroppedDown;
            combo.DroppedDown = opening;
            MP_UiSound.Play(opening ? MP_UiSound.Cue.Open : MP_UiSound.Cue.Confirm);
            return true;
        }
        if (key == Keys.Escape && combo.DroppedDown)
        {
            combo.DroppedDown = false;
            MP_UiSound.Play(MP_UiSound.Cue.Cancel);
            return true;
        }
        if (key is Keys.W or Keys.S or Keys.Up or Keys.Down)
        {
            int step = key is Keys.W or Keys.Up ? -1 : 1;
            if (combo.DroppedDown)
            {
                if (combo.Items.Count == 0) return true;
                int index = combo.SelectedIndex < 0
                    ? (step < 0 ? combo.Items.Count : -1)
                    : combo.SelectedIndex;
                int next = Math.Clamp(index + step, 0, combo.Items.Count - 1);
                if (next != combo.SelectedIndex)
                {
                    combo.SelectedIndex = next;
                    MP_UiSound.Play(MP_UiSound.Cue.Navigate);
                }
                return true;
            }
            if (isToolbar)
            {
                MP_FocusKeyboardPane(step < 0
                    ? MP_KeyboardPane.Items : MP_KeyboardPane.Canvas);
                return true;
            }
            var filters = MP_FilterCombos;
            int current = Array.IndexOf(filters, combo);
            for (int i = current + step; i >= 0 && i < filters.Length; i += step)
            {
                if (!filters[i].Enabled) continue;
                filters[i].Focus();
                MP_UiSound.Play(MP_UiSound.Cue.Navigate);
                return true;
            }
            MP_FocusKeyboardPane(step < 0
                ? MP_KeyboardPane.Sections : MP_KeyboardPane.Items);
            return true;
        }
        if (!combo.DroppedDown && key is Keys.A or Keys.D)
        {
            MP_FocusKeyboardPane(key == Keys.A
                ? (isToolbar ? MP_KeyboardPane.Documents : MP_KeyboardPane.Sections)
                : (isToolbar ? MP_KeyboardPane.Canvas : MP_KeyboardPane.Items));
            return true;
        }
        if (combo.DroppedDown && key is Keys.Home or Keys.End && combo.Items.Count > 0)
        {
            combo.SelectedIndex = key == Keys.Home ? 0 : combo.Items.Count - 1;
            MP_UiSound.Play(MP_UiSound.Cue.Navigate);
            return true;
        }
        return false;
    }

    private void MP_FocusLibrarySearch()
    {
        switch (MP_libraryTabs.SelectedIndex)
        {
            case 0:
                MP_activeKeyboardPane = MP_KeyboardPane.Filters;
                search.Focus();
                break;
            case 1:
                MP_activeKeyboardPane = MP_KeyboardPane.Items;
                MP_structureView?.FocusSearch();
                break;
            case 2:
                MP_activeKeyboardPane = MP_KeyboardPane.Items;
                MP_cpMotionView?.FocusSearch();
                break;
        }
        MP_UiSound.Play(MP_UiSound.Cue.Focus);
    }

    private void MP_SelectLibraryPageFromKeyboard(int index)
    {
        if (index < 0 || index > 2) return;
        MP_libraryTabs.SelectedIndex = index;
        MP_FocusKeyboardPane(MP_KeyboardPane.Items);
        MP_UiSound.Play(MP_UiSound.Cue.Confirm);
    }

    private bool MP_HandleWorkspaceNavigation(Keys key)
    {
        if (key is Keys.F6 or (Keys.Shift | Keys.F6))
        {
            MP_CycleKeyboardPane(key == Keys.F6 ? 1 : -1);
            return true;
        }
        if (canvas.Focused && key == Keys.A)
        {
            MP_FocusKeyboardPane(MP_KeyboardPane.Items);
            return true;
        }
        if (canvas.Focused && key == Keys.W)
        {
            MP_FocusKeyboardPane(MP_KeyboardPane.Toolbar);
            return true;
        }
        return false;
    }
}
