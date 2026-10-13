using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// Folder chooser: a collapsible tree of folders (paths split on "\" or "/") with a search box, plus an editable
// path field so a new folder can be created by typing it. Used by the record editor and by "Move to folder".
public class FolderPickerForm : Form
{
    private const int Pad = 20;

    private sealed record Row(string Path, string Label, int Depth, bool HasChildren);

    private readonly List<string> _paths;                         // every folder path, including implicit parents
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search folders…" };
    private readonly TextBox _pathBox = new() { PlaceholderText = "Folder path, e.g. Work\\Servers" };
    private readonly NavListView _list = new() { ShowScrollbar = true, ItemHeight = 34 };
    private readonly Font _rowFont = new("Segoe UI", 9.75f);
    private readonly Font _rowBold = new("Segoe UI Semibold", 9.75f);
    private readonly Font _pathFont = new("Segoe UI", 8.25f);

    public string? SelectedFolder { get; private set; }

    public FolderPickerForm(IEnumerable<string> folders, string? current, string title = "Choose folder", string actionText = "Select")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(440, 560);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        _paths = AllPaths(folders.Append("General"));
        current = Normalize(current ?? "");
        if (current.Length > 0)
        {
            // Open the branch that contains the current folder.
            var parts = current.Split('\\');
            for (var i = 1; i < parts.Length; i++) _expanded.Add(string.Join("\\", parts.Take(i)));
        }

        var card = UiKit.NewCard();
        card.SetBounds(Pad - 4, Pad - 4, ClientSize.Width - (Pad - 4) * 2, ClientSize.Height - 68 - (Pad - 4) * 2 + 4);
        var inner = card.Width - 32;
        Controls.Add(card);

        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(14, 14), Tag = AppTheme.NoTheme });

        var search = new RoundedField(_searchBox, 38) { LeadingIcon = IconKind.Search };
        search.SetBounds(16, 50, inner, 38);
        _searchBox.TextChanged += (_, _) => Rebuild();
        _searchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Down) { _list.Focus(); if (_list.SelectedIndex < 0) _list.SelectedIndex = 0; e.Handled = true; } };
        card.Controls.Add(search);

        _list.SetBounds(16, 98, inner, card.Height - 98 - 82);
        _list.BackColor = AppTheme.CardBg;
        _list.DrawItem += DrawRow;
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedItem is Row row) _pathBox.Text = row.Path; };
        _list.MouseDown += (_, e) => ToggleIfChevron(e.Location);
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is Row) Accept(); };
        _list.KeyDown += (_, e) =>
        {
            if (_list.SelectedItem is not Row row) return;
            if (e.KeyCode == Keys.Right && row.HasChildren && !_expanded.Contains(row.Path)) { _expanded.Add(row.Path); Rebuild(row.Path); }
            else if (e.KeyCode == Keys.Left && _expanded.Remove(row.Path)) Rebuild(row.Path);
            else if (e.KeyCode == Keys.Enter) Accept();
        };
        card.Controls.Add(_list);

        card.Controls.Add(UiKit.Caption("Folder (type a new path to create one)", new Point(15, card.Height - 74)));
        var pathField = new RoundedField(_pathBox, 38) { LeadingIcon = IconKind.Folder };
        pathField.SetBounds(16, card.Height - 52, inner, 38);
        _pathBox.Text = current.Length > 0 ? current : "General";
        card.Controls.Add(pathField);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        footer.Paint += (_, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        var ok = new PillButton(actionText, primary: true, new Size(110, 38)) { Location = new Point(ClientSize.Width - 20 - 110, 13) };
        var cancel = new PillButton("Cancel", primary: false, new Size(96, 38)) { Location = new Point(ok.Left - 10 - 96, 13) };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;

        Rebuild(current);
        ActiveControl = _searchBox;
    }

    private static string Normalize(string path) =>
        string.Join("\\", path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // All folders plus their parents ("a\b\c" also yields "a" and "a\b"), sorted.
    private static List<string> AllPaths(IEnumerable<string> folders)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var parts = Normalize(folder).Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i <= parts.Length; i++) set.Add(string.Join("\\", parts.Take(i)));
        }
        return set.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool FolderHasChildren(string path) => _paths.Any(p => p.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase));

    private void Rebuild(string? select = null)
    {
        var term = _searchBox.Text.Trim();
        var rows = new List<Row>();
        if (term.Length > 0)
        {
            // Search: a flat list of matching folders, shown with their full path.
            rows.AddRange(_paths.Where(p => p.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(p => new Row(p, p, 0, false)));
        }
        else
        {
            void Add(string? parent, int depth)
            {
                var prefix = parent is null ? "" : parent + "\\";
                foreach (var path in _paths.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Length > prefix.Length && p.IndexOf('\\', prefix.Length) < 0))
                {
                    var hasChildren = FolderHasChildren(path);
                    rows.Add(new Row(path, path[prefix.Length..], depth, hasChildren));
                    if (hasChildren && _expanded.Contains(path)) Add(path, depth + 1);
                }
            }
            Add(null, 0);
        }

        // Clear the old selection first: otherwise row 0 can look selected after a rebuild without counting as a
        // change, so clicking it wouldn't update the folder field.
        _list.SelectedIndex = -1;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(rows);
        _list.EndUpdate();

        var target = select ?? _pathBox.Text;
        var index = rows.FindIndex(r => r.Path.Equals(Normalize(target), StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _list.SelectedIndex = index;
            _list.TopIndex = Math.Max(0, index - 3);
        }
    }

    private const int Indent = 16;
    private const int ChevronZone = 20;

    private void ToggleIfChevron(Point p)
    {
        var index = _list.IndexFromPoint(p);
        if (index < 0 || _list.Items[index] is not Row { HasChildren: true } row) return;
        var left = 4 + row.Depth * Indent;
        if (p.X < left || p.X > left + ChevronZone) return;
        if (!_expanded.Add(row.Path)) _expanded.Remove(row.Path);
        BeginInvoke(() => Rebuild(row.Path));
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (_list.Items[e.Index] is not Row row) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var hot = (e.State & DrawItemState.HotLight) != 0;
        var rect = new Rectangle(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width - 1, e.Bounds.Height - 2);
        var back = selected ? UiKit.Tint(AppTheme.Accent, AppTheme.CardBg) : hot ? AppTheme.Hover : AppTheme.CardBg;
        using (var b = new SolidBrush(AppTheme.CardBg)) g.FillRectangle(b, e.Bounds);
        if (back != AppTheme.CardBg)
        {
            using var path = RoundedField.RoundRect(rect, 8);
            using var b = new SolidBrush(back);
            g.FillPath(b, path);
        }

        var x = rect.X + 4 + row.Depth * Indent;
        if (row.HasChildren)
            Icons.Draw(g, _expanded.Contains(row.Path) ? IconKind.ChevronDown : IconKind.ChevronRight,
                new Rectangle(x + 5, rect.Y + (rect.Height - 9) / 2, 9, 9), AppTheme.SubText, 1.5f, back);
        x += ChevronZone;
        Icons.Draw(g, IconKind.Folder, new Rectangle(x, rect.Y + (rect.Height - 16) / 2, 16, 16), selected ? AppTheme.Accent : AppTheme.SubText, 1.5f, back);
        x += 26;

        var textRect = new Rectangle(x, rect.Y, rect.Right - x - 8, rect.Height);
        if (_searchBox.TextLength > 0 && row.Path.Contains('\\'))
        {
            // Search results: leaf name with the parent path in small grey text.
            var cut = row.Path.LastIndexOf('\\');
            var leaf = row.Path[(cut + 1)..];
            TextRenderer.DrawText(g, leaf, selected ? _rowBold : _rowFont, new Rectangle(x, rect.Y + 2, textRect.Width, rect.Height / 2 + 2), AppTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, row.Path[..cut], _pathFont, new Rectangle(x, rect.Y + rect.Height / 2 + 1, textRect.Width, rect.Height / 2 - 2), AppTheme.SubText,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else
        {
            TextRenderer.DrawText(g, row.Label, selected ? _rowBold : _rowFont, textRect, AppTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private void Accept()
    {
        var path = Normalize(_pathBox.Text);
        SelectedFolder = path.Length > 0 ? path : "General";
        DialogResult = DialogResult.OK;
        Close();
    }
}
