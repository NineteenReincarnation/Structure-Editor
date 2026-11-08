using System.Diagnostics;

namespace StructureEditor;

// In-app help replaces the system's light MessageBoxes. All sizes are bounded
// by the current monitor; keyboard and sections scroll on smaller displays.
internal static class MP_HelpWindows
{
    private static readonly Color Surface = Color.FromArgb(30, 40, 48);
    private static readonly Color Background = Color.FromArgb(24, 33, 40);
    private static readonly Color Primary = Color.FromArgb(230, 239, 243);
    private static readonly Color Secondary = Color.FromArgb(156, 183, 199);
    private static readonly Color Accent = Color.FromArgb(87, 167, 189);
    internal const string ProjectUrl = "https://github.com/NineteenReincarnation/Structure-Editor";

    private sealed class MP_InfoWindow : Form
    {
        internal MP_InfoWindow(string caption, Size preferred, Form owner)
        {
            Text = caption;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            BackColor = Background; ForeColor = Primary;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Padding = Padding.Empty;
            var area = Screen.FromControl(owner).WorkingArea;
            Size = new(Math.Min(preferred.Width, area.Width - 48),
                Math.Min(preferred.Height, area.Height - 48));
            MinimumSize = new(Math.Min(530, Size.Width), Math.Min(360, Size.Height));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == System.Windows.Forms.Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MP_WindowGlass.Apply(Handle);
        }
    }

    private static Label Text(string value, float size = 9.5f, bool bold = false,
        Color? color = null) => new()
    {
        Text = value,
        AutoSize = true,
        ForeColor = color ?? Primary,
        BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 6)
    };

    private static Button Keycap(string key, bool accent = false, Action onClick = null)
    {
        int width = key switch
        {
            "Space" => 178, "Shift" => 90, "Enter" => 74,
            "Ctrl" or "Alt" or "Tab" or "Home" => 66,
            "Backspace" => 110, "Delete" => 78,
            _ => 41
        };
        Button button = new()
        {
            Text = key,
            Width = width,
            Height = 35,
            BackColor = accent ? Color.FromArgb(41, 95, 111) : Color.FromArgb(45, 60, 72),
            ForeColor = accent ? Color.FromArgb(225, 247, 249) : Color.FromArgb(203, 220, 230),
            Margin = new(2), Padding = Padding.Empty, FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, TabStop = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = accent
            ? Color.FromArgb(85, 163, 180) : Color.FromArgb(69, 90, 105);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(68, 113, 130);
        if (onClick != null) button.Click += (_, _) => onClick();
        return button;
    }

    private static FlowLayoutPanel Keys(params string[] keys)
    {
        FlowLayoutPanel row = new()
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Margin = new Padding(0), Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        foreach (string key in keys) row.Controls.Add(Keycap(key));
        return row;
    }

    private static Panel Section(string title)
    {
        Panel box = new() { BackColor = Surface, Height = 36,
            Margin = new Padding(0, 14, 0, 8), Padding = new Padding(10, 8, 10, 0) };
        var label = Text(title, 11f, true, Primary);
        label.Dock = DockStyle.Left;
        box.Controls.Add(label);
        return box;
    }

    private static void LinkToProject()
    {
        Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
    }

    internal static void Shortcuts(Form owner)
    {
        MP_InfoWindow form = new("快捷键 · Structure Editor", new Size(860, 740), owner);
        Label title = Text("键盘操作指南", 17, true);
        Label subtitle = Text("编辑功能全部通过快捷键操作。点击键帽，可查看对应功能。",
            9, false, Secondary);
        Panel header = new() { Dock = DockStyle.Top, Height = 94, Padding = new(20, 14, 12, 0),
            BackColor = Surface };
        title.Dock = DockStyle.Top; subtitle.Dock = DockStyle.Bottom;
        header.Controls.Add(subtitle); header.Controls.Add(title);
        Panel viewport = new() { Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = Background, Padding = new Padding(14, 12, 14, 16) };
        FlowLayoutPanel content = new()
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Background,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        viewport.Controls.Add(content);
        form.Controls.Add(viewport); form.Controls.Add(header);
        Label current = Text("选择一个键帽查看它的用途。", 10, false, Secondary);
        Panel footer = new() { Dock = DockStyle.Bottom, Height = 42,
            BackColor = Surface, Padding = new(20, 9, 10, 6) };
        current.Dock = DockStyle.Fill;
        footer.Controls.Add(current);
        form.Controls.Add(footer);
        form.Controls.SetChildIndex(viewport, 0);

        string Description(string key) => key.ToUpperInvariant() switch
        {
            "V" => "V · 选择工具", "B" => "B · 放置素材", "M" => "M · 矩形铺设",
            "E" => "E · 擦除 / 粘贴时顺时针旋转", "R" => "R · 旋转 90°",
            "H" => "H · 平移工具", "Z" => "Z · 缩放工具",
            "I" => "I · 吸取已有对象", "Q" => "Q · 粘贴时逆时针旋转",
            "G" => "G · 网格开关；Ctrl+G · 殉爆范围",
            "J" => "Ctrl+J · 护盾范围", "N" => "Ctrl+N · 新建工程",
            "O" => "Ctrl+O · 打开工程", "S" => "Ctrl+S · 保存",
            "A" => "Ctrl+A · 全选", "C" => "Ctrl+C · 复制",
            "X" => "Ctrl+X · 剪切；X · 删除对象",
            "D" => "Ctrl+D · 复制为新对象", "F5" => "F5 · 重新读取素材",
            "TAB" => "Tab / Shift+Tab · 切换编辑图层",
            "CTRL" => "Ctrl · 与其他键组合执行命令",
            "SHIFT" => "Shift · 多选与扩展框选；与 Tab 反向切换",
            "SPACE" => "空格 + 拖动 · 临时平移",
            "HOME" => "Home · 适合窗口；Ctrl+Home · 居中所选",
            "DELETE" or "BACKSPACE" => "Delete / Backspace · 删除对象",
            "ESC" => "Esc · 退出当前操作",
            _ => key + " · 可组合执行编辑命令"
        };
        void AddRow(string[] labels)
        {
            FlowLayoutPanel row = new()
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                BackColor = Color.Transparent, Margin = new Padding(0, 1, 0, 1),
                Padding = Padding.Empty
            };
            foreach (string label in labels)
                row.Controls.Add(Keycap(label,
                    new[] { "V", "B", "M", "E", "R", "H", "Z", "I", "G", "J", "Tab" }.Contains(label),
                    () => current.Text = Description(label)));
            content.Controls.Add(row);
        }
        content.Controls.Add(Section("键盘 · 快捷键位置"));
        AddRow(new[] { "Esc", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "F5" });
        AddRow(new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" });
        AddRow(new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", "Enter" });
        AddRow(new[] { "Shift", "Z", "X", "C", "V", "B", "N", "M", "Delete" });
        AddRow(new[] { "Ctrl", "Alt", "Space", "Home", "Backspace" });
        content.Controls.Add(Section("工具切换与画布"));
        void Binding(string description, params string[] shortcut)
        {
            Panel line = new() { Height = 36, Width = 755,
                BackColor = Color.Transparent, Margin = new Padding(0, 1, 0, 1) };
            FlowLayoutPanel caps = Keys(shortcut);
            caps.Width = Math.Min(310, shortcut.Sum(k => k.Length > 5 ? 92 : 53) + 18);
            caps.AutoSize = false; caps.Dock = DockStyle.Left;
            Label label = Text(description, 9.5f, false, Secondary);
            label.AutoSize = false; label.TextAlign = ContentAlignment.MiddleLeft;
            label.Dock = DockStyle.Fill; label.Margin = Padding.Empty;
            line.Controls.Add(label); line.Controls.Add(caps);
            content.Controls.Add(line);
        }
        Binding("选择 / 放置 / 矩形 / 擦除", "V", "B", "M", "E");
        Binding("平移 / 缩放 / 吸取 / 旋转", "H", "Z", "I", "R");
        Binding("图层切换（Shift 反向）", "Tab");
        Binding("居中所选 / 适应窗口", "Ctrl", "Home");
        content.Controls.Add(Section("工程与编辑"));
        Binding("保存工程", "Ctrl", "S");
        Binding("撤销 / 重做", "Ctrl", "Z");
        Binding("复制为新对象", "Ctrl", "D");
        Binding("复制 / 剪切 / 粘贴", "Ctrl", "C", "X", "V");
        Binding("全选 / 清除选择", "Ctrl", "A");
        content.Controls.Add(Section("范围显示"));
        Binding("殉爆范围", "Ctrl", "G");
        Binding("探测范围", "Ctrl", "H");
        Binding("护盾范围", "Ctrl", "J");
        content.Controls.Add(Text("鼠标：双击吸取 · Shift 拖动框选 · Ctrl 单击增减选择 · 滚轮缩放 · 中键平移",
            9, false, Secondary));
        // Recalculate widths on DPI changes and window resizing without
        // exposing content beyond the viewport's clipped scroll region.
        void Fit()
        {
            int width = Math.Max(450, viewport.ClientSize.Width - viewport.Padding.Horizontal - 20);
            foreach (Control child in content.Controls)
                if (child is Panel) child.Width = width;
        }
        viewport.Resize += (_, _) => Fit();
        Fit();
        form.Show(owner);
    }

    internal static void About(Form owner)
    {
        MP_InfoWindow form = new("关于 · Structure Editor", new Size(650, 500), owner);
        Panel header = new() { Dock = DockStyle.Top, Height = 145,
            Padding = new Padding(24, 22, 20, 12), BackColor = Surface };
        Label product = Text("Structure Editor", 23f, true);
        Label tagline = Text("RimWorld · 结构工坊", 11.5f, false, Accent);
        Label detail = Text("结构编辑 · 机械集群设计 · 多格式 XML 导出",
            9.5f, false, Secondary);
        detail.Dock = DockStyle.Bottom; tagline.Dock = DockStyle.Bottom; product.Dock = DockStyle.Top;
        header.Controls.Add(detail); header.Controls.Add(tagline); header.Controls.Add(product);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = Background, Padding = new Padding(24, 20, 24, 20) };
        FlowLayoutPanel stack = new()
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, FlowDirection = FlowDirection.TopDown,
            BackColor = Background
        };
        string version = Application.ProductVersion;
        stack.Controls.Add(Text("版本  " + version.Split('+')[0], 11, true));
        stack.Controls.Add(Text("作者  NineteenReincarnation", 10, false, Secondary));
        stack.Controls.Add(Text("为 RimWorld 制作建筑、地形与机械单位布局。JSON 保留编辑数据，XML 用于游戏与 Mod。",
            9.5f, false, Secondary));
        LinkLabel link = new()
        {
            AutoSize = true, Text = "github.com/NineteenReincarnation/Structure-Editor",
            LinkColor = Accent, ActiveLinkColor = Color.FromArgb(170, 224, 239),
            VisitedLinkColor = Accent,
            BackColor = Background,
            Margin = new Padding(0, 20, 0, 10)
        };
        link.LinkClicked += (_, _) => LinkToProject();
        stack.Controls.Add(link);
        stack.Controls.Add(Text("项目主页 · 源码、更新记录与问题反馈", 9, false, Secondary));
        body.Controls.Add(stack);
        form.Controls.Add(body); form.Controls.Add(header);
        form.Show(owner);
    }

    internal static void Documentation(Form owner)
    {
        string path = Path.GetFullPath(Path.Combine(Program.StructureLibraryFolder,
            "..", "docs", "使用说明.md"));
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else Shortcuts(owner);
    }

    internal static void Project() => LinkToProject();
}
