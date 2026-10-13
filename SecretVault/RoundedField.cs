using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// A rounded, borderless-looking input: hosts a TextBox with no border of its own, paints a rounded
// outline that turns accent-coloured on focus (or red when flagged), and can carry leading/trailing icons.
public class RoundedField : Panel
{
    private const int Radius = 8;
    private const int InnerPadX = 12;

    private readonly List<Control> _trailing = new();
    private bool _focused;
    private bool _hasError;
    private IconKind? _leadingIcon;

    public TextBox Input { get; }

    // Optional icon drawn inside the field before the text (e.g. a search glass).
    public IconKind? LeadingIcon
    {
        get => _leadingIcon;
        set { _leadingIcon = value; LayoutInner(); Invalidate(); }
    }

    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; Invalidate(); }
    }

    public RoundedField(TextBox input, int height = 38)
    {
        Input = input;
        Tag = AppTheme.NoTheme;
        Height = height;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.IBeam;

        input.Tag = AppTheme.NoTheme;
        input.BorderStyle = BorderStyle.None;
        input.Font = new Font("Segoe UI", 10f);
        input.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        input.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        input.TextChanged += (_, _) => { if (_hasError) HasError = false; };
        ApplyTheme();

        Controls.Add(input);
        Click += (_, _) => input.Focus();
        LayoutInner();
    }

    // Re-reads palette colours; call after a theme switch for long-lived fields.
    public void ApplyTheme()
    {
        BackColor = AppTheme.Field;
        Input.BackColor = AppTheme.Field;
        Input.ForeColor = AppTheme.Text;
        foreach (var c in _trailing) c.Invalidate();
        Invalidate();
    }

    public void AddTrailing(Control control)
    {
        _trailing.Add(control);
        Controls.Add(control);
        control.VisibleChanged += (_, _) => LayoutInner();
        LayoutInner();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutInner();
    }

    private void LayoutInner()
    {
        if (Input is null) return;

        var right = Width - 6;
        for (var i = _trailing.Count - 1; i >= 0; i--)
        {
            var c = _trailing[i];
            c.Location = new Point(right - c.Width, (Height - c.Height) / 2);
            if (c.Visible) right = c.Left - 2;
        }

        var inputRight = right < Width - 6 ? right - 2 : Width - InnerPadX;
        var left = _leadingIcon is null ? InnerPadX : InnerPadX + 26;
        if (Input.Multiline)
        {
            Input.Location = new Point(left, 9);
            Input.Size = new Size(Math.Max(0, inputRight - left), Math.Max(0, Height - 18));
        }
        else
        {
            // A borderless TextBox is sized exactly to the font, which clips descenders (g, p, y) in the
            // placeholder text; give it a little extra height and centre that instead.
            Input.AutoSize = false;
            var inputHeight = Input.PreferredHeight + 4;
            Input.SetBounds(left, (Height - inputHeight) / 2, Math.Max(0, inputRight - left), inputHeight);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? AppTheme.CardBg);

        var borderColor = _hasError ? AppTheme.Danger : _focused ? AppTheme.Accent : AppTheme.FieldBorder;
        var borderWidth = _hasError || _focused ? 1.6f : 1f;

        using var path = RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
        using var fill = new SolidBrush(AppTheme.Field);
        using var pen = new Pen(borderColor, borderWidth);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        if (_leadingIcon is { } icon)
            Icons.Draw(g, icon, new Rectangle(InnerPadX, (Height - 16) / 2, 16, 16), _focused ? AppTheme.Accent : AppTheme.SubText, 1.7f, AppTheme.Field);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
