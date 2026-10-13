using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

public enum IconKind { Hamburger, Plus, Gear, ChevronDown, ChevronRight, Eye, EyeOff, Copy, Trash, Edit, Duplicate, Close, DragHandle, Search, Folder, Key, Shield, Dice, Person, Card, Back, Lock, Star, StarFilled, Globe, External, Download, Upload, History, Tag, Fingerprint, Server, Database, Apps, Certificate, Note }

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
                DrawGear(g, r, color, cutout);
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

            case IconKind.Lock:
            {
                var bodyRect = new RectangleF(r.Left + r.Width * 0.1f, r.Top + r.Height * 0.44f, r.Width * 0.8f, r.Height * 0.56f);
                using var lockBody = RoundedRectPath(Rectangle.Round(bodyRect), Math.Max(2, (int)(r.Width * 0.12f)));
                g.DrawPath(pen, lockBody);
                g.DrawArc(pen, r.Left + r.Width * 0.24f, r.Top, r.Width * 0.52f, r.Height * 0.62f, 180, 180);
                g.DrawLine(pen, r.Left + r.Width * 0.24f, r.Top + r.Height * 0.31f, r.Left + r.Width * 0.24f, bodyRect.Top);
                g.DrawLine(pen, r.Right - r.Width * 0.24f, r.Top + r.Height * 0.31f, r.Right - r.Width * 0.24f, bodyRect.Top);
                var kh = Math.Max(1.5f, r.Width * 0.09f);
                g.FillEllipse(brush, r.Left + r.Width / 2f - kh, bodyRect.Top + bodyRect.Height * 0.42f - kh, kh * 2, kh * 2);
                break;
            }

            case IconKind.Star:
            case IconKind.StarFilled:
            {
                var cx = r.Left + r.Width / 2f;
                var cy = r.Top + r.Height * 0.54f;
                var outer = r.Width / 2f;
                var inner = outer * 0.45f;
                var pts = new PointF[10];
                for (var i = 0; i < 10; i++)
                {
                    var rad = i % 2 == 0 ? outer : inner;
                    var a = -Math.PI / 2 + i * Math.PI / 5;
                    pts[i] = new PointF(cx + (float)(Math.Cos(a) * rad), cy + (float)(Math.Sin(a) * rad));
                }
                if (kind == IconKind.StarFilled) g.FillPolygon(brush, pts);
                else g.DrawPolygon(pen, pts);
                break;
            }

            case IconKind.Globe:
                g.DrawEllipse(pen, r);
                g.DrawEllipse(pen, r.Left + r.Width * 0.28f, r.Top, r.Width * 0.44f, r.Height);
                g.DrawLine(pen, r.Left, r.Top + r.Height / 2f, r.Right, r.Top + r.Height / 2f);
                break;

            case IconKind.External:
            {
                var boxR = new Rectangle(r.Left, r.Top + r.Height / 4, r.Width * 3 / 4, r.Height * 3 / 4);
                g.DrawLine(pen, boxR.Left + boxR.Width / 2, boxR.Top, boxR.Left, boxR.Top);
                g.DrawLine(pen, boxR.Left, boxR.Top, boxR.Left, boxR.Bottom);
                g.DrawLine(pen, boxR.Left, boxR.Bottom, boxR.Right, boxR.Bottom);
                g.DrawLine(pen, boxR.Right, boxR.Bottom, boxR.Right, boxR.Bottom - boxR.Height / 2);
                g.DrawLine(pen, r.Left + r.Width * 0.45f, r.Top + r.Height * 0.55f, r.Right, r.Top);
                g.DrawLine(pen, r.Right - r.Width * 0.38f, r.Top, r.Right, r.Top);
                g.DrawLine(pen, r.Right, r.Top, r.Right, r.Top + r.Height * 0.38f);
                break;
            }

            case IconKind.Download:
            case IconKind.Upload:
            {
                var cx = r.Left + r.Width / 2f;
                var arrowTop = (float)r.Top;
                var arrowBottom = r.Top + r.Height * 0.66f;
                g.DrawLine(pen, cx, arrowTop, cx, arrowBottom);
                if (kind == IconKind.Download)
                {
                    g.DrawLine(pen, cx - r.Width * 0.25f, arrowBottom - r.Height * 0.25f, cx, arrowBottom);
                    g.DrawLine(pen, cx + r.Width * 0.25f, arrowBottom - r.Height * 0.25f, cx, arrowBottom);
                }
                else
                {
                    g.DrawLine(pen, cx - r.Width * 0.25f, arrowTop + r.Height * 0.25f, cx, arrowTop);
                    g.DrawLine(pen, cx + r.Width * 0.25f, arrowTop + r.Height * 0.25f, cx, arrowTop);
                }
                g.DrawLine(pen, r.Left, r.Bottom - r.Height * 0.18f, r.Left, r.Bottom);
                g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
                g.DrawLine(pen, r.Right, r.Bottom, r.Right, r.Bottom - r.Height * 0.18f);
                break;
            }

            case IconKind.History:
            {
                g.DrawArc(pen, r, 200, 300);
                var cx = r.Left + r.Width / 2f;
                var cy = r.Top + r.Height / 2f;
                g.DrawLine(pen, cx, cy, cx, r.Top + r.Height * 0.22f);
                g.DrawLine(pen, cx, cy, cx + r.Width * 0.22f, cy + r.Height * 0.12f);
                g.DrawLine(pen, r.Left - 1, r.Top + r.Height * 0.2f, r.Left + r.Width * 0.05f, r.Top + r.Height * 0.42f);
                g.DrawLine(pen, r.Left + r.Width * 0.05f, r.Top + r.Height * 0.42f, r.Left + r.Width * 0.27f, r.Top + r.Height * 0.36f);
                break;
            }

            case IconKind.Tag:
            {
                using var tagPath = new GraphicsPath();
                tagPath.AddPolygon(new[]
                {
                    new PointF(r.Left, r.Top), new PointF(r.Left + r.Width * 0.5f, r.Top),
                    new PointF(r.Right, r.Top + r.Height * 0.5f), new PointF(r.Left + r.Width * 0.5f, r.Bottom),
                    new PointF(r.Left, r.Top + r.Height * 0.5f)
                });
                g.DrawPath(pen, tagPath);
                var h = Math.Max(1.5f, r.Width * 0.09f);
                g.FillEllipse(brush, r.Left + r.Width * 0.25f - h, r.Top + r.Height * 0.25f - h, h * 2, h * 2);
                break;
            }

            case IconKind.Fingerprint:
            {
                // Nested ridges open at the bottom, drawn with a thin pen so they stay distinct at small sizes.
                using var ridge = new Pen(color, Math.Max(1f, r.Width / 11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                var cx = r.Left + r.Width / 2f;
                var cy = r.Top + r.Height * 0.5f;
                for (var i = 0; i < 3; i++)
                {
                    var rx = r.Width * (0.16f + 0.17f * i);
                    var ry = r.Height * (0.2f + 0.15f * i);
                    g.DrawArc(ridge, cx - rx, cy - ry, rx * 2, ry * 2 + r.Height * 0.25f, 180, 180);
                    g.DrawLine(ridge, cx - rx, cy + r.Height * 0.125f, cx - rx, cy + r.Height * (0.2f + 0.08f * i));
                    g.DrawLine(ridge, cx + rx, cy + r.Height * 0.125f, cx + rx, cy + r.Height * (0.32f - 0.05f * i));
                }
                g.DrawLine(ridge, cx, cy - r.Height * 0.05f, cx, cy + r.Height * 0.4f);
                break;
            }

            case IconKind.Server:
            {
                // Two stacked rack units, each with a status light.
                var unitH = r.Height * 0.42f;
                for (var i = 0; i < 2; i++)
                {
                    var unit = new RectangleF(r.Left, r.Top + i * (unitH + r.Height * 0.16f), r.Width, unitH);
                    using var unitPath = RoundedRectPath(Rectangle.Round(unit), Math.Max(1, (int)(unitH * 0.25f)));
                    g.DrawPath(pen, unitPath);
                    var dot = Math.Max(1.5f, r.Width * 0.09f);
                    g.FillEllipse(brush, unit.Right - r.Width * 0.22f - dot, unit.Top + unitH / 2 - dot, dot * 2, dot * 2);
                    g.DrawLine(pen, unit.Left + r.Width * 0.18f, unit.Top + unitH / 2, unit.Left + r.Width * 0.45f, unit.Top + unitH / 2);
                }
                break;
            }

            case IconKind.Database:
            {
                // Cylinder: top ellipse, sides, one middle band, bottom arc.
                var ellH = r.Height * 0.28f;
                g.DrawEllipse(pen, r.Left, r.Top, r.Width, ellH);
                g.DrawLine(pen, r.Left, r.Top + ellH / 2, r.Left, r.Bottom - ellH / 2);
                g.DrawLine(pen, r.Right, r.Top + ellH / 2, r.Right, r.Bottom - ellH / 2);
                g.DrawArc(pen, r.Left, r.Top + (r.Height - ellH) / 2, r.Width, ellH, 0, 180);
                g.DrawArc(pen, r.Left, r.Bottom - ellH, r.Width, ellH, 0, 180);
                break;
            }

            case IconKind.Apps:
            {
                // 2x2 grid of rounded tiles.
                var gap = r.Width * 0.16f;
                var tile = (r.Width - gap) / 2f;
                for (var row = 0; row < 2; row++)
                for (var col = 0; col < 2; col++)
                {
                    var cell = new RectangleF(r.Left + col * (tile + gap), r.Top + row * (tile + gap), tile, tile);
                    using var cellPath = RoundedRectPath(Rectangle.Round(cell), Math.Max(1, (int)(tile * 0.3f)));
                    g.DrawPath(pen, cellPath);
                }
                break;
            }

            case IconKind.Certificate:
            {
                // Rosette with two ribbon tails.
                var d = r.Width * 0.62f;
                var cx = r.Left + r.Width / 2f;
                var rosette = new RectangleF(cx - d / 2, r.Top, d, d);
                g.DrawEllipse(pen, rosette);
                var inner = d * 0.42f;
                g.FillEllipse(brush, cx - inner / 2, r.Top + (d - inner) / 2, inner, inner);
                var tailTop = r.Top + d * 0.82f;
                g.DrawLine(pen, cx - d * 0.22f, tailTop, cx - d * 0.36f, r.Bottom);
                g.DrawLine(pen, cx - d * 0.36f, r.Bottom, cx - d * 0.12f, r.Bottom - r.Height * 0.12f);
                g.DrawLine(pen, cx + d * 0.22f, tailTop, cx + d * 0.36f, r.Bottom);
                g.DrawLine(pen, cx + d * 0.36f, r.Bottom, cx + d * 0.12f, r.Bottom - r.Height * 0.12f);
                break;
            }

            case IconKind.Note:
            {
                // Page with a folded corner and text lines.
                var fold = r.Width * 0.28f;
                var left = r.Left + r.Width * 0.12f;
                var right = r.Right - r.Width * 0.12f;
                using var page = new GraphicsPath();
                page.AddLines(new[]
                {
                    new PointF(left, r.Top), new PointF(right - fold, r.Top), new PointF(right, r.Top + fold),
                    new PointF(right, r.Bottom), new PointF(left, r.Bottom)
                });
                page.CloseFigure();
                g.DrawPath(pen, page);
                for (var i = 0; i < 3; i++)
                {
                    var y = r.Top + r.Height * (0.45f + i * 0.17f);
                    g.DrawLine(pen, left + r.Width * 0.14f, y, right - r.Width * (i == 2 ? 0.3f : 0.14f), y);
                }
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

    // Outlined cog: 8 square teeth around a ring, with a round hole (stroke style, like the other nav icons).
    private static void DrawGear(Graphics g, Rectangle r, Color color, Color cutout)
    {
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        var outer = r.Width / 2f;
        var body = outer * 0.74f;
        const int teeth = 8;
        var toothHalf = (float)(Math.PI / teeth) * 0.42f;

        using var path = new GraphicsPath();
        var points = new List<PointF>();
        for (var i = 0; i < teeth; i++)
        {
            var a = Math.PI * 2 * i / teeth - Math.PI / 2;
            PointF At(double angle, float radius) => new(cx + (float)(Math.Cos(angle) * radius), cy + (float)(Math.Sin(angle) * radius));
            points.Add(At(a - toothHalf * 1.25, body));
            points.Add(At(a - toothHalf, outer));
            points.Add(At(a + toothHalf, outer));
            points.Add(At(a + toothHalf * 1.25, body));
        }
        path.AddPolygon(points.ToArray());

        using var pen = new Pen(color, Math.Max(1.4f, r.Width / 11f)) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
        var hole = outer * 0.3f;
        g.DrawEllipse(pen, cx - hole, cy - hole, hole * 2, hole * 2);
    }
}
