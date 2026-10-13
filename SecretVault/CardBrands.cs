using System.Drawing.Drawing2D;
using SecretVault.Models;

namespace SecretVault.Gui;

// Card colours and simple drawn network marks. Detection lives in SecretVault.Core (CardNetworks) so the mobile app shares it.
public static class CardBrands
{
    public static CardBrand[] Selectable => CardNetworks.Selectable;
    public static CardBrand Resolve(PaymentCard card) => CardNetworks.Resolve(card);
    public static CardBrand Detect(string number) => CardNetworks.Detect(number);
    public static string DisplayName(CardBrand brand) => CardNetworks.DisplayName(brand);
    public static string ShortName(CardBrand brand) => CardNetworks.ShortName(brand);

    public static (Color Start, Color End) Colors(CardBrand brand) => brand switch
    {
        CardBrand.Visa => (Color.FromArgb(20, 40, 120), Color.FromArgb(40, 100, 220)),
        CardBrand.Mastercard => (Color.FromArgb(28, 28, 36), Color.FromArgb(70, 70, 86)),
        CardBrand.Amex => (Color.FromArgb(0, 92, 150), Color.FromArgb(40, 160, 210)),
        CardBrand.RuPay => (Color.FromArgb(10, 60, 90), Color.FromArgb(20, 130, 120)),
        CardBrand.Discover => (Color.FromArgb(160, 70, 10), Color.FromArgb(245, 140, 40)),
        CardBrand.Jcb => (Color.FromArgb(10, 80, 60), Color.FromArgb(30, 150, 100)),
        CardBrand.Diners => (Color.FromArgb(40, 60, 90), Color.FromArgb(100, 120, 150)),
        CardBrand.Maestro => (Color.FromArgb(10, 50, 110), Color.FromArgb(30, 130, 200)),
        CardBrand.UnionPay => (Color.FromArgb(120, 20, 30), Color.FromArgb(200, 50, 60)),
        _ => (Color.FromArgb(70, 48, 140), Color.FromArgb(140, 90, 220))
    };

    // Draws the network mark right-aligned inside `area` (white-on-card style). Returns nothing; scales with area height.
    public static void DrawLogo(Graphics g, RectangleF area, CardBrand brand)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var h = area.Height;

        switch (brand)
        {
            case CardBrand.Mastercard:
            case CardBrand.Maestro:
            {
                var r = h * 0.5f;
                var right = area.Right;
                var cy = area.Top + h / 2f;
                var left = new RectangleF(right - r * 3.3f, cy - r, r * 2, r * 2);
                var rightC = new RectangleF(right - r * 2f, cy - r, r * 2, r * 2);
                var (c1, c2) = brand == CardBrand.Mastercard
                    ? (Color.FromArgb(235, 0, 27), Color.FromArgb(247, 158, 27))
                    : (Color.FromArgb(0, 153, 223), Color.FromArgb(237, 0, 6));
                using (var b1 = new SolidBrush(c1)) g.FillEllipse(b1, left);
                using (var b2 = new SolidBrush(Color.FromArgb(235, c2))) g.FillEllipse(b2, rightC);
                using (var overlapPath = new GraphicsPath())
                {
                    overlapPath.AddEllipse(left);
                    var clip = g.Clip;
                    g.SetClip(overlapPath, CombineMode.Intersect);
                    using var ov = new SolidBrush(brand == CardBrand.Mastercard ? Color.FromArgb(255, 95, 0) : Color.FromArgb(110, 70, 160));
                    g.FillEllipse(ov, rightC);
                    g.Clip = clip;
                }
                break;
            }

            case CardBrand.Amex:
            {
                using var font = new Font("Arial", h * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
                var size = g.MeasureString("AMEX", font);
                var box = new RectangleF(area.Right - size.Width - h * 0.3f, area.Top + h * 0.1f, size.Width + h * 0.3f, h * 0.8f);
                using (var path = RoundedField.RoundRect(box, h * 0.12f))
                using (var bg = new SolidBrush(Color.White))
                    g.FillPath(bg, path);
                using var fg = new SolidBrush(Color.FromArgb(0, 111, 207));
                g.DrawString("AMEX", font, fg, box.Left + (box.Width - size.Width) / 2, box.Top + (box.Height - size.Height) / 2);
                break;
            }

            case CardBrand.Jcb:
            {
                var w = h * 0.55f;
                var colors = new[] { Color.FromArgb(0, 102, 178), Color.FromArgb(222, 0, 41), Color.FromArgb(0, 154, 68) };
                var letters = new[] { "J", "C", "B" };
                using var font = new Font("Arial", h * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel);
                for (var i = 0; i < 3; i++)
                {
                    var bar = new RectangleF(area.Right - (3 - i) * (w + 2), area.Top + h * 0.05f, w, h * 0.9f);
                    using var path = RoundedField.RoundRect(bar, w * 0.25f);
                    using var b = new SolidBrush(colors[i]);
                    g.FillPath(b, path);
                    var s = g.MeasureString(letters[i], font);
                    g.DrawString(letters[i], font, Brushes.White, bar.Left + (w - s.Width) / 2, bar.Top + (bar.Height - s.Height) / 2);
                }
                break;
            }

            case CardBrand.UnionPay:
            {
                var w = h * 0.55f;
                var colors = new[] { Color.FromArgb(221, 0, 42), Color.FromArgb(0, 69, 135), Color.FromArgb(0, 131, 132) };
                for (var i = 0; i < 3; i++)
                {
                    var x = area.Right - (3 - i) * (w * 0.8f) - w * 0.3f;
                    var pts = new[]
                    {
                        new PointF(x + w * 0.3f, area.Top), new PointF(x + w, area.Top),
                        new PointF(x + w * 0.7f, area.Bottom), new PointF(x, area.Bottom)
                    };
                    using var b = new SolidBrush(colors[i]);
                    g.FillPolygon(b, pts);
                }
                break;
            }

            default:
            {
                var (text, style, accent) = brand switch
                {
                    CardBrand.Visa => ("VISA", FontStyle.Bold | FontStyle.Italic, (Color?)null),
                    CardBrand.RuPay => ("RuPay", FontStyle.Bold | FontStyle.Italic, Color.FromArgb(247, 148, 30)),
                    CardBrand.Discover => ("DISCOVER", FontStyle.Bold, Color.FromArgb(255, 160, 40)),
                    CardBrand.Diners => ("Diners Club", FontStyle.Bold, (Color?)null),
                    _ => ("CARD", FontStyle.Bold | FontStyle.Italic, (Color?)null)
                };
                var px = brand == CardBrand.Visa ? h * 0.78f : h * 0.5f;
                using var font = new Font(brand == CardBrand.Visa ? "Arial Black" : "Segoe UI", px, style, GraphicsUnit.Pixel);
                var size = g.MeasureString(text, font);
                var arrowW = brand == CardBrand.RuPay ? h * 0.32f : 0;
                var x = area.Right - size.Width - arrowW;
                var y = area.Top + (h - size.Height) / 2;
                if (brand == CardBrand.Discover)
                {
                    // The "O" in DISCOVER is an orange disc: draw "DISC", the disc, then "VER".
                    using var fmt = StringFormat.GenericTypographic;
                    var discW = g.MeasureString("DISC", font, PointF.Empty, fmt).Width;
                    var verW = g.MeasureString("VER", font, PointF.Empty, fmt).Width;
                    var o = size.Height * 0.62f;
                    x = area.Right - discW - o - verW - 2;
                    g.DrawString("DISC", font, Brushes.White, x, y, fmt);
                    using var dot = new SolidBrush(accent!.Value);
                    g.FillEllipse(dot, x + discW + 1, y + (size.Height - o) / 2 - size.Height * 0.04f, o, o);
                    g.DrawString("VER", font, Brushes.White, x + discW + o + 2, y, fmt);
                    break;
                }
                g.DrawString(text, font, Brushes.White, x, y);
                if (brand == CardBrand.RuPay && accent is { } a)
                {
                    using var b = new SolidBrush(a);
                    var ax = x + size.Width;
                    g.FillPolygon(b, new[] { new PointF(ax, y + size.Height * 0.28f), new PointF(ax + arrowW, y + size.Height * 0.5f), new PointF(ax, y + size.Height * 0.72f) });
                }
                break;
            }
        }
    }
}
