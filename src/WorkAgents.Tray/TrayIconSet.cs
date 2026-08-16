using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WorkAgents.Tray;

/// <summary>
/// WorkAgentsのロゴを基底に、状態ごとの形状バッジを重ねたトレイアイコン一式。
/// 色だけに依存せず、チェック・更新矢印・警告マークで状態を判別できるようにする。
/// </summary>
public sealed class TrayIconSet : IDisposable
{
    private const int IconSize = 32;
    private const string LogoFileName = "workagents-favicon.png";

    public required Icon Starting { get; init; }

    public required Icon Running { get; init; }

    public required Icon Updating { get; init; }

    public required Icon Error { get; init; }

    public static TrayIconSet LoadDefault()
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", LogoFileName);
        using var logo = File.Exists(logoPath) ? Image.FromFile(logoPath) : CreateFallbackLogo();

        return new TrayIconSet
        {
            Starting = CreateStatusIcon(logo, Status.Starting),
            Running = CreateStatusIcon(logo, Status.Running),
            Updating = CreateStatusIcon(logo, Status.Updating),
            Error = CreateStatusIcon(logo, Status.Error),
        };
    }

    private static Icon CreateStatusIcon(Image logo, Status status)
    {
        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        graphics.DrawImage(
            logo,
            new Rectangle(2, 2, 28, 28),
            0,
            0,
            logo.Width,
            logo.Height,
            GraphicsUnit.Pixel);

        DrawBadge(graphics, status);
        return CreateIcon(bitmap);
    }

    private static void DrawBadge(Graphics graphics, Status status)
    {
        const float badgeX = 18;
        const float badgeY = 18;
        const float badgeSize = 13;
        var badgeBounds = new RectangleF(badgeX, badgeY, badgeSize, badgeSize);

        using var shadowBrush = new SolidBrush(Color.FromArgb(150, Color.Black));
        graphics.FillEllipse(shadowBrush, badgeX - 1, badgeY - 1, badgeSize + 2, badgeSize + 2);

        switch (status)
        {
            case Status.Starting:
                DrawStartingBadge(graphics, badgeBounds);
                break;
            case Status.Running:
                DrawRunningBadge(graphics, badgeBounds);
                break;
            case Status.Updating:
                DrawUpdatingBadge(graphics, badgeBounds);
                break;
            case Status.Error:
                DrawErrorBadge(graphics, badgeBounds);
                break;
        }
    }

    private static void DrawStartingBadge(Graphics graphics, RectangleF bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 235, 171, 55));
        using var pen = new Pen(Color.White, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(brush, bounds);
        graphics.DrawArc(pen, bounds, -75, 245);
        graphics.FillEllipse(Brushes.White, bounds.X + 5.1f, bounds.Y + 5.1f, 2.8f, 2.8f);
    }

    private static void DrawRunningBadge(Graphics graphics, RectangleF bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 29, 177, 132));
        using var pen = new Pen(Color.White, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.FillEllipse(brush, bounds);
        graphics.DrawLines(
            pen,
            [
                new PointF(bounds.X + 3.1f, bounds.Y + 6.8f),
                new PointF(bounds.X + 5.5f, bounds.Y + 9.1f),
                new PointF(bounds.X + 10f, bounds.Y + 4.2f),
            ]);
    }

    private static void DrawUpdatingBadge(Graphics graphics, RectangleF bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 63, 133, 226));
        using var pen = new Pen(Color.White, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(brush, bounds);
        graphics.DrawArc(pen, bounds.X + 2.5f, bounds.Y + 2.5f, 8, 8, 205, 185);
        graphics.DrawArc(pen, bounds.X + 2.5f, bounds.Y + 2.5f, 8, 8, 25, 185);
        graphics.FillPolygon(
            Brushes.White,
            [
                new PointF(bounds.X + 3.1f, bounds.Y + 3.5f),
                new PointF(bounds.X + 5.7f, bounds.Y + 3.5f),
                new PointF(bounds.X + 4.1f, bounds.Y + 1.8f),
            ]);
        graphics.FillPolygon(
            Brushes.White,
            [
                new PointF(bounds.X + 9.9f, bounds.Y + 9.5f),
                new PointF(bounds.X + 7.3f, bounds.Y + 9.5f),
                new PointF(bounds.X + 8.9f, bounds.Y + 11.2f),
            ]);
    }

    private static void DrawErrorBadge(Graphics graphics, RectangleF bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 222, 76, 86));
        using var pen = new Pen(Color.White, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var triangle = new[]
        {
            new PointF(bounds.X + bounds.Width / 2, bounds.Y + 1.2f),
            new PointF(bounds.Right - 1.1f, bounds.Bottom - 1.1f),
            new PointF(bounds.X + 1.1f, bounds.Bottom - 1.1f),
        };
        graphics.FillPolygon(brush, triangle);
        graphics.DrawPolygon(pen, triangle);
        graphics.DrawLine(pen, bounds.X + 6.5f, bounds.Y + 4.5f, bounds.X + 6.5f, bounds.Y + 8.2f);
        graphics.FillEllipse(Brushes.White, bounds.X + 5.7f, bounds.Y + 9.6f, 1.6f, 1.6f);
    }

    private static Image CreateFallbackLogo()
    {
        var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var path = new GraphicsPath();
        path.AddArc(2, 2, 10, 10, 180, 90);
        path.AddArc(20, 2, 10, 10, 270, 90);
        path.AddArc(20, 20, 10, 10, 0, 90);
        path.AddArc(2, 20, 10, 10, 90, 90);
        path.CloseFigure();
        using var fill = new LinearGradientBrush(
            new Rectangle(0, 0, IconSize, IconSize),
            Color.FromArgb(126, 91, 224),
            Color.FromArgb(75, 52, 165),
            135f);
        graphics.FillPath(fill, path);

        using var logoPen = new Pen(Color.White, 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLines(
            logoPen,
            [
                new PointF(8, 11),
                new PointF(12, 21),
                new PointF(16, 13),
                new PointF(20, 21),
                new PointF(24, 11),
            ]);
        graphics.FillEllipse(Brushes.White, 6.5f, 9.5f, 3, 3);
        graphics.FillEllipse(Brushes.White, 22.5f, 9.5f, 3, 3);
        return bitmap;
    }

    private static Icon CreateIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        Starting.Dispose();
        Running.Dispose();
        Updating.Dispose();
        Error.Dispose();
    }

    private enum Status
    {
        Starting,
        Running,
        Updating,
        Error,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint handle);
}
