using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// Rounded button. Primary = blue gradient fill; secondary = outlined. Implements IButtonControl so it can be
// a form's AcceptButton / CancelButton (Enter / Esc).
public class PillButton : Control, IButtonControl
{
    private bool _hovered;

    public bool Primary { get; set; }
    public IconKind? LeadingIcon { get; set; }
    // Text/icon colour for an outlined button (defaults to the accent colour when it has an icon).
    public Color? AccentColor { get; set; }
    // Draws the outlined variant as "selected" (tinted accent fill), for segmented filters.
    public bool Selected { get; set; }
    // Renders like a dropdown field: text left-aligned, chevron on the right.
    public bool Dropdown { get; set; }
    // Primary button drawn in the danger colour (red) for destructive actions.
    public bool Danger { get; set; }
    public DialogResult DialogResult { get; set; }

    public PillButton(string text, bool primary, Size size)
    {
        Text = text;
        Primary = primary;
        Size = size;
        Tag = AppTheme.NoTheme;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular);
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        EnabledChanged += (_, _) => { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); };
        TextChanged += (_, _) => Invalidate();
    }

    public void NotifyDefault(bool value) { }

    public void PerformClick()
    {
        if (Enabled) OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var parentBack = Parent?.BackColor ?? AppTheme.CardBg;
        g.Clear(parentBack);

        using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Math.Min(8, Height / 2f));
        Color textColor;
        if (Primary)
        {
            if (Danger)
            {
                var red = _hovered ? UiKit.Blend(Color.White, AppTheme.Danger, 0.12f) : AppTheme.Danger;
                using var fill = new SolidBrush(Enabled ? red : UiKit.Blend(red, parentBack, 0.45f));
                g.FillPath(fill, path);
            }
            else
            {
                AppTheme.FillPrimary(g, path, new RectangleF(0, 0, Width, Height), _hovered, Enabled, parentBack);
            }
            textColor = AppTheme.OnPrimary;
        }
        else
        {
            var accent = AccentColor ?? AppTheme.Accent;
            if (Selected || _hovered)
            {
                using var hover = new SolidBrush(Selected ? UiKit.Tint(accent, parentBack) : AppTheme.Hover);
                g.FillPath(hover, path);
            }
            using var pen = new Pen(Selected ? accent : AppTheme.FieldBorder);
            g.DrawPath(pen, path);
            textColor = Selected || LeadingIcon is not null ? accent : AppTheme.Text;
            if (!Enabled) textColor = AppTheme.SubText;
        }

        if (Dropdown && !Primary)
        {
            var size = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(12, 0, Width - 40, Height), Enabled ? AppTheme.Text : AppTheme.SubText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Icons.Draw(g, IconKind.ChevronDown, new Rectangle(Width - 24, (Height - 10) / 2, 10, 10), AppTheme.SubText, 1.6f);
            return;
        }

        var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
        var iconSize = LeadingIcon is null ? 0 : Height >= 44 ? 16 : 12;
        var gap = LeadingIcon is null || Text.Length == 0 ? 0 : 6;
        var x = (Width - (iconSize + gap + textSize.Width)) / 2;

        if (LeadingIcon is { } icon)
            Icons.Draw(g, icon, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), textColor, 1.8f);

        TextRenderer.DrawText(g, Text, Font, new Point(x + iconSize + gap, (Height - textSize.Height) / 2), textColor, TextFormatFlags.NoPrefix);
    }
}
