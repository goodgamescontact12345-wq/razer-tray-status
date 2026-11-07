using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
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
/// The icon is a battery lying on its side - wide rounded body, small terminal on the right - with
/// the percentage set inside it in a real typeface. Lying it down is what makes the silhouette read
/// as a battery; a square body with a tab on top reads as a jar. Everything is drawn at 4x and
/// scaled down with a high-quality filter, so the edges are smooth at tray size.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>Everything is drawn at this multiple of the final size, then downsampled.</summary>
    private const int Supersample = 4;

    public static Icon Create(TrayState state, int percent, out IntPtr handle)
    {
        int size = IconSize();
        int canvas = size * Supersample;

        using var large = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(large))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            graphics.Clear(Color.Transparent);
            Draw(graphics, state, percent, size, canvas);
        }

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(large, new Rectangle(0, 0, size, size));
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

    private static void Draw(Graphics graphics, TrayState state, int percent, int size, int canvas)
    {
        Color accent = ColorFor(state);
        float s = canvas / (float)Supersample; // one final pixel, in canvas units

        // Wide body with a short terminal on the right: the classic battery silhouette.
        var body = new RectangleF(s * 0.03f, s * 0.19f, s * 0.76f, s * 0.62f);
        var terminal = new RectangleF(s * 0.82f, s * 0.34f, s * 0.15f, s * 0.32f);

        using GraphicsPath bodyPath = RoundedRect(body, s * 0.115f);
        using GraphicsPath terminalPath = RoundedRect(terminal, s * 0.035f);

        using (var fill = new LinearGradientBrush(
                   new RectangleF(0f, 0f, canvas, canvas),
                   Blend(accent, 0.16f),
                   Blend(accent, -0.12f),
                   90f))
        {
            graphics.FillPath(fill, bodyPath);
            graphics.FillPath(fill, terminalPath);
        }

        // Barely-there top highlight: the depth cue that stops it looking like a flat sticker.
        graphics.SetClip(bodyPath);
        using (var highlight = new SolidBrush(Color.FromArgb(30, 255, 255, 255)))
            graphics.FillRectangle(highlight, body.X, body.Y, body.Width, body.Height * 0.30f);
        graphics.ResetClip();

        DrawPercent(
            graphics,
            percent is >= 0 and <= 100 ? percent.ToString() : "?",
            canvas,
            body);
    }

    /// <summary>Draws the percentage centred in the battery body, cropping to the ink so it is optically centred.</summary>
    private static void DrawPercent(Graphics graphics, string text, int canvas, RectangleF body)
    {
        using var layer = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);

        using (Graphics textGraphics = Graphics.FromImage(layer))
        {
            textGraphics.Clear(Color.Transparent);
            textGraphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            textGraphics.SmoothingMode = SmoothingMode.AntiAlias;

            float maxWidth = body.Width * 0.86f;
            float em = body.Height * 1.00f;
            using Font font = FitFont(textGraphics, text, em, maxWidth);

            using var brush = new SolidBrush(Color.White);
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

            textGraphics.DrawString(text, font, brush, 0f, 0f, format);
        }

        Rectangle ink = InkBounds(layer);
        if (ink.Width <= 0 || ink.Height <= 0) return;

        using Bitmap cropped = layer.Clone(ink, PixelFormat.Format32bppArgb);

        float centreX = body.X + body.Width / 2f;
        float centreY = body.Y + body.Height / 2f;

        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(
            cropped,
            new RectangleF(
                centreX - ink.Width / 2f,
                centreY - ink.Height / 2f,
                ink.Width,
                ink.Height));
    }

    /// <summary>Largest bold UI font that still fits the width; Segoe UI is what Windows itself uses.</summary>
    private static Font FitFont(Graphics graphics, string text, float em, float maxWidth)
    {
        float[] candidates =
        {
            em,
            em * 0.92f,
            em * 0.84f,
            em * 0.76f,
            em * 0.68f,
            em * 0.60f,
        };

        Font? last = null;

        foreach (float candidate in candidates)
        {
            Font font = CreateFont(candidate);
            last?.Dispose();
            last = font;

            if (graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width <= maxWidth)
                return font;
        }

        return last ?? CreateFont(em * 0.60f);
    }

    private static Font CreateFont(float em)
    {
        foreach (string family in new[] { "Segoe UI Semibold", "Segoe UI", "Tahoma", "Arial" })
        {
            try
            {
                return new Font(family, em, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            catch (ArgumentException)
            {
                // Font family not installed: try the next one.
            }
        }

        return new Font(FontFamily.GenericSansSerif, em, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    /// <summary>Bounding box of the non-transparent pixels, so text can be centred by its ink.</summary>
    private static Rectangle InkBounds(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            var buffer = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int row = y * stride;

                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (buffer[row + x * 4 + 3] <= 8) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY) return Rectangle.Empty;

            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        finally
        {
            bitmap.UnlockBits(data);
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
