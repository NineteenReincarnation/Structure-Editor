namespace StructureEditor;

// Offline validation of the editor shell, tabs and navigation.
// Entry point only dispatches this; no test behavior in normal startup.
internal static class MP_UiSmokeChecks
{
    internal static void Run()
    {

                // No game installation needed: safeguard editor document
                // serialization independently of the external game catalog.
                MP_EditorRegressionChecks.Run();
                if (Math.Abs(MP_Motion.EaseOut(0f)) > 0.00001f ||
                    Math.Abs(MP_Motion.EaseOut(1f) - 1f) > 0.00001f ||
                    Math.Abs(MP_Motion.EaseOut(.5f) - .875f) > 0.00001f ||
                    MP_Motion.Blend(Color.Black, Color.White, 1f) != Color.FromArgb(255, 255, 255))
                    throw new InvalidOperationException("Motion easing/color contract failed.");
                // Ported lilt 240 Hz spring integration must converge and
                // preserve velocity when its target changes mid-flight.
                var spring = new MP_LiltValue(0, MP_LiltSpring.Default);
                spring.Set(100);
                for (int sample = 0; sample < 240; sample++)
                    spring.Step(1d / 240);
                if (!double.IsFinite(spring.Current) || Math.Abs(spring.Current - 100) > .2)
                    throw new InvalidOperationException("lilt spring physics did not converge.");
                if (MP_LibraryNavigation.Step(-1, -1, 4) != 3
                    || MP_LibraryNavigation.Step(-1, 1, 4) != 0
                    || MP_LibraryNavigation.Step(3, 1, 4) != 3
                    || MP_LibraryNavigation.Step(0, -1, 4) != 0
                    || MP_LibraryNavigation.StepPage(0, 1, 3) != 1
                    || MP_LibraryNavigation.StepPage(2, 1, 3) != 2)
                    throw new InvalidOperationException(
                        "WASD library navigation index boundaries changed.");
                // The document and canvas bars use the SAME physical row
                // sizing as the WPF side navigation at all common DPIs.
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                {
                    int height = MP_WorkspaceHeaderCheck(dpi);
                    if (height != MP_SectionTabs.MP_RequiredHeaderPixels(dpi))
                        throw new InvalidOperationException("Main toolbar DPI sizing drifted.");
                }
                static int MP_WorkspaceHeaderCheck(int dpi)
                    => MainForm.MP_WorkspaceHeaderPixels(dpi);
                // Check 96/120/144/192 DPI conversions for hybrid headers.
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                {
                    int height = MP_SectionTabs.MP_RequiredHeaderPixels(dpi);
                    if (height < MP_StudioTheme.HeaderHeight * dpi / 96d ||
                        height > MP_StudioTheme.HeaderHeight * dpi / 96d + 4)
                        throw new InvalidOperationException("WPF section header DPI sizing failed.");
                }
                // Verify a direct WPF thumbnail retains real color and alpha.
                using (var sample = new Bitmap(4, 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics sampleGraphics = Graphics.FromImage(sample))
                        sampleGraphics.Clear(Color.FromArgb(255, 40, 80, 120));
                    var thumbnail = MP_WpfThumbnailFactory.Create(sample,
                        new Rectangle(0, 0, 4, 4));
                    if (thumbnail == null || !thumbnail.IsFrozen ||
                        thumbnail.PixelWidth != 4 || thumbnail.PixelHeight != 4)
                        throw new InvalidOperationException("WPF fast thumbnail failed.");
                    byte[] pixel = new byte[4 * 4 * 4];
                    thumbnail.CopyPixels(pixel, 4 * 4, 0);
                    if (pixel[0] < 90 || pixel[0] > 150 ||
                        pixel[1] < 50 || pixel[1] > 110 ||
                        pixel[2] < 10 || pixel[2] > 70 ||
                        pixel[3] != 255)
                        throw new InvalidOperationException("WPF thumbnail channels/alpha changed.");
                }
                // Preserve enough space for property editors when the CP
                // footer is displayed on a short or high-DPI window.
                foreach (var (height, dpi) in new[]
                    { (480, 96), (700, 144), (900, 192) })
                {
                    int footer = MP_InspectorLayout.CpFooterHeight(height, dpi);
                    if (footer < 120 || footer > height * .34
                        || footer >= height / 2)
                        throw new InvalidOperationException(
                            "CP footer takes priority over the object inspector.");
                }
                // A single palette must drive both HWND and WPF surfaces.
                var panelColor = MP_StudioTheme.WpfColor(MP_StudioTheme.Panel);
                if (panelColor.R != MP_StudioTheme.Panel.R
                    || panelColor.G != MP_StudioTheme.Panel.G
                    || panelColor.B != MP_StudioTheme.Panel.B
                    || MP_StudioTheme.Panel.ToArgb() != Color.FromArgb(8, 10, 14).ToArgb()
                    || MP_StudioTheme.Text.ToArgb() != Color.FromArgb(200, 230, 255).ToArgb()
                    || MP_StudioTheme.Active != MP_StudioTheme.Text
                    || MP_StudioTheme.ActiveText != MP_StudioTheme.Panel
                    || MP_StudioTheme.HeaderHeight < 38
                    || MP_StudioTheme.PanelInset < 8)
                    throw new InvalidOperationException("Unified editor theme contract failed.");
                using var form = new MainForm();
                form.CreateControl();
                if (!form.MP_WorkspaceHeadersFit)
                    throw new InvalidOperationException(
                        "Document or canvas toolbar row clips high-DPI controls.");
                static IEnumerable<Control> Descendants(Control parent)
                {
                    foreach (Control child in parent.Controls)
                    {
                        yield return child;
                        foreach (Control descendant in Descendants(child)) yield return descendant;
                    }
                }
                var wpfHosts = Descendants(form)
                    .OfType<System.Windows.Forms.Integration.ElementHost>().ToArray();
                if (wpfHosts.Count(host => host.Child is MP_CpMotionView) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidAssetPalette) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidStructureLibrary) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidCpSummary) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidDocumentBar) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidTabBar) != 2)
                    throw new InvalidOperationException("WPF editor presentation hosts are missing.");
                // Selection and rubber-band effects must not mask other
                // rows, the scrollbar, or the search and preview panels.
                if (wpfHosts.Select(h => h.Child).OfType<MP_FluidStructureLibrary>()
                        .Any(h => !h.MP_HasSafeLayering)
                    || wpfHosts.Select(h => h.Child).OfType<MP_FluidAssetPalette>()
                        .Any(h => !h.MP_HasSafeLayering)
                    || wpfHosts.Select(h => h.Child).OfType<MP_CpMotionView>()
                        .Any(h => !h.MP_HasSafeLayering))
                    throw new InvalidOperationException("WPF cursor or scrollbar can obscure content.");
                var readout = new MP_PixelReadout { Text = "INDEX / 09" };
                readout.Measure(new System.Windows.Size(500, 100));
                if (readout.DesiredSize.Width <= 0 || readout.DesiredSize.Height <= 0)
                    throw new InvalidOperationException("Pixel readout layout failed.");
                // Template realization must have a public zero-argument
                // constructor so virtualized lists work after catalog load.
                _ = new MP_FluidAssetRow();
                // 278px split panel at 150% DPI leaves about 185 DIPs.
                // Every navigation tab must remain reachable at that width.
                double[] fitted = MP_TabLayout.Fit(new[] { 74d, 74d, 86d }, 185d);
                if (fitted.Length != 3 || fitted.Any(w => w < 55)
                    || fitted.Sum() > 185.01d)
                    throw new InvalidOperationException("WPF navigation tabs overflow narrow panels.");
                var sections = Descendants(form).OfType<MP_SectionTabs>().ToArray();
                if (sections.Length != 2 || sections.Any(section =>
                {
                    if (section.Controls.Count != 1 ||
                        section.Controls[0] is not TableLayoutPanel layout) return true;
                    return layout.RowCount != 2
                        || layout.GetControlFromPosition(0, 0)
                           is not System.Windows.Forms.Integration.ElementHost
                        || layout.GetControlFromPosition(0, 1) is not MP_StablePageHost
                        || !section.MP_HasStablePages || !section.MP_HeaderFitsDpi
                        || !section.MP_ContentStartsBelowHeader;
                }))
                    throw new InvalidOperationException("Tab headers can overlap editor page content.");
                var pages = Descendants(form).OfType<MP_PageSurface>().ToArray();
                if (pages.Length != 5 || !pages.Any(page => page.Title == "集群 CP")
                    || pages.Any(page => page.Parent is not MP_StablePageHost))
                    throw new InvalidOperationException("Workspace pages must stay on stable host panels.");
                // Switch twice to exercise the flicker-free page host while
                // preserving all page instances, without recreating HWNDs.
                foreach (var section in sections)
                {
                    int previous = section.SelectedIndex;
                    section.SelectedIndex = 1;
                    section.SelectedIndex = previous;
                    if (!section.MP_HasStablePages)
                        throw new InvalidOperationException("Page switch changed host/page ownership.");
                    // Regression: A/D must activate the real page, not merely
                    // move the animated cursor over an inactive heading.
                    var layoutForNavigation = (TableLayoutPanel)section.Controls[0];
                    var navBar = (MP_FluidTabBar)(
                        (System.Windows.Forms.Integration.ElementHost)
                        layoutForNavigation.GetControlFromPosition(0, 0)!).Child;
                    int initial = section.SelectedIndex;
                    navBar.MP_ActivateStep(1);
                    if (section.SelectedIndex != initial + 1 || !section.MP_HasStablePages)
                        throw new InvalidOperationException(
                            "A/D moved only the library tab outline; the page did not switch.");
                    navBar.MP_ActivateStep(-1);
                    if (section.SelectedIndex != initial || !section.MP_HasStablePages)
                        throw new InvalidOperationException(
                            "Reverse A/D navigation did not restore the active page.");
                    var activeSurface = Descendants(section)
                        .OfType<MP_PageSurface>()
                        .SingleOrDefault(page => page.MP_IsActive);
                    if (activeSurface == null
                        || !section.SelectPage(activeSurface)
                        || section.MP_SelectedPageTitle != activeSurface.Title
                        || !section.MP_HasStablePages)
                        throw new InvalidOperationException(
                            "Stable page selection no longer matches workspace commands.");
                }
                // Exercise all docking boundaries at common desktop sizes;
                // this catches accidental toolbar/page overlap after changes
                // to margins, min-widths or DPI row sizing.
                var savedSize = form.ClientSize;
                foreach (var size in new[]
                    { new Size(1040, 700), new Size(1280, 800), new Size(1680, 950) })
                {
                    form.ClientSize = size;
                    form.PerformLayout();
                    foreach (var section in sections) section.PerformLayout();
                    if (!form.MP_WorkspaceHeaderBoundsClear ||
                        sections.Any(section => !section.MP_ContentStartsBelowHeader))
                        throw new InvalidOperationException(
                            $"Editor controls overlap at {size.Width}x{size.Height}.");
                }
                form.ClientSize = savedSize;
                form.PerformLayout();
                var tabs = Descendants(form).OfType<MP_SectionTabs>().ToArray();
                if (tabs.Length != 2 || tabs.Any(tab => tab.SelectedIndex < 0)
                    || !pages.Any(page => page.Title == "工程")
                    || pages.Any(page => page.Title is "导出" or "结构" or "操作"))
                    throw new InvalidOperationException("Workspace section tabs failed to initialize.");
                var documentStrip = (ToolStrip)typeof(MainForm).GetField("documentBar",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(form)!;
                var documentHost = (Panel)typeof(MainForm).GetField("MP_documentHost",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(form)!;
                if (documentStrip.Parent != documentHost
                    || documentStrip.Items.Cast<ToolStripItem>().Any(item =>
                        item.Text is "新建" or "打开…"))
                    throw new InvalidOperationException("Document tabs must be hosted separately from New/Open.");
                var quickActions = Descendants(form).OfType<ToolStrip>()
                    .SelectMany(strip => strip.Items.Cast<ToolStripItem>()).ToArray();
                // Compact labels may be 新/开. Command identity is carried by
                // stable tooltips and must remain exactly one of each.
                if (quickActions.Count(item => item.ToolTipText?.StartsWith("新建结构") == true) != 1
                    || quickActions.Count(item => item.ToolTipText?.StartsWith("打开结构") == true) != 1)
                    throw new InvalidOperationException(
                        "New/Open commands missing or duplicated in compact toolbar.");
                if (!MP_UiSound.MP_ValidCueData())
                    throw new InvalidOperationException("Controller UI audio samples are invalid.");
                // Ensure the document strip also sends A/D to the real
                // activation callback (without opening/closing anything).
                int requestedDocument = -1;
                MP_FluidDocumentBar documentTest = new((index, close) =>
                    requestedDocument = close ? -2 : index);
                documentTest.SetTabs([
                    new MP_DocumentVisualState(0, "Alpha", true),
                    new MP_DocumentVisualState(1, "Beta", false)
                ]);
                documentTest.MP_ActivateStep(1);
                if (requestedDocument != 1)
                    throw new InvalidOperationException(
                        "Document A/D changed only the cursor, not the active document.");
    }
}
