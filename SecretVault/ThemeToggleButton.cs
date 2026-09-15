namespace SecretVault.Gui;

public class ThemeToggleButton : Button
{
    public ThemeToggleButton()
    {
        Tag = AppTheme.NoTheme;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 44;
        Cursor = Cursors.Hand;
        Text = string.Empty;
        Click += (_, _) => AppTheme.Toggle();
        AppTheme.ThemeChanged += Invalidate;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? AppTheme.Shell);

        var diameter = Math.Min(Width, Height) - 12;
        var rect = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);

        if (AppTheme.Mode == ThemeMode.Dark)
            DrawSun(g, rect);
        else
            DrawMoon(g, rect);
    }

    private static void DrawSun(Graphics g, Rectangle rect)
    {
        using var brush = new SolidBrush(AppTheme.Gold);
        using var pen = new Pen(AppTheme.Gold, 2);

        var coreInflate = -6;
        var core = Rectangle.Inflate(rect, coreInflate, coreInflate);
        g.FillEllipse(brush, core);

        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        var innerR = core.Width / 2f + 3;
        var outerR = rect.Width / 2f;

        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * 2 * i / 8;
            var x1 = center.X + (float)(Math.Cos(angle) * innerR);
            var y1 = center.Y + (float)(Math.Sin(angle) * innerR);
            var x2 = center.X + (float)(Math.Cos(angle) * outerR);
            var y2 = center.Y + (float)(Math.Sin(angle) * outerR);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private void DrawMoon(Graphics g, Rectangle rect)
    {
        using var brush = new SolidBrush(AppTheme.Accent);
        using var eraseBrush = new SolidBrush(Parent?.BackColor ?? AppTheme.Shell);

        g.FillEllipse(brush, rect);

        var bite = new Rectangle(rect.X + rect.Width / 3, rect.Y - 2, rect.Width, rect.Height + 4);
        g.FillEllipse(eraseBrush, bite);
    }
}
