using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

public class MainForm : Form
{
    private const string AllItemsFolder = "All Items";
    private const int ClipboardClearSeconds = 20;

    private readonly FileVaultStore _store = new();
    private readonly VaultData _data;
    private readonly string _masterPassword;

    private readonly TextBox _searchBox = new();
    private readonly ThemeToggleButton _themeToggle = new();
    private readonly ListBox _folderList = new();
    private readonly ListBox _entryListBox = new();
    private readonly Label _resultCountLabel = new();
    private readonly ComboBox _sortCombo = new();
    private bool _sortByDateModified;
    private readonly Panel _detailPanel = new() { Tag = AppTheme.RoleCard };
    private readonly Panel _detailContent = new() { Dock = DockStyle.Fill, AutoScroll = true, Tag = AppTheme.NoTheme };
    private Panel _folderColumn = null!;
    private Panel _folderDivider = null!;
    private Panel _recordArea = null!;
    private Panel _detailDivider = null!;
    private SecurityAuditPanel _auditPanel = null!;
    private IdentityPaymentsPanel _identityPanel = null!;
    private Panel _navPanel = null!;

    private readonly System.Windows.Forms.Timer _totpTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _clipboardClearTimer = new() { Interval = ClipboardClearSeconds * 1000 };
    private string? _pendingClipboardValue;

    private VaultEntry? _selectedEntry;
    private Label? _totpCodeLabel;
    private bool _railExpanded = true;
    private MainView _currentView = MainView.Vault;

    private enum MainView { Vault, Deleted, SecurityAudit, IdentityPayments }

    public MainForm(VaultData data, string masterPassword)
    {
        _data = data;
        _masterPassword = masterPassword;

        Text = "SecretVault";
        Size = new Size(1240, 780);
        MinimumSize = new Size(960, 580);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Shell;
        Font = new Font("Segoe UI", 9.5f);

        BuildLayout();
        AppTheme.Apply(this);

        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);
        AppTheme.ThemeChanged += OnThemeChanged;
        FormClosed += (_, _) => AppTheme.ThemeChanged -= OnThemeChanged;

        _totpTimer.Tick += (_, _) => UpdateTotpLabel();
        _totpTimer.Start();

        _clipboardClearTimer.Tick += (_, _) => ClearClipboardIfPending();

        RefreshFolderList();
        RefreshEntryList();
    }

    private void OnThemeChanged()
    {
        DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);
        BackColor = AppTheme.Shell;
        AppTheme.Apply(this);
        _folderList.BackColor = AppTheme.Shell;
        _entryListBox.BackColor = AppTheme.Shell;
        _navPanel.BackColor = AppTheme.Sidebar;
        _folderList.Invalidate();
        _entryListBox.Invalidate();
        _navPanel.Invalidate(true);
        RenderDetail(_selectedEntry);

        if (_currentView == MainView.SecurityAudit)
            _auditPanel.RefreshData();
        else if (_currentView == MainView.IdentityPayments)
            _identityPanel.RefreshView();
    }

    private void SwitchView(MainView view)
    {
        _currentView = view;
        _searchBox.Text = string.Empty;

        var showVaultLike = view is MainView.Vault or MainView.Deleted;
        _folderColumn.Visible = view == MainView.Vault;
        _folderDivider.Visible = view == MainView.Vault;
        _recordArea.Visible = showVaultLike;
        _detailPanel.Visible = showVaultLike;
        _auditPanel.Visible = view == MainView.SecurityAudit;
        _identityPanel.Visible = view == MainView.IdentityPayments;

        if (view == MainView.SecurityAudit)
            _auditPanel.RefreshData();
        else if (view == MainView.IdentityPayments)
            _identityPanel.RefreshView();
        else
        {
            RefreshFolderList();
            RefreshEntryList();
        }

        _navPanel.Invalidate(true);
    }

    private void BuildLayout()
    {
        // --- Left nav rail ---
        var rail = new Panel { Dock = DockStyle.Left, Width = 230, Tag = AppTheme.RoleSidebar, Padding = new Padding(14, 16, 14, 16) };

        var hamburger = new IconButton(IconKind.Hamburger, 26) { Location = new Point(0, 0), IconColor = AppTheme.RailIconInactive };

        var createButton = new Button
        {
            Text = "+  Create New",
            Location = new Point(0, 40),
            Size = new Size(200, 42),
            BackColor = AppTheme.Gold,
            ForeColor = Color.Black,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Tag = AppTheme.NoTheme
        };
        createButton.FlatAppearance.BorderSize = 0;
        createButton.FlatAppearance.MouseOverBackColor = AppTheme.GoldHover;
        createButton.Click += (_, _) => OnCreateNew();
        AppTheme.ThemeChanged += () => createButton.FlatAppearance.MouseOverBackColor = AppTheme.GoldHover;

        var createCircleButton = new GoldCircleButton(36) { Location = new Point(0, 40), Visible = false };
        createCircleButton.Click += (_, _) => OnCreateNew();

        _navPanel = new Panel { Location = new Point(0, 96), Size = new Size(200, 220), BackColor = AppTheme.Sidebar, Tag = AppTheme.NoTheme };
        var navY = 0;
        navY = AddNavRow(_navPanel, IconKind.Shield, "My Vault", () => _currentView == MainView.Vault, navY, () => SwitchView(MainView.Vault));
        navY = AddNavRow(_navPanel, IconKind.Card, "Identity & Payments", () => _currentView == MainView.IdentityPayments, navY, () => SwitchView(MainView.IdentityPayments));
        navY = AddNavRow(_navPanel, IconKind.Search, "Security Audit", () => _currentView == MainView.SecurityAudit, navY, () => SwitchView(MainView.SecurityAudit));
        navY = AddNavRow(_navPanel, IconKind.Eye, "BreachWatch", () => false, navY, null);
        AddNavRow(_navPanel, IconKind.Trash, "Deleted Items", () => _currentView == MainView.Deleted, navY, () => SwitchView(MainView.Deleted));

        void LayoutRailChildren()
        {
            var railWidth = _railExpanded ? 230 : 64;
            hamburger.Location = new Point((railWidth - hamburger.Width) / 2, 16);
            createButton.Location = new Point(14, 56);
            createCircleButton.Location = new Point((railWidth - createCircleButton.Width) / 2, 56);
            createButton.Visible = _railExpanded;
            createCircleButton.Visible = !_railExpanded;
            _navPanel.Location = new Point(_railExpanded ? 14 : (railWidth - _navPanel.Width) / 2, 112);
        }

        LayoutRailChildren();

        hamburger.Click += (_, _) =>
        {
            SuspendLayout();
            _railExpanded = !_railExpanded;
            rail.Width = _railExpanded ? 230 : 64;
            _navPanel.Width = _railExpanded ? 200 : 36;
            LayoutRailChildren();
            ResumeLayout(true);
            PerformLayout();
            Invalidate(true);
            Update();
        };

        _themeToggle.Dock = DockStyle.Bottom;
        rail.Controls.Add(hamburger);
        rail.Controls.Add(createButton);
        rail.Controls.Add(createCircleButton);
        rail.Controls.Add(_navPanel);
        rail.Controls.Add(_themeToggle);

        var railDivider = CreateDivider(DockStyle.Left);

        // --- Content area ---
        var content = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.RoleShell };

        var topBar = new Panel { Dock = DockStyle.Top, Height = 64, Tag = AppTheme.RoleShell, Padding = new Padding(12, 16, 24, 16) };
        var searchWrap = new Panel { Dock = DockStyle.Left, Width = 420, Height = 32, Tag = AppTheme.RoleShell };
        var searchIcon = new IconButton(IconKind.Search, 20) { Location = new Point(0, 6), Circular = false, Enabled = false };
        _searchBox.Location = new Point(28, 0);
        _searchBox.Width = 392;
        _searchBox.Height = 30;
        _searchBox.Font = new Font("Segoe UI", 10f);
        _searchBox.BorderStyle = BorderStyle.FixedSingle;
        _searchBox.PlaceholderText = "Search";
        _searchBox.TextChanged += (_, _) => RefreshEntryList();
        searchWrap.Controls.Add(searchIcon);
        searchWrap.Controls.Add(_searchBox);
        topBar.Controls.Add(searchWrap);
        var topDivider = CreateDivider(DockStyle.Bottom);

        var body = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.RoleShell };

        _folderColumn = new Panel
        {
            Dock = DockStyle.Left,
            Width = 210,
            MinimumSize = new Size(210, 0),
            MaximumSize = new Size(210, 0),
            Tag = AppTheme.RoleShell,
            Padding = new Padding(12, 16, 12, 12)
        };
        var folderHeader = new Label
        {
            Text = "FOLDERS",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = AppTheme.SubText,
            Dock = DockStyle.Top,
            Height = 24
        };
        _folderList.Dock = DockStyle.Fill;
        _folderList.BorderStyle = BorderStyle.None;
        _folderList.BackColor = AppTheme.Shell;
        _folderList.Font = new Font("Segoe UI", 10f);
        _folderList.DrawMode = DrawMode.OwnerDrawFixed;
        _folderList.ItemHeight = 34;
        _folderList.IntegralHeight = false;
        _folderList.Tag = AppTheme.NoTheme;
        _folderList.DrawItem += FolderList_DrawItem;
        _folderList.SelectedIndexChanged += (_, _) => RefreshEntryList();
        _folderColumn.Controls.Add(_folderList);
        _folderColumn.Controls.Add(folderHeader);
        _folderDivider = CreateDivider(DockStyle.Left);

        _recordArea = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.RoleShell, Padding = new Padding(20, 14, 20, 0) };

        var listHeader = new Panel { Dock = DockStyle.Top, Height = 30, Tag = AppTheme.RoleShell };
        _resultCountLabel.ForeColor = AppTheme.SubText;
        _resultCountLabel.AutoSize = true;
        _resultCountLabel.Location = new Point(0, 5);

        var sortLabel = new Label { Text = "Sort by:", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, 5) };
        _sortCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _sortCombo.Items.AddRange(new object[] { "Name", "Date Modified" });
        _sortCombo.SelectedIndex = 0;
        _sortCombo.Width = 140;
        _sortCombo.SelectedIndexChanged += (_, _) =>
        {
            _sortByDateModified = _sortCombo.SelectedIndex == 1;
            RefreshEntryList();
        };
        void LayoutListHeader()
        {
            sortLabel.Location = new Point(listHeader.Width - _sortCombo.Width - sortLabel.Width - 8, 5);
            _sortCombo.Location = new Point(listHeader.Width - _sortCombo.Width, 2);
        }
        listHeader.Resize += (_, _) => LayoutListHeader();
        listHeader.Controls.Add(_resultCountLabel);
        listHeader.Controls.Add(sortLabel);
        listHeader.Controls.Add(_sortCombo);

        _entryListBox.Dock = DockStyle.Fill;
        _entryListBox.BorderStyle = BorderStyle.None;
        _entryListBox.BackColor = AppTheme.Shell;
        _entryListBox.DrawMode = DrawMode.OwnerDrawFixed;
        _entryListBox.ItemHeight = 58;
        _entryListBox.IntegralHeight = false;
        _entryListBox.Tag = AppTheme.NoTheme;
        _entryListBox.Font = new Font("Segoe UI", 10f);
        _entryListBox.DrawItem += EntryListBox_DrawItem;
        _entryListBox.SelectedIndexChanged += (_, _) => OnEntrySelected();
        _entryListBox.MouseDoubleClick += (_, _) => OnEditSelected();
        _recordArea.Controls.Add(_entryListBox);
        _recordArea.Controls.Add(listHeader);
        _detailDivider = CreateDivider(DockStyle.Right);

        _detailPanel.Dock = DockStyle.Right;
        _detailPanel.Width = 440;
        _detailPanel.MinimumSize = new Size(440, 0);
        _detailPanel.MaximumSize = new Size(440, 0);
        _detailPanel.Padding = new Padding(20, 20, 20, 20);
        _detailPanel.Controls.Add(_detailContent);
        _detailContent.Resize += (_, _) => RenderDetail(_selectedEntry);

        _auditPanel = new SecurityAuditPanel(_data, _masterPassword, () => SwitchView(MainView.Vault)) { Dock = DockStyle.Fill, Visible = false };
        _identityPanel = new IdentityPaymentsPanel(_data, SaveAsync) { Dock = DockStyle.Fill, Visible = false };

        body.Controls.Add(_recordArea);
        body.Controls.Add(_detailDivider);
        body.Controls.Add(_detailPanel);
        body.Controls.Add(_folderDivider);
        body.Controls.Add(_folderColumn);
        body.Controls.Add(_auditPanel);
        body.Controls.Add(_identityPanel);

        content.Controls.Add(body);
        content.Controls.Add(topDivider);
        content.Controls.Add(topBar);

        Controls.Add(content);
        Controls.Add(railDivider);
        Controls.Add(rail);
    }

    private static Panel CreateDivider(DockStyle dock) =>
        new()
        {
            Dock = dock,
            Width = 1,
            Height = 1,
            MinimumSize = new Size(1, 1),
            MaximumSize = new Size(1, 0),
            Tag = AppTheme.NoTheme,
            BackColor = AppTheme.Border
        };

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int AddNavRow(Panel parent, IconKind icon, string label, Func<bool> isActive, int y, Action? onClick)
    {
        var row = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(parent.Width, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Tag = AppTheme.NoTheme,
            Cursor = onClick is not null ? Cursors.Hand : Cursors.Default
        };
        row.Paint += (_, e) =>
        {
            var active = isActive();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(AppTheme.RailBg);
            e.Graphics.FillRectangle(bg, row.ClientRectangle);

            var pillRect = new Rectangle(0, 1, row.Width, row.Height - 2);
            var fg = active ? AppTheme.RailTextActive : onClick is not null ? AppTheme.RailTextInactive : Color.FromArgb(110, 130, 160);

            if (active)
            {
                using var pillBrush = new SolidBrush(AppTheme.RailSelection);
                using var path = RoundedRect(pillRect, 8);
                e.Graphics.FillPath(pillBrush, path);
            }

            var iconX = _railExpanded ? pillRect.X + 10 : pillRect.X + (pillRect.Width - 18) / 2;
            var iconBounds = new Rectangle(iconX, pillRect.Y + (pillRect.Height - 18) / 2, 18, 18);
            Icons.Draw(e.Graphics, icon, iconBounds, fg, 1.6f, active ? AppTheme.RailSelection : AppTheme.RailBg);

            if (_railExpanded)
            {
                var textRect = new Rectangle(iconBounds.Right + 10, pillRect.Y, pillRect.Width - iconBounds.Right - 10, pillRect.Height);
                var font = active ? new Font(Font, FontStyle.Bold) : Font;
                TextRenderer.DrawText(e.Graphics, label, font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
        };
        if (onClick is not null) row.Click += (_, _) => onClick();
        AppTheme.ThemeChanged += row.Invalidate;

        parent.Controls.Add(row);
        return y + 38;
    }

    private void FolderList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var bg = new SolidBrush(AppTheme.Shell);
        e.Graphics.FillRectangle(bg, e.Bounds);

        var pillRect = new Rectangle(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width, e.Bounds.Height - 4);
        var iconColor = selected ? AppTheme.Accent : AppTheme.SubText;

        if (selected)
        {
            using var pillBrush = new SolidBrush(AppTheme.Selection);
            using var path = RoundedRect(pillRect, 8);
            e.Graphics.FillPath(pillBrush, path);
        }

        var iconBounds = new Rectangle(pillRect.X + 10, pillRect.Y + (pillRect.Height - 18) / 2, 18, 18);
        Icons.Draw(e.Graphics, IconKind.Folder, iconBounds, iconColor, 1.4f, selected ? AppTheme.Selection : AppTheme.Shell);

        var text = _folderList.Items[e.Index].ToString() ?? string.Empty;
        var textColor = selected ? AppTheme.Text : AppTheme.SubText;
        var font = selected ? new Font(_folderList.Font, FontStyle.Bold) : _folderList.Font;
        var textRect = new Rectangle(iconBounds.Right + 10, pillRect.Y, pillRect.Width - iconBounds.Right, pillRect.Height);
        TextRenderer.DrawText(e.Graphics, text, font, textRect, textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    private void EntryListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var entry = (VaultEntry)_entryListBox.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var back = selected ? AppTheme.Selection : AppTheme.Shell;
        using (var bg = new SolidBrush(back))
            g.FillRectangle(bg, e.Bounds);

        if (selected)
        {
            using var accentPen = new Pen(AppTheme.Accent, 3);
            g.DrawLine(accentPen, e.Bounds.Left + 1, e.Bounds.Top, e.Bounds.Left + 1, e.Bounds.Bottom);
        }

        var avatarRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + (e.Bounds.Height - 36) / 2, 36, 36);
        using (var avatarBrush = new SolidBrush(AppTheme.Accent))
            g.FillEllipse(avatarBrush, avatarRect);
        var initial = entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?";
        TextRenderer.DrawText(g, initial, new Font("Segoe UI", 12, FontStyle.Bold), avatarRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var textLeft = avatarRect.Right + 14;
        var nameRect = new Rectangle(textLeft, e.Bounds.Top + 8, e.Bounds.Width - textLeft - 12, 22);
        var subRect = new Rectangle(textLeft, e.Bounds.Top + 30, e.Bounds.Width - textLeft - 12, 20);

        var subtitle = _currentView == MainView.Deleted && entry.DeletedAt is not null
            ? $"Deleted {entry.DeletedAt.Value.LocalDateTime:g}"
            : entry.Username;

        TextRenderer.DrawText(g, entry.Name, new Font(_entryListBox.Font, FontStyle.Bold), nameRect, AppTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, subtitle, _entryListBox.Font, subRect, AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var linePen = new Pen(AppTheme.Border);
        g.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void RefreshFolderList()
    {
        var selected = _folderList.SelectedItem as string;

        var folders = new List<string> { AllItemsFolder };
        folders.AddRange(_data.Entries
            .Where(e => e.DeletedAt is null)
            .Select(e => e.Folder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));

        _folderList.DataSource = folders;

        var restoreIndex = selected is not null ? folders.IndexOf(selected) : 0;
        _folderList.SelectedIndex = restoreIndex >= 0 ? restoreIndex : 0;
    }

    private void RefreshEntryList()
    {
        if (_currentView == MainView.Deleted)
        {
            var deleted = _data.Entries
                .Where(e => e.DeletedAt is not null)
                .OrderByDescending(e => e.DeletedAt)
                .ToList();

            _entryListBox.BeginUpdate();
            _entryListBox.Items.Clear();
            foreach (var entry in deleted)
                _entryListBox.Items.Add(entry);
            _entryListBox.EndUpdate();

            _resultCountLabel.Text = deleted.Count == 1 ? "1 deleted item" : $"{deleted.Count} deleted items";
            RenderDetail(null);
            return;
        }

        var term = _searchBox.Text.Trim();
        var folder = _folderList.SelectedItem as string ?? AllItemsFolder;

        IEnumerable<VaultEntry> entries = _data.Entries.Where(e => e.DeletedAt is null);

        if (!string.IsNullOrEmpty(term))
        {
            entries = entries.Where(e =>
                e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.Folder.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.Notes.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        else if (!folder.Equals(AllItemsFolder, StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(e => e.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
        }

        var list = _sortByDateModified
            ? entries.OrderByDescending(e => e.ModifiedAt).ToList()
            : entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _entryListBox.BeginUpdate();
        _entryListBox.Items.Clear();
        foreach (var entry in list)
            _entryListBox.Items.Add(entry);
        _entryListBox.EndUpdate();

        _resultCountLabel.Text = list.Count == 1 ? "1 record" : $"{list.Count} records";

        RenderDetail(null);
    }

    private void OnEntrySelected()
    {
        var entry = _entryListBox.SelectedItem as VaultEntry;
        RenderDetail(entry);
    }

    private void RenderDetail(VaultEntry? entry)
    {
        _selectedEntry = entry;
        _totpCodeLabel = null;

        if (entry is null)
        {
            _detailPanel.Visible = false;
            _detailDivider.Visible = false;
            _detailContent.Controls.Clear();
            return;
        }

        _detailPanel.Visible = true;
        _detailDivider.Visible = true;
        _detailContent.Controls.Clear();
        _detailPanel.BackColor = AppTheme.CardBg;
        _detailContent.BackColor = AppTheme.CardBg;

        var y = 0;
        var width = _detailContent.Width;

        // Toolbar: edit / duplicate / delete / close
        var toolbar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Location = new Point(0, y),
            Size = new Size(width, 30),
            Tag = AppTheme.RoleCard
        };
        var closeBtn = new IconButton(IconKind.Close, 28) { IconColor = AppTheme.SubText };
        closeBtn.Click += (_, _) => RenderDetail(null);
        toolbar.Controls.Add(closeBtn);

        if (_currentView == MainView.Deleted)
        {
            var purgeBtn = new IconButton(IconKind.Trash, 28) { IconColor = AppTheme.Danger };
            purgeBtn.Click += (_, _) => OnPurgeSelected();
            var restoreBtn = new Button
            {
                Text = "Restore",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = AppTheme.Accent,
                BackColor = AppTheme.CardBg,
                Font = new Font(Font, FontStyle.Bold),
                Tag = AppTheme.NoTheme,
                Cursor = Cursors.Hand
            };
            restoreBtn.FlatAppearance.BorderSize = 0;
            restoreBtn.Click += (_, _) => OnRestoreSelected();
            toolbar.Controls.Add(purgeBtn);
            toolbar.Controls.Add(restoreBtn);
        }
        else
        {
            var deleteBtn = new IconButton(IconKind.Trash, 28) { IconColor = AppTheme.Danger };
            deleteBtn.Click += (_, _) => OnDeleteSelected();
            var editBtn = new IconButton(IconKind.Edit, 28) { IconColor = AppTheme.SubText };
            editBtn.Click += (_, _) => OnEditSelected();
            toolbar.Controls.Add(deleteBtn);
            toolbar.Controls.Add(editBtn);
        }

        _detailContent.Controls.Add(toolbar);
        y += 40;

        var avatar = new Panel { Size = new Size(44, 44), Location = new Point(0, y) };
        avatar.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(AppTheme.Accent);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(brush, 0, 0, 43, 43);
            var initial = entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?";
            TextRenderer.DrawText(e.Graphics, initial, new Font("Segoe UI", 16, FontStyle.Bold),
                new Rectangle(0, 0, 44, 44), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        _detailContent.Controls.Add(avatar);

        var title = new Label
        {
            Text = entry.Name,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(56, y + 2)
        };
        var folderLabel = new Label
        {
            Text = entry.Folder,
            Font = new Font("Segoe UI", 9),
            ForeColor = AppTheme.SubText,
            AutoSize = true,
            Location = new Point(56, y + 24)
        };
        _detailContent.Controls.Add(title);
        _detailContent.Controls.Add(folderLabel);
        y += 66;

        y = AddDivider(width, y);
        y = AddReadField("Username", entry.Username, width, y, copyable: true);
        y = AddSecretField("Password", entry.Secret, width, y);

        if (!string.IsNullOrEmpty(entry.Notes))
            y = AddReadField("Notes", entry.Notes, width, y, copyable: false);

        if (entry.CustomFields.Count > 0)
        {
            y = AddDivider(width, y);
            foreach (var field in entry.CustomFields)
            {
                y = field.Type switch
                {
                    CustomFieldType.Password => AddSecretField(field.Label, field.Value, width, y),
                    _ => AddReadField(field.Label, field.Value, width, y, copyable: true)
                };
            }
        }

        if (entry.TotpSecret is not null)
        {
            y = AddDivider(width, y);
            y = AddTotpField(entry.TotpSecret, width, y);
        }

        y = AddDivider(width, y);
        y = AddReadField("Created", entry.CreatedAt.LocalDateTime.ToString("g"), width, y, copyable: false);
        AddReadField("Modified", entry.ModifiedAt.LocalDateTime.ToString("g"), width, y, copyable: false);

        AppTheme.Apply(_detailPanel);
    }

    private int AddDivider(int width, int y)
    {
        y += 6;
        var line = new Panel { Location = new Point(0, y), Size = new Size(width, 1), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        _detailContent.Controls.Add(line);
        return y + 18;
    }

    private int AddReadField(string label, string value, int width, int y, bool copyable)
    {
        var lbl = new Label { Text = label, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        _detailContent.Controls.Add(lbl);
        y += 20;

        if (!copyable)
        {
            var valueLabel = new Label
            {
                Text = value,
                ForeColor = AppTheme.Text,
                AutoSize = false,
                Location = new Point(0, y),
                Size = new Size(width, 26)
            };
            _detailContent.Controls.Add(valueLabel);
            return y + 30;
        }

        var box = new TextBox
        {
            Text = value,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = AppTheme.CardBg,
            ForeColor = AppTheme.Text,
            Tag = AppTheme.NoTheme,
            Location = new Point(0, y),
            Size = new Size(width - 40, 22)
        };
        _detailContent.Controls.Add(box);

        var copyButton = new IconButton(IconKind.Copy, 28) { Location = new Point(width - 30, y - 3), Circular = false };
        copyButton.Click += (_, _) => CopyToClipboard(value);
        _detailContent.Controls.Add(copyButton);

        return y + 36;
    }

    private int AddSecretField(string label, string value, int width, int y)
    {
        var lbl = new Label { Text = label, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        _detailContent.Controls.Add(lbl);
        y += 20;

        var box = new TextBox
        {
            Text = value,
            ReadOnly = true,
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.None,
            BackColor = AppTheme.CardBg,
            ForeColor = AppTheme.Text,
            Tag = AppTheme.NoTheme,
            Location = new Point(0, y),
            Size = new Size(width - 70, 22)
        };
        _detailContent.Controls.Add(box);

        var showButton = new IconButton(IconKind.Eye, 28) { Location = new Point(width - 66, y - 3), Circular = false };
        showButton.Click += (_, _) =>
        {
            box.UseSystemPasswordChar = !box.UseSystemPasswordChar;
            showButton.Icon = box.UseSystemPasswordChar ? IconKind.Eye : IconKind.EyeOff;
            showButton.Invalidate();
        };
        _detailContent.Controls.Add(showButton);

        var copyButton = new IconButton(IconKind.Copy, 28) { Location = new Point(width - 32, y - 3), Circular = false };
        copyButton.Click += (_, _) => CopyToClipboard(value);
        _detailContent.Controls.Add(copyButton);

        return y + 36;
    }

    private int AddTotpField(string totpSecret, int width, int y)
    {
        var lbl = new Label { Text = "TOTP Code", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        _detailContent.Controls.Add(lbl);
        y += 20;

        var code = TotpGenerator.GenerateCode(totpSecret);
        var remaining = TotpGenerator.SecondsRemaining();

        _totpCodeLabel = new Label
        {
            Text = $"{code}   ({remaining}s)",
            Font = new Font("Consolas", 14, FontStyle.Bold),
            ForeColor = AppTheme.Accent,
            AutoSize = true,
            Location = new Point(0, y)
        };
        _detailContent.Controls.Add(_totpCodeLabel);

        var copyButton = new IconButton(IconKind.Copy, 28) { Location = new Point(width - 32, y - 4), Circular = false };
        copyButton.Click += (_, _) => CopyToClipboard(TotpGenerator.GenerateCode(totpSecret));
        _detailContent.Controls.Add(copyButton);

        return y + 40;
    }

    private void UpdateTotpLabel()
    {
        if (_selectedEntry?.TotpSecret is null || _totpCodeLabel is null) return;
        var code = TotpGenerator.GenerateCode(_selectedEntry.TotpSecret);
        var remaining = TotpGenerator.SecondsRemaining();
        _totpCodeLabel.Text = $"{code}   ({remaining}s)";
    }

    private void CopyToClipboard(string value)
    {
        Clipboard.SetText(value);
        _pendingClipboardValue = value;
        _clipboardClearTimer.Stop();
        _clipboardClearTimer.Start();
    }

    private void ClearClipboardIfPending()
    {
        _clipboardClearTimer.Stop();
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _pendingClipboardValue)
                Clipboard.Clear();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard was locked by another process; nothing to do.
        }
        _pendingClipboardValue = null;
    }

    private async void OnCreateNew()
    {
        using var editForm = new EntryEditForm(_data.Entries.Select(e => e.Folder));
        if (editForm.ShowDialog(this) != DialogResult.OK || editForm.Result is null) return;

        _data.Entries.Add(editForm.Result);
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async void OnEditSelected()
    {
        if (_selectedEntry is null) return;

        using var editForm = new EntryEditForm(_data.Entries.Select(e => e.Folder), _selectedEntry);
        if (editForm.ShowDialog(this) != DialogResult.OK || editForm.Result is null) return;

        var index = _data.Entries.IndexOf(_selectedEntry);
        _data.Entries[index] = editForm.Result;
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async void OnDeleteSelected()
    {
        if (_selectedEntry is null) return;

        var confirm = MessageBox.Show(this, $"Move '{_selectedEntry.Name}' to Deleted Items?", "Confirm Delete",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _selectedEntry.DeletedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async void OnRestoreSelected()
    {
        if (_selectedEntry is null) return;

        _selectedEntry.DeletedAt = null;
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async void OnPurgeSelected()
    {
        if (_selectedEntry is null) return;

        var confirm = MessageBox.Show(this, $"Permanently delete '{_selectedEntry.Name}'? This cannot be undone.",
            "Confirm Permanent Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _data.Entries.Remove(_selectedEntry);
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async Task SaveAsync() => await _store.SaveAsync(_data, _masterPassword);
}
