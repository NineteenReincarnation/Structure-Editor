using System.Diagnostics;
using System.Reflection;

namespace StructureEditor;

internal static class StudioStartupChecks
{
    public static void Run(StudioPaths paths, string folder, Action<bool, string> need)
    {
        using MainForm form = new();
        _ = form.Handle;
        int heartbeats = 0; long previous = 0, largestGap = 0;
        bool responsiveDuringLoad = false;
        var elapsed = Stopwatch.StartNew();
        using System.Windows.Forms.Timer pulse = new() { Interval = 15 };
        pulse.Tick += (_, _) =>
        {
            long now = elapsed.ElapsedMilliseconds;
            if (previous > 0) largestGap = Math.Max(largestGap, now - previous);
            previous = now; heartbeats++; responsiveDuringLoad |= form.IsLoadingCatalog;
        };
        pulse.Start(); Task loading = form.LoadCatalogAsync();
        while (!loading.IsCompleted && elapsed.ElapsedMilliseconds < 60000) { Application.DoEvents(); Thread.Sleep(2); }
        pulse.Stop();
        need(loading.IsCompletedSuccessfully && !form.IsLoadingCatalog, "Background catalog load must complete without blocking the message loop");
        need(responsiveDuringLoad && heartbeats > 0, "The UI must process timer messages while game definitions load");
        var source = (Catalog)typeof(MainForm).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
        need(source != null && form.CatalogLoadMilliseconds > 0, "Startup must publish a usable catalog");
        need(source.Find("Building", "Slate")?.Group == "自然岩石", "Natural rock walls must not appear as miscellaneous furniture");
        need(source.Find("Building", "Campfire")?.Group == "温度" && source.Find("Building", "StandingLamp")?.Group == "照明", "Temperature and lighting follow distinct useful categories");
        need(CatalogGrouping.Order("结构") < CatalogGrouping.Order("生产") && CatalogGrouping.Order("生产") < CatalogGrouping.Order("家具"), "Common building categories follow the Architect/Wiki order");

        foreach (var (factor, width, height, name) in new[] { (1f, 1050, 720, "compact"), (1.25f, 1450, 980, "125"), (1.5f, 1650, 1100, "150") })
        {
            using MainForm layout = new(); layout.BindForChecks(source, new StructureDocument());
            if (factor != 1) layout.Scale(new SizeF(factor, factor));
            layout.Size = new(width, height);
            StudioInteractionChecks.RenderWorkspace(layout, Path.Combine(folder, "workspace-layout-" + name + ".png"));
            foreach (string field in new[] { "scopeBox", "kindBox", "groupBox", "packBox", "search" })
            {
                var control = (Control)typeof(MainForm).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layout);
                need(control.Width > 80 && control.Parent.ClientRectangle.Contains(control.Bounds), name + ": " + field + " must remain completely inside its responsive filter row");
            }
        }
        using TextureLibrary preview = new(); preview.AddArchive(Path.Combine(paths.GameRoot, "Assets.zip"));
        using ManualResetEventSlim ready = new(); preview.TextureReady += () => ready.Set();
        string texture = source.Find("Building", "Wall").Texture;
        var requestTimer = Stopwatch.StartNew(); var pending = preview.Request(texture); long requestMs = requestTimer.ElapsedMilliseconds;
        need(pending == null, "An uncached texture request must return a placeholder immediately");
        need(ready.Wait(15000) && preview.Get(texture) != null, "The background preview worker must decode and publish its result: " + texture + " " + string.Join("; ", preview.Errors));
        File.WriteAllText(Path.Combine(folder, "startup-checks.txt"), $"Catalog {form.CatalogLoadMilliseconds} ms; UI heartbeats {heartbeats}; largest message gap {largestGap} ms; uncached preview request {requestMs} ms.\nResponsive layouts verified at 100%, 125%, 150%.\n");
    }
}
