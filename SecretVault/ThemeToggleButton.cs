using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// Sidebar theme switcher. Wide: a segmented "Light | Dark" pill where clicking a side selects it.
// Narrow (collapsed rail): a single round button showing the theme you would switch to.
public class ThemeToggleButton : Control
{
    private const int SidePad = 16;
    private const int PillHeight = 38;
    private const int CompactThreshold = 120;

    private readonly Font _font = new("Segoe UI Semibold", 9f);
    private int _hoverSide = -1; // 0 = light, 1 = dark, -1 = none

    public ThemeToggleButton()
    {
        Tag = AppTheme.NoTheme;
        Height = 56;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
        AppTheme.ThemeChanged += Invalidate;
        new ToolTip().SetToolTip(this, "Switch light / dark theme");
    }

    private bool Compact => Width < CompactThreshold;

    private Rectangle PillBounds => Compact
        ? new Rectangle((Width - PillHeight) / 2, (Height - PillHeight) / 2, PillHeight, PillHeight)
        : new Rectangle(SidePad, (Height - PillHeight) / 2, Width - SidePad * 2, PillHeight);

    private int SideAt(Point p)
    {
        var pill = PillBounds;
        if (Compact || !pill.Contains(p)) return -1;
        return p.X < pill.Left + pill.Width / 2 ? 0 : 1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var side = SideAt(e.Location);
        if (side == _hoverSide) return;
        _hoverSide = side;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverSide = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Compact)
        {
            if (PillBounds.Contains(e.Location)) AppTheme.Toggle();
            return;
        }

        var side = SideAt(e.Location);
        var target = side == 0 ? ThemeMode.Light : ThemeMode.Dark;
        if (side >= 0 && target != AppTheme.Mode) AppTheme.SetMode(target);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.RailBg);

        var pill = PillBounds;
        var trackColor = UiKit.Blend(Color.Black, AppTheme.RailBg, 0.22f);
        using (var path = RoundedField.RoundRect(pill, pill.Height / 2f))
        using (var track = new SolidBrush(trackColor))
            g.FillPath(track, path);

        if (Compact)
        {
            // Show the icon of the theme a click switches to.
            var icon = new Rectangle(pill.Left + 10, pill.Top + 10, pill.Width - 20, pill.Height - 20);
            if (AppTheme.Mode == ThemeMode.Dark) DrawSun(g, icon, AppTheme.Gold);
            else DrawMoon(g, icon, Color.White);
            return;
        }

        var half = pill.Width / 2;
        var darkActive = AppTheme.Mode == ThemeMode.Dark;
        var activeRect = new Rectangle(darkActive ? pill.Left + half : pill.Left, pill.Top, half, pill.Height);
        activeRect.Inflate(-3, -3);
        var activeColor = Color.White;
        using (var path = RoundedField.RoundRect(activeRect, activeRect.Height / 2f))
        using (var brush = new SolidBrush(activeColor))
            g.FillPath(brush, path);

        // Hover hint on the inactive side.
        var inactiveSide = darkActive ? 0 : 1;
        if (_hoverSide == inactiveSide)
        {
            var hoverRect = new Rectangle(darkActive ? pill.Left : pill.Left + half, pill.Top, half, pill.Height);
            hoverRect.Inflate(-3, -3);
            using var path = RoundedField.RoundRect(hoverRect, hoverRect.Height / 2f);
            using var brush = new SolidBrush(UiKit.Blend(Color.White, trackColor, 0.12f));
            g.FillPath(brush, path);
        }

        DrawSegment(g, new Rectangle(pill.Left, pill.Top, half, pill.Height), "Light", isDark: false, active: !darkActive);
        DrawSegment(g, new Rectangle(pill.Left + half, pill.Top, pill.Width - half, pill.Height), "Dark", isDark: true, active: darkActive);
    }

    private void DrawSegment(Graphics g, Rectangle bounds, string text, bool isDark, bool active)
    {
        var fore = active ? AppTheme.RailBg : AppTheme.RailTextInactive;
        var textSize = TextRenderer.MeasureText(g, text, _font, Size.Empty, TextFormatFlags.NoPadding);
        const int iconSize = 14;
        const int gap = 6;
        var x = bounds.Left + (bounds.Width - (iconSize + gap + textSize.Width)) / 2;
        var icon = new Rectangle(x, bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize);

        if (isDark) DrawMoon(g, icon, active ? AppTheme.Accent : fore);
        else DrawSun(g, icon, active ? AppTheme.Gold : fore);

        TextRenderer.DrawText(g, text, _font, new Rectangle(icon.Right + gap, bounds.Top, textSize.Width + 4, bounds.Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private static void DrawSun(Graphics g, Rectangle rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, Math.Max(1.4f, rect.Width / 10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        var coreR = rect.Width * 0.24f;
        g.FillEllipse(brush, center.X - coreR, center.Y - coreR, coreR * 2, coreR * 2);

        var innerR = coreR + rect.Width * 0.1f;
        var outerR = rect.Width / 2f;
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            g.DrawLine(pen,
                center.X + (float)(Math.Cos(angle) * innerR), center.Y + (float)(Math.Sin(angle) * innerR),
                center.X + (float)(Math.Cos(angle) * outerR), center.Y + (float)(Math.Sin(angle) * outerR));
        }
    }

    // Crescent cut out with a clip region (not painted over), so it looks right on any background, including hover.
    private static void DrawMoon(Graphics g, Rectangle rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var bite = new GraphicsPath();
        bite.AddEllipse(rect.X + rect.Width * 0.38f, rect.Y - rect.Height * 0.12f, rect.Width, rect.Height);
        var previousClip = g.Clip;
        g.SetClip(bite, CombineMode.Exclude);
        g.FillEllipse(brush, rect);
        g.Clip = previousClip;
    }
}
