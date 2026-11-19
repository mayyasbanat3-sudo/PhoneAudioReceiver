using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace PhoneAudioReceiver;

enum Indicator { Idle, Busy, Connected, Receiving, Error }

static class Palette
{
    public static MediaColor Of(Indicator indicator) => indicator switch
    {
        Indicator.Busy => MediaColor.FromRgb(0xFF, 0xB0, 0x20),
        Indicator.Connected => MediaColor.FromRgb(0x5B, 0x8C, 0xFF),
        Indicator.Receiving => MediaColor.FromRgb(0x2D, 0xD8, 0x81),
        Indicator.Error => MediaColor.FromRgb(0xFF, 0x5A, 0x6A),
        _ => MediaColor.FromRgb(0x8E, 0x91, 0xA3),
    };
}

// Tray icon (WinForms NotifyIcon, the lightest option) with a themed context menu.
sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon notifyIcon;
    readonly Forms.ContextMenuStrip menu;
    readonly Forms.ToolStripMenuItem toggleItem;
    readonly MenuRenderer renderer = new();
    readonly Dictionary<Indicator, Icon> icons = new();
    Indicator? shown;

    public event Action? OpenRequested;
    public event Action? ToggleRequested;
    public event Action? ExitRequested;

    public Tray()
    {
        menu = new Forms.ContextMenuStrip
        {
            Renderer = renderer,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Forms.Padding(4),
            Font = new Font("Segoe UI Variable Text", 9.75f),
        };
        menu.HandleCreated += (_, _) => Backdrop.RoundSmall(menu.Handle);

        menu.Items.Add(MenuItem("Open", () => OpenRequested?.Invoke()));
        toggleItem = MenuItem("Connect", () => ToggleRequested?.Invoke());
        menu.Items.Add(toggleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(MenuItem("Exit", () => ExitRequested?.Invoke()));

        notifyIcon = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        notifyIcon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    static Forms.ToolStripMenuItem MenuItem(string text, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(8, 5, 24, 5) };
        item.Click += (_, _) => onClick();
        return item;
    }

    public void Update(Indicator indicator, string tooltip, string toggleText, bool toggleEnabled)
    {
        if (shown != indicator)
        {
            shown = indicator;
            notifyIcon.Icon = IconFor(indicator);
        }
        notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..62] + "…";
        toggleItem.Text = toggleText;
        toggleItem.Enabled = toggleEnabled;
    }

    public void ApplyTheme(bool dark)
    {
        renderer.Dark = dark;
        menu.Invalidate();
    }

    Icon IconFor(Indicator indicator)
    {
        if (icons.TryGetValue(indicator, out var cached)) return cached;

        const int size = 32;
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        using var stream = info.Stream;
        using var baseIcon = new Icon(stream, size, size);
        using var bmp = baseIcon.ToBitmap();
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(Color.Transparent)) g.FillEllipse(clear, 15.5f, 15.5f, 17f, 17f);
            g.CompositingMode = CompositingMode.SourceOver;
            var c = Palette.Of(indicator);
            using var dot = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
            g.FillEllipse(dot, 18.5f, 18.5f, 12f, 12f);
        }

        IntPtr handle = bmp.GetHicon();
        Icon icon;
        using (var temp = Icon.FromHandle(handle)) icon = (Icon)temp.Clone();
        DestroyIcon(handle);
        icons[indicator] = icon;
        return icon;
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        foreach (var icon in icons.Values) icon.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    sealed class MenuRenderer : Forms.ToolStripRenderer
    {
        public bool Dark { get; set; } = true;

        Color Back => Dark ? Color.FromArgb(0x2A, 0x2A, 0x32) : Color.FromArgb(0xFA, 0xFA, 0xFC);
        Color Line => Dark ? Color.FromArgb(0x3C, 0x3C, 0x48) : Color.FromArgb(0xE2, 0xE2, 0xE8);
        Color Hover => Dark ? Color.FromArgb(0x3A, 0x3A, 0x47) : Color.FromArgb(0xEC, 0xEC, 0xF2);
        Color Text => Dark ? Color.FromArgb(0xF3, 0xF3, 0xF8) : Color.FromArgb(0x18, 0x19, 0x20);
        Color Muted => Dark ? Color.FromArgb(0x7E, 0x80, 0x8E) : Color.FromArgb(0xA2, 0xA4, 0xB0);

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(Back);

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Line);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 5);
            using var brush = new SolidBrush(Hover);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Line);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
