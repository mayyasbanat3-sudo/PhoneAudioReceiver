using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PhoneAudioReceiver;

enum Indicator { Idle, Busy, Connected, Receiving, Error }

static class AppIcons
{
    static readonly Dictionary<Indicator, Icon> Cache = new();

    public static Color ColorOf(Indicator indicator) => indicator switch
    {
        Indicator.Busy => Color.FromArgb(0xD9, 0x96, 0x00),
        Indicator.Connected => Color.FromArgb(0x0A, 0x7C, 0xE6),
        Indicator.Receiving => Color.FromArgb(0x22, 0xA0, 0x48),
        Indicator.Error => Color.FromArgb(0xD8, 0x3B, 0x3B),
        _ => Color.FromArgb(0x80, 0x80, 0x80),
    };

    public static Icon Get(Indicator indicator)
    {
        if (!Cache.TryGetValue(indicator, out var icon))
        {
            using var bmp = Draw(ColorOf(indicator), 32);
            IntPtr handle = bmp.GetHicon();
            using (var temp = Icon.FromHandle(handle)) icon = (Icon)temp.Clone();
            DestroyIcon(handle);
            Cache[indicator] = icon;
        }
        return icon;
    }

    // Filled circle with a white headphones glyph.
    public static Bitmap Draw(Color color, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(color)) g.FillEllipse(fill, 0.5f, 0.5f, size - 1f, size - 1f);

        float m = size * 0.26f;
        float band = size - 2 * m;
        using (var pen = new Pen(Color.White, size * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(pen, m, m, band, band, 180, 180);

        float cupW = size * 0.15f, cupH = size * 0.25f, cupY = size * 0.48f;
        g.FillRectangle(Brushes.White, m - cupW * 0.5f, cupY, cupW, cupH);
        g.FillRectangle(Brushes.White, size - m - cupW * 0.5f, cupY, cupW, cupH);
        return bmp;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
