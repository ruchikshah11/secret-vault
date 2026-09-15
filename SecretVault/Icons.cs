using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

public enum IconKind { Hamburger, Plus, Gear, ChevronDown, ChevronRight, Eye, EyeOff, Copy, Trash, Edit, Duplicate, Close, DragHandle, Search, Folder, Key, Shield, Dice, Person, Card, Back }

public static class Icons
{
    public static void Draw(Graphics g, IconKind kind, Rectangle bounds, Color color, float strokeWidth = 1.6f, Color? background = null)
    {
        var cutout = background ?? BackgroundHintFor(color);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        var r = bounds;

        switch (kind)
        {
            case IconKind.Hamburger:
                for (var i = 0; i < 3; i++)
                {
                    var y = r.Top + r.Height * (i + 1) / 4f;
                    g.DrawLine(pen, r.Left, y, r.Right, y);
                }
                break;

            case IconKind.Plus:
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top, r.Left + r.Width / 2f, r.Bottom);
                g.DrawLine(pen, r.Left, r.Top + r.Height / 2f, r.Right, r.Top + r.Height / 2f);
                break;

            case IconKind.Gear:
                DrawGear(g, r, color);
                break;

            case IconKind.ChevronDown:
                DrawChevron(g, r, pen, 90);
                break;

            case IconKind.ChevronRight:
                DrawChevron(g, r, pen, 0);
                break;

            case IconKind.Back:
            {
                var cx = r.Left + r.Width / 2f;
                var cy = r.Top + r.Height / 2f;
                var p1 = new PointF(cx + r.Width * 0.15f, r.Top);
                var p2 = new PointF(cx - r.Width * 0.25f, cy);
                var p3 = new PointF(cx + r.Width * 0.15f, r.Bottom);
                g.DrawLine(pen, p1, p2);
                g.DrawLine(pen, p2, p3);
                break;
            }

            case IconKind.Eye:
                DrawEye(g, r, pen, brush, false);
                break;

            case IconKind.EyeOff:
                DrawEye(g, r, pen, brush, true);
                break;

            case IconKind.Copy:
                var back = new Rectangle(r.Left, r.Top + r.Height / 5, (int)(r.Width * 0.65f), (int)(r.Height * 0.65f));
                var front = new Rectangle(r.Left + r.Width / 3, r.Top, (int)(r.Width * 0.65f), (int)(r.Height * 0.65f));
                g.DrawRectangle(pen, back);
                g.FillRectangle(new SolidBrush(cutout), front);
                g.DrawRectangle(pen, front);
                break;

            case IconKind.Trash:
            {
                var lidY = r.Top + r.Height * 0.24f;
                var handleLeft = r.Left + r.Width * 0.32f;
                var handleRight = r.Right - r.Width * 0.32f;
                var handleTop = r.Top;

                g.DrawLine(pen, r.Left, lidY, r.Right, lidY);
                g.DrawLine(pen, handleLeft, lidY, handleLeft, handleTop);
                g.DrawLine(pen, handleRight, lidY, handleRight, handleTop);
                g.DrawLine(pen, handleLeft, handleTop, handleRight, handleTop);

                var bodyLeft = r.Left + r.Width * 0.12f;
                var bodyRight = r.Right - r.Width * 0.12f;
                using var binBody = new GraphicsPath();
                binBody.AddPolygon(new[]
                {
                    new PointF(bodyLeft, lidY),
                    new PointF(bodyRight, lidY),
                    new PointF(bodyRight - 1, r.Bottom),
                    new PointF(bodyLeft + 1, r.Bottom)
                });
                g.DrawPath(pen, binBody);

                var innerTop = lidY + (r.Bottom - lidY) * 0.15f;
                var innerBottom = r.Bottom - (r.Bottom - lidY) * 0.12f;
                for (var i = 1; i <= 3; i++)
                {
                    var x = bodyLeft + (bodyRight - bodyLeft) * i / 4f;
                    g.DrawLine(pen, x, innerTop, x, innerBottom);
                }
                break;
            }

            case IconKind.Edit:
            {
                using var bodyPen = new Pen(color, Math.Max(3f, r.Width * 0.22f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Triangle
                };
                g.DrawLine(bodyPen, r.Left + 1, r.Bottom - 1, r.Right - 3, r.Top + 3);
                break;
            }

            case IconKind.Duplicate:
                var d1 = new Rectangle(r.Left, r.Top + 3, r.Width - 4, r.Height - 4);
                var d2 = new Rectangle(r.Left + 4, r.Top, r.Width - 4, r.Height - 4);
                g.DrawRectangle(pen, d1);
                g.FillRectangle(new SolidBrush(cutout), d2);
                g.DrawRectangle(pen, d2);
                break;

            case IconKind.Close:
                g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
                break;

            case IconKind.DragHandle:
                for (var i = 0; i < 3; i++)
                {
                    var y = r.Top + r.Height * (i + 1) / 4f;
                    g.DrawLine(pen, r.Left, y, r.Right, y);
                }
                break;

            case IconKind.Search:
                var circle = new Rectangle(r.Left, r.Top, (int)(r.Width * 0.7f), (int)(r.Height * 0.7f));
                g.DrawEllipse(pen, circle);
                g.DrawLine(pen, circle.Right - 2, circle.Bottom - 2, r.Right, r.Bottom);
                break;

            case IconKind.Folder:
                var body = new Rectangle(r.Left, r.Top + r.Height / 4, r.Width, r.Height * 3 / 4);
                var tab = new Rectangle(r.Left, r.Top + r.Height / 6, r.Width * 2 / 5, r.Height / 6);
                g.FillRectangle(brush, tab);
                g.FillRectangle(brush, body);
                break;

            case IconKind.Key:
                var head = new Rectangle(r.Left, r.Top, (int)(r.Width * 0.55f), (int)(r.Width * 0.55f));
                g.DrawEllipse(pen, head);
                g.DrawLine(pen, head.Right - 3, head.Top + head.Height / 2, r.Right, head.Top + head.Height / 2);
                g.DrawLine(pen, r.Right - 4, head.Top + head.Height / 2, r.Right - 4, head.Top + head.Height / 2 + 5);
                g.DrawLine(pen, r.Right, head.Top + head.Height / 2, r.Right, head.Top + head.Height / 2 + 8);
                break;

            case IconKind.Dice:
                using (var face = new GraphicsPath())
                {
                    face.AddRectangle(new Rectangle(r.Left, r.Top, r.Width - 1, r.Height - 1));
                    g.DrawPath(pen, face);
                }
                var pipR = Math.Max(1.5f, r.Width * 0.08f);
                void Pip(float fx, float fy) => g.FillEllipse(brush, r.Left + r.Width * fx - pipR, r.Top + r.Height * fy - pipR, pipR * 2, pipR * 2);
                Pip(0.25f, 0.25f);
                Pip(0.75f, 0.25f);
                Pip(0.5f, 0.5f);
                Pip(0.25f, 0.75f);
                Pip(0.75f, 0.75f);
                break;

            case IconKind.Card:
            {
                var cardRect = new Rectangle(r.Left, r.Top + (int)(r.Height * 0.12f), r.Width, (int)(r.Height * 0.76f));
                using var cardPath = RoundedRectPath(cardRect, Math.Max(2, cardRect.Height / 6));
                g.DrawPath(pen, cardPath);

                var stripeY = cardRect.Top + cardRect.Height * 0.3f;
                g.DrawLine(pen, cardRect.Left, stripeY, cardRect.Right, stripeY);

                var dotR = Math.Max(1.5f, cardRect.Height * 0.09f);
                var dotY = cardRect.Bottom - cardRect.Height * 0.25f;
                g.FillEllipse(brush, cardRect.Left + cardRect.Width * 0.15f - dotR, dotY - dotR, dotR * 2, dotR * 2);
                g.FillEllipse(brush, cardRect.Left + cardRect.Width * 0.28f - dotR, dotY - dotR, dotR * 2, dotR * 2);
                break;
            }

            case IconKind.Person:
            {
                var headD = r.Width * 0.42f;
                g.FillEllipse(brush, r.Left + (r.Width - headD) / 2, r.Top, headD, headD);
                using var personBody = new GraphicsPath();
                personBody.AddArc(r.Left, r.Bottom - r.Height * 0.55f, r.Width, r.Height * 0.6f, 180, 180);
                g.FillPath(brush, personBody);
                break;
            }

            case IconKind.Shield:
            {
                var top = r.Top + r.Height * 0.06f;
                var midY = r.Top + r.Height * 0.55f;
                var midX = r.Left + r.Width / 2f;

                using var path = new GraphicsPath();
                path.AddLine(r.Left, top, r.Right, top);
                path.AddLine(r.Right, top, r.Right, midY);
                path.AddBezier(r.Right, midY, r.Right, r.Bottom, midX, r.Bottom, midX, r.Bottom);
                path.AddBezier(midX, r.Bottom, r.Left, r.Bottom, r.Left, midY, r.Left, midY);
                path.CloseFigure();
                g.DrawPath(pen, path);
                break;
            }
        }
    }

    private static Color BackgroundHintFor(Color foreground) =>
        foreground.GetBrightness() > 0.5f ? Color.FromArgb(40, 40, 44) : Color.White;

    private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawChevron(Graphics g, Rectangle r, Pen pen, int rotationDegrees)
    {
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        PointF p1, p2, p3;
        if (rotationDegrees == 90)
        {
            p1 = new PointF(r.Left, cy - r.Height * 0.15f);
            p2 = new PointF(cx, cy + r.Height * 0.25f);
            p3 = new PointF(r.Right, cy - r.Height * 0.15f);
        }
        else
        {
            p1 = new PointF(cx - r.Width * 0.15f, r.Top);
            p2 = new PointF(cx + r.Width * 0.25f, cy);
            p3 = new PointF(cx - r.Width * 0.15f, r.Bottom);
        }
        g.DrawLine(pen, p1, p2);
        g.DrawLine(pen, p2, p3);
    }

    private static void DrawEye(Graphics g, Rectangle r, Pen pen, SolidBrush brush, bool off)
    {
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        using var path = new GraphicsPath();
        path.AddBezier(r.Left, cy, r.Left + r.Width * 0.25f, r.Top, r.Right - r.Width * 0.25f, r.Top, r.Right, cy);
        path.AddBezier(r.Right, cy, r.Right - r.Width * 0.25f, r.Bottom, r.Left + r.Width * 0.25f, r.Bottom, r.Left, cy);
        g.DrawPath(pen, path);

        var pupilR = r.Height * 0.16f;
        g.FillEllipse(brush, cx - pupilR, cy - pupilR, pupilR * 2, pupilR * 2);

        if (off)
            g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Top);
    }

    private static void DrawGear(Graphics g, Rectangle r, Color color)
    {
        using var brush = new SolidBrush(color);
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        var outerR = r.Width / 2f;
        var innerR = outerR * 0.62f;
        var toothR = outerR * 0.15f;

        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x = cx + (float)(Math.Cos(angle) * outerR * 0.82f);
            var y = cy + (float)(Math.Sin(angle) * outerR * 0.82f);
            g.FillEllipse(brush, x - toothR, y - toothR, toothR * 2, toothR * 2);
        }

        g.FillEllipse(brush, cx - innerR, cy - innerR, innerR * 2, innerR * 2);
        using var holeBrush = new SolidBrush(BackgroundHintFor(color));
        var holeR = innerR * 0.4f;
        g.FillEllipse(holeBrush, cx - holeR, cy - holeR, holeR * 2, holeR * 2);
    }
}
