namespace SecretVault.Gui;

public class IconButton : Control
{
    public IconKind Icon { get; set; }
    public Color? IconColor { get; set; }
    public bool Circular { get; set; } = true;

    private bool _hovered;

    public IconButton(IconKind icon, int size = 30)
    {
        Icon = icon;
        Size = new Size(size, size);
        Tag = AppTheme.NoTheme;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        AppTheme.ThemeChanged += Invalidate;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? AppTheme.CardBg);

        if (_hovered)
        {
            using var hoverBrush = new SolidBrush(AppTheme.Hover);
            if (Circular)
                g.FillEllipse(hoverBrush, 0, 0, Width, Height);
            else
                g.FillRectangle(hoverBrush, 0, 0, Width, Height);
        }

        var color = IconColor ?? AppTheme.SubText;
        var inset = (int)(Width * 0.26f);
        var bounds = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);
        Icons.Draw(g, Icon, bounds, color, background: Parent?.BackColor);
    }
}
