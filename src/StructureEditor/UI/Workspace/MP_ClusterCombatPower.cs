using StructureEditor.Compatibility;

namespace StructureEditor;

// WPF is now the only CP presentation layer; the compatibility cost model
// and the structure-placement command are shared with the remaining editor.
public partial class MainForm
{
    private MP_CpMotionView MP_cpMotionView;
    private MP_FluidCpSummary MP_cpSummary;

    private Control MP_CreateClusterCpCatalog()
    {
        MP_cpMotionView = new MP_CpMotionView(item =>
        {
            if (item != null) MP_ActivatePaletteBrush(item);
        })
        {
            SwitchLibrary = delta => MP_CycleLibraryPage(delta)
        };
        return new System.Windows.Forms.Integration.ElementHost
        {
            Dock = DockStyle.Fill, BackColor = surface,
            Child = MP_cpMotionView, TabStop = true
        };
    }

    private void MP_RefreshClusterCpCatalog() => MP_cpMotionView?.SetCatalog(catalog);

    private Control MP_CreateClusterCpFooter()
    {
        MP_cpSummary = new MP_FluidCpSummary();
        return new System.Windows.Forms.Integration.ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = MP_StudioTheme.Panel,
            Child = MP_cpSummary, TabStop = false
        };
    }

    private void MP_RefreshClusterCpSummary()
        => MP_cpSummary?.SetRows(MP_ClusterCombatPower.Calculate(catalog, document));
}
