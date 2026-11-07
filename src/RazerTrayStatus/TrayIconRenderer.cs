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

/// <summary>Renders the tray icon with GDI+, so the app ships without image assets.</summary>
internal static class TrayIconRenderer
{
    private const int IconSize = 32;

    public static Icon Create(TrayState state, int percent, out IntPtr handle)
    {
        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            Draw(graphics, state, percent);
        }

        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static void Draw(Graphics graphics, TrayState state, int percent)
    {
        const float size = IconSize;
        float padding = size * 0.10f;
        float capHeight = size * 0.09f;
        float capWidth = size * 0.36f;

        var cap = new RectangleF((size - capWidth) / 2f, padding * 0.5f, capWidth, capHeight);
        var body = new RectangleF(padding, padding * 0.5f + capHeight, size - padding * 2f, size - padding * 1.5f - capHeight);

        Color color = ColorFor(state);
        float radius = Math.Max(1.5f, size * 0.13f);

        using GraphicsPath bodyPath = RoundedRect(body, radius);

        using (var dim = new SolidBrush(Color.FromArgb(70, color)))
            graphics.FillPath(dim, bodyPath);

        if (percent >= 0)
        {
            float fillHeight = body.Height * Math.Clamp(percent, 0, 100) / 100f;
            var fillRect = new RectangleF(body.X, body.Y + body.Height - fillHeight, body.Width, fillHeight);

            graphics.SetClip(bodyPath);
            using var fill = new SolidBrush(color);
            graphics.FillRectangle(fill, fillRect);
            graphics.ResetClip();
        }

        using (var outline = new Pen(Color.FromArgb(190, color), Math.Max(1f, size * 0.06f)))
            graphics.DrawPath(outline, bodyPath);

        using (var capBrush = new SolidBrush(Color.FromArgb(190, color)))
            graphics.FillRectangle(capBrush, cap);

        if (state == TrayState.Charging)
            DrawBolt(graphics, body);
        else if (state is TrayState.Disconnected or TrayState.NoDevice)
            DrawSlash(graphics, body);
    }

    private static void DrawBolt(Graphics graphics, RectangleF body)
    {
        float centerX = body.X + body.Width / 2f;
        float centerY = body.Y + body.Height / 2f;
        float halfWidth = body.Width * 0.30f;
        float halfHeight = body.Height * 0.36f;

        PointF[] points =
        {
            new(centerX + halfWidth * 0.15f, centerY - halfHeight),
            new(centerX - halfWidth, centerY + halfHeight * 0.10f),
            new(centerX - halfWidth * 0.10f, centerY + halfHeight * 0.10f),
            new(centerX - halfWidth * 0.15f, centerY + halfHeight),
            new(centerX + halfWidth, centerY - halfHeight * 0.10f),
            new(centerX + halfWidth * 0.10f, centerY - halfHeight * 0.10f),
        };

        using var brush = new SolidBrush(Color.FromArgb(230, 20, 20, 20));
        graphics.FillPolygon(brush, points);
    }

    private static void DrawSlash(Graphics graphics, RectangleF body)
    {
        using var pen = new Pen(Color.FromArgb(235, 220, 60, 60), Math.Max(2f, body.Width * 0.16f));
        graphics.DrawLine(
            pen,
            body.X + body.Width * 0.12f,
            body.Y + body.Height * 0.88f,
            body.X + body.Width * 0.88f,
            body.Y + body.Height * 0.12f);
    }

    private static Color ColorFor(TrayState state) => state switch
    {
        TrayState.Charging => Color.FromArgb(255, 138, 214, 106),
        TrayState.Low => Color.FromArgb(255, 240, 180, 41),
        TrayState.Critical => Color.FromArgb(255, 229, 72, 77),
        TrayState.Disconnected => Color.FromArgb(255, 229, 72, 77),
        TrayState.Asleep => Color.FromArgb(255, 148, 153, 162),
        TrayState.NoDevice => Color.FromArgb(255, 148, 153, 162),
        _ => Color.FromArgb(255, 235, 235, 235),
    };

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
