namespace SecretVault.Gui;

// Round "+" button used for Create New when the sidebar is collapsed.
public class PrimaryCircleButton : Control
{
    private bool _hovered;

    public PrimaryCircleButton(int size = 40)
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
        var back = Parent?.BackColor ?? AppTheme.Sidebar;
        g.Clear(back);

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddEllipse(0, 0, Width - 1, Height - 1);
        AppTheme.FillPrimary(g, path, new RectangleF(0, 0, Width, Height), _hovered, true, back);

        var inset = (int)(Width * 0.3f);
        var bounds = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);
        Icons.Draw(g, IconKind.Plus, bounds, AppTheme.OnPrimary, 2.2f);
    }
}
