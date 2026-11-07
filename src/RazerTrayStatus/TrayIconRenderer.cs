using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace RazerTrayStatus;

internal enum TrayState
{
    Normal,
    Charging,
    Low,
    Critical,
    Asleep,
    Disconnected,
    NoDevice,
}

/// <summary>
/// Renders the tray icon with GDI+, so the app ships without image assets.
///
/// The icon is a rounded badge: the background colour says how the battery is doing and the
/// number says the exact level. Colours are deliberately dark enough for white digits to stay
/// readable on any taskbar, and the digits come from a hand-drawn 3x5 font so they stay crisp
/// at 16 px instead of being a downscaled picture.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>3x5 pixel digits. '1' is one pixel wide so "100%" still fits at 16 px.</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = new[] { "111", "101", "101", "101", "111" },
        ['1'] = new[] { "1", "1", "1", "1", "1" },
        ['2'] = new[] { "111", "001", "111", "100", "111" },
        ['3'] = new[] { "111", "001", "111", "001", "111" },
        ['4'] = new[] { "101", "101", "111", "001", "001" },
        ['5'] = new[] { "111", "100", "111", "001", "111" },
        ['6'] = new[] { "111", "100", "111", "101", "111" },
        ['7'] = new[] { "111", "001", "001", "001", "001" },
        ['8'] = new[] { "111", "101", "111", "101", "111" },
        ['9'] = new[] { "111", "101", "111", "001", "111" },
        ['?'] = new[] { "111", "001", "011", "000", "010" },
    };

    public static Icon Create(TrayState state, int percent, out IntPtr handle)
    {
        int size = IconSize();

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            Draw(graphics, state, percent, size);
        }

        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    /// <summary>Renders at the size the shell actually asks for, so nothing gets rescaled and blurred.</summary>
    private static int IconSize() => Math.Clamp(SystemInformation.SmallIconSize.Width, 16, 64);

    /// <summary>Green = plenty, orange = getting low, red = charge now, blue = charging, grey = unknown.</summary>
    public static Color ColorFor(TrayState state) => state switch
    {
        TrayState.Normal => Color.FromArgb(255, 21, 128, 61),
        TrayState.Charging => Color.FromArgb(255, 29, 78, 216),
        TrayState.Low => Color.FromArgb(255, 180, 83, 9),
        TrayState.Critical => Color.FromArgb(255, 185, 28, 28),
        _ => Color.FromArgb(255, 82, 82, 91),
    };

    /// <summary>The same meaning in words, shown in the menu and the tooltip next to the percentage.</summary>
    public static string Describe(TrayState state) => state switch
    {
        TrayState.Normal => "good",
        TrayState.Charging => "charging",
        TrayState.Low => "getting low",
        TrayState.Critical => "charge now",
        TrayState.Asleep => "no battery answer",
        TrayState.Disconnected => "not connected",
        _ => "no mouse",
    };

    private static void Draw(Graphics graphics, TrayState state, int percent, int size)
    {
        Color accent = ColorFor(state);
        var bounds = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
        float radius = Math.Max(2f, size * 0.26f);

        using GraphicsPath path = RoundedRect(bounds, radius);

        // A soft top-to-bottom shade keeps the badge from looking like a flat sticker.
        using (var fill = new LinearGradientBrush(
                   new RectangleF(0f, 0f, size, size),
                   Blend(accent, 0.20f),
                   Blend(accent, -0.10f),
                   90f))
        {
            graphics.FillPath(fill, path);
        }

        using (var ring = new Pen(Color.FromArgb(80, 255, 255, 255), Math.Max(1f, size * 0.055f)))
            graphics.DrawPath(ring, path);

        DrawNumber(graphics, percent is >= 0 and <= 100 ? percent.ToString() : "?", size);
    }

    private static void DrawNumber(Graphics graphics, string text, int size)
    {
        var glyphs = new List<string[]>(text.Length);
        foreach (char character in text)
            glyphs.Add(Glyphs.TryGetValue(character, out string[]? glyph) ? glyph : Glyphs['?']);

        // Three digits get no gap, so "100" is still big enough to read at 16 px.
        int gap = glyphs.Count > 2 ? 0 : 1;

        int units = gap * (glyphs.Count - 1);
        foreach (string[] glyph in glyphs) units += glyph[0].Length;

        int scale = Math.Max(1, size / 8);
        while (scale > 1 && units * scale > size - 2) scale--;

        int height = 5 * scale;
        int cursor = (size - units * scale) / 2;
        int top = (size - height) / 2;

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;

        using var brush = new SolidBrush(Color.White);

        foreach (string[] glyph in glyphs)
        {
            int width = glyph[0].Length;

            for (int row = 0; row < glyph.Length; row++)
            {
                string line = glyph[row];

                for (int column = 0; column < width; column++)
                {
                    if (line[column] != '1') continue;

                    graphics.FillRectangle(
                        brush,
                        cursor + column * scale,
                        top + row * scale,
                        scale,
                        scale);
                }
            }

            cursor += (width + gap) * scale;
        }

        graphics.SmoothingMode = previous;
    }

    private static Color Blend(Color color, float amount) => amount >= 0
        ? Color.FromArgb(
            color.A,
            Lerp(color.R, 255, amount),
            Lerp(color.G, 255, amount),
            Lerp(color.B, 255, amount))
        : Color.FromArgb(
            color.A,
            Lerp(color.R, 0, -amount),
            Lerp(color.G, 0, -amount),
            Lerp(color.B, 0, -amount));

    private static int Lerp(int from, int to, float amount) =>
        (int)Math.Round(from + (to - from) * Math.Clamp(amount, 0f, 1f));

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));

        if (diameter <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
