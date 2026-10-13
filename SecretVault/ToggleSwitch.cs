using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// iOS-style on/off switch.
public class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        Size = new Size(44, 24);
        Tag = AppTheme.NoTheme;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var back = Parent?.BackColor ?? AppTheme.CardBg;
        g.Clear(back);

        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = RoundedField.RoundRect(track, track.Height / 2f))
        {
            if (_checked)
                AppTheme.FillPrimary(g, path, track, false, Enabled, back);
            else
            {
                using var off = new SolidBrush(AppTheme.FieldBorder);
                g.FillPath(off, path);
            }
        }

        var d = Height - 6;
        var x = _checked ? Width - d - 3 : 3;
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x, 3, d, d);
    }
}
