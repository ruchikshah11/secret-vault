using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// Shared drawing helpers so every screen uses the same cards, avatars and chips.
public static class UiKit
{
    private static readonly Color[] AvatarPalette =
    {
        Color.FromArgb(66, 133, 244), Color.FromArgb(52, 168, 83), Color.FromArgb(234, 134, 0),
        Color.FromArgb(171, 71, 188), Color.FromArgb(0, 150, 166), Color.FromArgb(229, 57, 53),
        Color.FromArgb(63, 81, 181), Color.FromArgb(216, 27, 96),
    };

    private static readonly Dictionary<int, Font> AvatarFonts = new();

    // Deterministic (string.GetHashCode is randomised per process) so a record keeps its colour.
    public static Color AvatarColor(string seed)
    {
        var hash = 0;
        foreach (var ch in seed.ToUpperInvariant()) hash = unchecked(hash * 31 + ch);
        return AvatarPalette[(hash & int.MaxValue) % AvatarPalette.Length];
    }

    public static void DrawAvatar(Graphics g, Rectangle r, string name)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(AvatarColor(name)))
            g.FillEllipse(brush, r);

        var px = (int)Math.Round(r.Height * 0.42f);
        if (!AvatarFonts.TryGetValue(px, out var font))
            AvatarFonts[px] = font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
        var trimmed = name.Trim();
        var initial = trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
        TextRenderer.DrawText(g, initial, font, r, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    // Rounded card: clears the corners to the parent's colour, then fills and outlines.
    public static void PaintCard(Graphics g, Control card, float radius = 14)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(card.Parent?.BackColor ?? AppTheme.Canvas);
        using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, card.Width - 1.5f, card.Height - 1.5f), radius);
        using var fill = new SolidBrush(AppTheme.CardBg);
        using var pen = new Pen(AppTheme.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    public static Panel NewCard()
    {
        var card = new Panel { Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        card.Paint += (_, e) => PaintCard(e.Graphics, card);
        card.Resize += (_, _) => card.Invalidate();
        return card;
    }

    // Small rounded "chip" with an optional icon; returns its width.
    public static int DrawChip(Graphics g, Point location, string text, Font font, Color fore, Color back, IconKind? icon = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var textSize = TextRenderer.MeasureText(g, text, font, Size.Empty, flags);
        var iconW = icon is null ? 0 : 16;
        var width = 10 + iconW + textSize.Width + 10;
        var height = 22;
        using (var path = RoundedField.RoundRect(new RectangleF(location.X, location.Y, width, height), height / 2f))
        using (var brush = new SolidBrush(back))
            g.FillPath(brush, path);
        if (icon is { } k)
            Icons.Draw(g, k, new Rectangle(location.X + 9, location.Y + 5, 12, 12), fore, 1.5f, back);
        TextRenderer.DrawText(g, text, font, new Rectangle(location.X + 10 + iconW, location.Y, textSize.Width + 2, height), fore,
            flags | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        return width;
    }

    // An opaque tint of a colour over a background (WinForms controls cannot draw alpha backgrounds).
    public static Color Tint(Color c, Color background) =>
        Blend(c, background, AppTheme.Mode == ThemeMode.Dark ? 0.22f : 0.13f);

    public static Color Blend(Color c, Color background, float amount)
    {
        int Mix(int a, int b) => (int)(b + (a - b) * amount);
        return Color.FromArgb(Mix(c.R, background.R), Mix(c.G, background.G), Mix(c.B, background.B));
    }

    public static Label Caption(string text, Point location, bool required = false) => new()
    {
        Text = text + (required ? " *" : ""),
        UseMnemonic = false,
        AutoSize = true,
        Location = location,
        Font = new Font("Segoe UI Semibold", 9f),
        ForeColor = AppTheme.SubText,
        Tag = AppTheme.NoTheme
    };

    public static string StrengthText(Crypto.StrengthLevel level) => level switch
    {
        Crypto.StrengthLevel.VeryWeak => "Very weak",
        Crypto.StrengthLevel.VeryStrong => "Very strong",
        _ => level.ToString()
    };
}
