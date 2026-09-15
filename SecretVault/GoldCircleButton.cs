namespace SecretVault.Gui;

public class GoldCircleButton : Control
{
    private bool _hovered;

    public GoldCircleButton(int size = 40)
    {
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
        g.Clear(Parent?.BackColor ?? AppTheme.Sidebar);

        using var brush = new SolidBrush(_hovered ? AppTheme.GoldHover : AppTheme.Gold);
        g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);

        var inset = (int)(Width * 0.3f);
        var bounds = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);
        Icons.Draw(g, IconKind.Plus, bounds, Color.Black, 2.2f);
    }
}
