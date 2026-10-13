using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// One slim bar on the right edge of a left-docked panel that is both:
//  - the panel's scrollbar for a list inside it (drag the thumb up/down), and
//  - the panel's resize handle (drag anywhere else on the bar sideways).
// The list draws no scrollbar of its own (NavListView), so this bar is its only scrollbar.
public class PanelSplitter : Control
{
    public const int MinPanelWidth = 180;
    private const int MinRecordAreaWidth = 420;
    private const int ThumbWidth = 6;
    private const int MinThumbHeight = 32;

    private readonly Control _panel;
    private readonly NavListView _list;

    private enum DragMode { None, Resize, Scroll }

    private DragMode _drag;
    private int _dragStartScreenX;
    private int _dragStartWidth;
    private int _thumbGrabOffset;
    private bool _thumbHot;

    public event EventHandler? SplitterMoved;

    public PanelSplitter(Control panel, NavListView list)
    {
        _panel = panel;
        _list = list;
        Dock = DockStyle.Left;
        Width = 12;
        Tag = AppTheme.NoTheme;
        BackColor = AppTheme.Canvas;
        Cursor = Cursors.VSplit;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        new ToolTip().SetToolTip(this, "Drag sideways to resize · drag the handle to scroll");

        // Repaint the thumb only when the list scrolls or its contents change.
        _list.Scrolled += (_, _) => Invalidate();
    }

    // ---------- scroll geometry ----------

    private int VisibleItems => _list.VisibleItems;
    private int MaxTopIndex => _list.MaxTopIndex;
    private bool CanScroll => MaxTopIndex > 0;

    private Rectangle TrackBounds
    {
        get
        {
            // Align the track with the list, which sits below the panel's header.
            var top = _list.Top;
            return new Rectangle((Width - ThumbWidth) / 2, top + 4, ThumbWidth, Math.Max(0, Height - top - 8));
        }
    }

    private Rectangle ThumbBounds
    {
        get
        {
            var track = TrackBounds;
            var thumbHeight = Math.Max(MinThumbHeight, track.Height * VisibleItems / Math.Max(1, _list.Items.Count));
            thumbHeight = Math.Min(thumbHeight, track.Height);
            var y = track.Top + (track.Height - thumbHeight) * Math.Min(_list.TopIndex, MaxTopIndex) / Math.Max(1, MaxTopIndex);
            return new Rectangle(track.Left - 2, y, ThumbWidth + 4, thumbHeight); // slightly wider hit area
        }
    }

    // ---------- mouse ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (CanScroll && ThumbBounds.Contains(e.Location))
        {
            _drag = DragMode.Scroll;
            _thumbGrabOffset = e.Y - ThumbBounds.Top;
        }
        else
        {
            _drag = DragMode.Resize;
            _dragStartScreenX = Cursor.Position.X;
            _dragStartWidth = _panel.Width;
        }
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        switch (_drag)
        {
            case DragMode.Resize:
                _panel.Width = Math.Clamp(_dragStartWidth + Cursor.Position.X - _dragStartScreenX, MinPanelWidth, MaxPanelWidth());
                break;

            case DragMode.Scroll:
                var track = TrackBounds;
                var thumb = ThumbBounds;
                var travel = Math.Max(1, track.Height - thumb.Height);
                var fraction = Math.Clamp((e.Y - _thumbGrabOffset - track.Top) / (float)travel, 0f, 1f);
                _list.TopIndex = (int)Math.Round(fraction * MaxTopIndex);
                Invalidate();
                break;

            default:
                var overThumb = CanScroll && ThumbBounds.Contains(e.Location);
                Cursor = overThumb ? Cursors.Default : Cursors.VSplit;
                if (overThumb != _thumbHot) { _thumbHot = overThumb; Invalidate(); }
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var wasResizing = _drag == DragMode.Resize;
        _drag = DragMode.None;
        Capture = false;
        Invalidate();
        if (wasResizing) SplitterMoved?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _thumbHot = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _list.TopIndex -= Math.Sign(e.Delta) * 3;
    }

    private int MaxPanelWidth()
    {
        if (Parent is null) return 600;
        var rightDocked = Parent.Controls.Cast<Control>().Where(c => c.Visible && c.Dock == DockStyle.Right).Sum(c => c.Width);
        return Math.Max(MinPanelWidth, Parent.ClientSize.Width - Parent.Padding.Horizontal - rightDocked - Width - MinRecordAreaWidth);
    }

    // ---------- paint ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var track = TrackBounds;

        // Always a quiet 1px divider; the ↔ cursor is what signals that it can be dragged.
        using (var line = new Pen(AppTheme.Border, 1f))
            g.DrawLine(line, Width / 2f, track.Top, Width / 2f, track.Bottom);

        if (!CanScroll) return;

        var thumb = ThumbBounds;
        var pill = new RectangleF(track.Left, thumb.Top, ThumbWidth, thumb.Height);
        // Neutral grey handle, a little darker while hovered / dragged.
        var color = UiKit.Blend(AppTheme.SubText, BackColor, _drag == DragMode.Scroll || _thumbHot ? 0.85f : 0.55f);
        using var path = RoundedField.RoundRect(pill, ThumbWidth / 2f);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }
}
