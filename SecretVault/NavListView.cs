using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// A fully custom-drawn, double-buffered list used for every list in the app (folders, records, cards, audit).
// It mimics the small part of the ListBox API the screens use (Items, SelectedIndex/Item, TopIndex, DrawItem,
// IndexFromPoint) but paints every pixel itself into an off-screen buffer, so scrolling, resizing and selection
// never flicker or leave stale pixels behind.
// Optional slim scrollbar (ShowScrollbar) on the right; the folder tree uses PanelSplitter as its scrollbar instead.
public class NavListView : Control
{
    private const int ScrollbarWidth = 6;
    private const int ScrollbarGutter = 12;
    private const int MinThumbHeight = 32;

    private int _selectedIndex = -1;
    private int _topIndex;
    private int _hotIndex = -1;
    private int _updating;
    private bool _thumbHot;
    private bool _draggingThumb;
    private int _thumbGrabOffset;

    public List<object> Items { get; } = new();
    public int ItemHeight { get; set; } = 38;
    public bool ShowScrollbar { get; set; }

    // Row drawn as hovered while something is dragged over it (-1 = none).
    private int _dropHighlightIndex = -1;
    public int DropHighlightIndex
    {
        get => _dropHighlightIndex;
        set { if (_dropHighlightIndex == value) return; _dropHighlightIndex = value; Invalidate(); }
    }

    public event DrawItemEventHandler? DrawItem;
    public event EventHandler? SelectedIndexChanged;
    public event EventHandler? Scrolled;

    public NavListView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Tag = AppTheme.NoTheme;
        TabStop = true;
    }

    // ---------- ListBox-like API ----------

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Items.Count == 0 ? -1 : Math.Clamp(value, -1, Items.Count - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            EnsureVisible(clamped);
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public void ClearSelected() => SelectedIndex = -1;

    public int VisibleItems => Math.Max(1, ClientSize.Height / ItemHeight);
    public int MaxTopIndex => Math.Max(0, Items.Count - VisibleItems);
    private bool CanScroll => ShowScrollbar && MaxTopIndex > 0;

    // Width available to rows (the slim scrollbar gets its own gutter so it never covers row content).
    private int RowWidth => ClientSize.Width - (CanScroll ? ScrollbarGutter : 0);

    public int TopIndex
    {
        get => _topIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxTopIndex);
            if (clamped == _topIndex) return;
            _topIndex = clamped;
            Invalidate();
            Scrolled?.Invoke(this, EventArgs.Empty);
        }
    }

    public int GetItemHeight(int index) => ItemHeight;

    public void BeginUpdate() => _updating++;

    public void EndUpdate()
    {
        if (_updating > 0) _updating--;
        if (_updating > 0) return;
        // The items were replaced, so the old selection is meaningless. Keeping it would leave a row looking
        // selected that a click can't "re-select" (no change => no SelectedIndexChanged => details never open).
        // Callers that want a selection after rebuilding set SelectedIndex again.
        _selectedIndex = -1;
        _topIndex = Math.Clamp(_topIndex, 0, MaxTopIndex);
        _hotIndex = -1;
        Invalidate();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    public int IndexFromPoint(Point p)
    {
        if (p.Y < 0 || p.X < 0 || p.X >= RowWidth) return -1;
        var index = _topIndex + p.Y / ItemHeight;
        return index < Items.Count ? index : -1;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0) return;
        if (index < _topIndex) TopIndex = index;
        else if (index >= _topIndex + VisibleItems) TopIndex = index - VisibleItems + 1;
    }

    // ---------- scrollbar geometry ----------

    private Rectangle ThumbBounds
    {
        get
        {
            var trackHeight = ClientSize.Height - 8;
            var thumbHeight = Math.Clamp(trackHeight * VisibleItems / Math.Max(1, Items.Count), MinThumbHeight, trackHeight);
            var y = 4 + (trackHeight - thumbHeight) * _topIndex / Math.Max(1, MaxTopIndex);
            return new Rectangle(ClientSize.Width - ScrollbarGutter / 2 - ScrollbarWidth / 2 - 2, y, ScrollbarWidth + 4, thumbHeight);
        }
    }

    // ---------- input ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && CanScroll && e.X >= RowWidth)
        {
            var thumb = ThumbBounds;
            if (thumb.Contains(e.Location))
            {
                _draggingThumb = true;
                _thumbGrabOffset = e.Y - thumb.Top;
                Capture = true;
            }
            else
            {
                TopIndex += e.Y < thumb.Top ? -VisibleItems : VisibleItems; // page up / down on the track
            }
            base.OnMouseDown(e);
            return;
        }

        // Select first, then raise MouseDown (same order as a native ListBox). Right-click selects too (context menus).
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            var index = IndexFromPoint(e.Location);
            if (index >= 0) SelectedIndex = index;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingThumb)
        {
            var travel = Math.Max(1, ClientSize.Height - 8 - ThumbBounds.Height);
            var fraction = Math.Clamp((e.Y - _thumbGrabOffset - 4) / (float)travel, 0f, 1f);
            TopIndex = (int)Math.Round(fraction * MaxTopIndex);
            return;
        }

        var thumbHot = CanScroll && ThumbBounds.Contains(e.Location);
        var hot = IndexFromPoint(e.Location);
        if (hot == _hotIndex && thumbHot == _thumbHot) return;
        _hotIndex = hot;
        _thumbHot = thumbHot;
        Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_draggingThumb) return;
        _draggingThumb = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hotIndex == -1 && !_thumbHot) return;
        _hotIndex = -1;
        _thumbHot = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        TopIndex -= Math.Sign(e.Delta) * 3;
        // Keep the hover highlight on the row now under the cursor.
        _hotIndex = IndexFromPoint(PointToClient(Cursor.Position));
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var target = e.KeyCode switch
        {
            Keys.Up => _selectedIndex - 1,
            Keys.Down => _selectedIndex + 1,
            Keys.Home => 0,
            Keys.End => Items.Count - 1,
            Keys.PageUp => _selectedIndex - VisibleItems,
            Keys.PageDown => _selectedIndex + VisibleItems,
            _ => int.MinValue
        };
        if (target == int.MinValue || Items.Count == 0) return;
        SelectedIndex = Math.Clamp(target, 0, Items.Count - 1);
        e.Handled = true;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _topIndex = Math.Clamp(_topIndex, 0, MaxTopIndex);
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    // ---------- paint ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_updating > 0) return;

        var rowWidth = RowWidth;
        for (var i = _topIndex; i < Items.Count; i++)
        {
            var y = (i - _topIndex) * ItemHeight;
            if (y >= ClientSize.Height) break;
            var state = DrawItemState.None;
            if (i == _selectedIndex) state |= DrawItemState.Selected;
            if (i == _hotIndex || i == _dropHighlightIndex) state |= DrawItemState.HotLight;
            DrawItem?.Invoke(this, new DrawItemEventArgs(g, Font, new Rectangle(0, y, rowWidth, ItemHeight), i, state));
        }

        if (!CanScroll) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var thumb = ThumbBounds;
        var pill = new RectangleF(thumb.Left + 2, thumb.Top, ScrollbarWidth, thumb.Height);
        using var path = RoundedField.RoundRect(pill, ScrollbarWidth / 2f);
        using var brush = new SolidBrush(UiKit.Blend(AppTheme.SubText, BackColor, _draggingThumb || _thumbHot ? 0.85f : 0.45f));
        g.FillPath(brush, path);
    }
}
