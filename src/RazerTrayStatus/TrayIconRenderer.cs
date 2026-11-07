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
/// The icon is a battery - terminal on top, body below - with the percentage drawn inside the
/// body. Standing it upright is what leaves the whole 16 px width free for the digits, which is
/// what keeps the number readable at tray size. The digits come from a hand-drawn 3x5 font and
/// each one gets a 1 px dark halo, so neighbouring strokes never merge into a single white bar.
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

        // Upright battery: small terminal on top, body below it, with a hair of space between.
        float nubHeight = Math.Max(2f, size * 0.14f);
        float nubWidth = size * 0.26f;

        var body = new RectangleF(
            0.5f,
            nubHeight + 0.6f,
            size - 1f,
            size - 0.5f - (nubHeight + 0.6f));

        var nub = new RectangleF((size - nubWidth) / 2f, 0.5f, nubWidth, nubHeight);

        using GraphicsPath bodyPath = RoundedRect(body, Math.Max(1.5f, size * 0.12f));
        using GraphicsPath nubPath = RoundedRect(nub, Math.Max(0.8f, size * 0.05f));

        // One shared gradient so the terminal and the body look like a single object.
        using (var fill = new LinearGradientBrush(
                   new RectangleF(0f, 0f, size, size),
                   Blend(accent, 0.18f),
                   Blend(accent, -0.10f),
                   90f))
        {
            graphics.FillPath(fill, bodyPath);
            graphics.FillPath(fill, nubPath);
        }

        // A hairline highlight separates the battery from a dark or a light taskbar.
        using (var ring = new Pen(Color.FromArgb(95, 255, 255, 255), Math.Max(1f, size * 0.055f)))
        {
            graphics.DrawPath(ring, bodyPath);
            graphics.DrawPath(ring, nubPath);
        }

        DrawNumber(graphics, percent is >= 0 and <= 100 ? percent.ToString() : "?", size, body);
    }

    private static void DrawNumber(Graphics graphics, string text, int size, RectangleF body)
    {
        var glyphs = new List<string[]>(text.Length);
        foreach (char character in text)
            glyphs.Add(Glyphs.TryGetValue(character, out string[]? glyph) ? glyph : Glyphs['?']);

        // Two digits butt together and the dark halo separates them, which buys back the width
        // the number needs at 16 px. Three digits keep a gap, or "100" reads as a row of bars.
        int gap = glyphs.Count > 2 ? 1 : 0;

        int units = 0;
        foreach (string[] glyph in glyphs) units += glyph[0].Length;

        int scale = Math.Max(1, size / 8);
        while (scale > 1 && units * scale > size - 2) scale--;

        int height = 5 * scale;
        int cursor = (size - units * scale) / 2;
        int top = (int)Math.Round(body.Y + (body.Height - height) / 2f);

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;

        // Dark halo first, so adjacent strokes never read as one thick white bar.
        using (var halo = new SolidBrush(Color.FromArgb(120, 12, 12, 14)))
            DrawGlyphs(graphics, glyphs, halo, cursor, top, scale, gap, inflate: 1);

        using (var brush = new SolidBrush(Color.White))
            DrawGlyphs(graphics, glyphs, brush, cursor, top, scale, gap, inflate: 0);

        graphics.SmoothingMode = previous;
    }

    private static void DrawGlyphs(
        Graphics graphics,
        List<string[]> glyphs,
        Brush brush,
        int cursor,
        int top,
        int scale,
        int gap,
        int inflate)
    {
        int x = cursor;

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
                        x + column * scale - inflate,
                        top + row * scale - inflate,
                        scale + inflate * 2,
                        scale + inflate * 2);
                }
            }

            x += (width + gap) * scale;
        }
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
