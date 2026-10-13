using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

public class MainForm : Form
{
    private const int ClipboardClearSeconds = 5 * 60;
    private const int RailExpandedWidth = 232;
    private const int RailCollapsedWidth = 68;

    private readonly FileVaultStore _store = new();
    private readonly VaultData _data;
    private string _masterPassword;

    private readonly TextBox _searchBox = new() { PlaceholderText = "Search records, usernames, websites, tags…   (Ctrl+F)" };
    private RoundedField _searchField = null!;
    private readonly ThemeToggleButton _themeToggle = new();
    private readonly NavListView _folderList = new();
    private readonly NavListView _entryListBox = new() { ShowScrollbar = true };
    private readonly Label _pageTitle = new();
    private readonly Label _resultCountLabel = new();
    private readonly Label _emptyListLabel = new();
    private PillButton _sortButton = null!;
    private PillButton _newRecordButton = null!;
    private bool _sortByDateModified;
    private readonly Panel _detailPanel = new() { Tag = AppTheme.NoTheme };
    private readonly Panel _detailCard = new() { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme, Padding = new Padding(24, 20, 14, 16) };
    private readonly Panel _detailContent = new() { Dock = DockStyle.Fill, AutoScroll = true, Tag = AppTheme.NoTheme };
    private readonly List<Control> _canvasPanels = new();
    private Panel _topBar = null!;
    private Panel _recoveryBanner = null!;
    private bool _recoveryKeyMissing;
    private Panel _folderColumn = null!;
    private PanelSplitter _folderSplitter = null!;
    private Panel _recordArea = null!;
    private Panel _toast = null!;
    private SecurityAuditPanel _auditPanel = null!;
    private IdentityPaymentsPanel _identityPanel = null!;
    private BreachWatchPanel _breachPanel = null!;
    private SettingsPanel _settingsPanel = null!;
    private Panel _navPanel = null!;
    private Panel _lockHost = null!;

    private readonly Font _listTitleFont = new("Segoe UI Semibold", 10f);
    private readonly Font _listSubFont = new("Segoe UI", 9f);
    private readonly Font _folderFont = new("Segoe UI", 9.75f);
    private readonly Font _folderFontBold = new("Segoe UI Semibold", 9.75f);
    private readonly Font _chipFont = new("Segoe UI Semibold", 8.25f);
    private readonly Font _sectionFont = new("Segoe UI", 8f, FontStyle.Bold);

    private readonly System.Windows.Forms.Timer _totpTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _clipboardClearTimer = new() { Interval = ClipboardClearSeconds * 1000 };
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2200 };
    private readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 10_000 };
    private string? _pendingClipboardValue;
    private string _toastText = string.Empty;

    private VaultEntry? _selectedEntry;
    private Label? _totpCodeLabel;
    private Panel? _totpRing;
    private Panel? _detailAvatar;
    private int _renderedDetailWidth = -1;
    private bool _railExpanded = true;
    private int _lastFolderIndex;
    private bool _suppressFolderChange;
    private bool _autoExpandedOnSelect;
    private MainView _currentView = MainView.Vault;

    private enum MainView { Vault, Deleted, SecurityAudit, IdentityPayments, BreachWatch, Settings }

    private enum NavKind { All, Favorites, Folder, Header, RecordType, Tag }

    // An item in the left "folders" list: smart lists, folders, a TAGS header and tags.
    // Folders form a tree on "\" (or "/"): Name is the short (leaf) name, Path the full folder path.
    private sealed record NavItem(NavKind Kind, string Name, int Count, string Path = "", int Depth = 0, bool HasChildren = false)
    {
        public override string ToString() => Kind == NavKind.Folder ? Path : Name;
    }

    private static readonly char[] FolderSeparators = { '\\', '/' };
    private const int TreeIndent = 16;
    private const int TreeChevronZone = 20;

    // Folder rules live in SecretVault.Core (VaultOperations) so the Android app behaves the same.
    private static string NormalizeFolder(string folder) => VaultOperations.NormalizeFolder(folder);

    // "Infrastructure\Servers" -> "Servers" (the full path is shown in the details panel).
    private static string LeafFolderName(string folder) => VaultOperations.LeafFolderName(folder);

    private static bool IsInFolder(VaultEntry entry, string path) => VaultOperations.IsInFolder(entry, path);

    private static IconKind RecordTypeIcon(string type) => type switch
    {
        RecordTypes.Server => IconKind.Server,
        RecordTypes.Database => IconKind.Database,
        RecordTypes.AppCredentials => IconKind.Apps,
        RecordTypes.Certificate => IconKind.Certificate,
        RecordTypes.SoftwareLicense => IconKind.Tag,
        RecordTypes.SecureNote => IconKind.Note,
        _ => IconKind.Key
    };

    private static Color RecordTypeColor(string type) => type switch
    {
        RecordTypes.Server => Color.FromArgb(13, 148, 136),      // teal
        RecordTypes.Database => Color.FromArgb(147, 51, 234),    // purple
        RecordTypes.AppCredentials => Color.FromArgb(79, 70, 229), // indigo
        RecordTypes.Certificate => Color.FromArgb(217, 119, 6),  // amber
        RecordTypes.SoftwareLicense => Color.FromArgb(22, 163, 74), // green
        RecordTypes.SecureNote => Color.FromArgb(71, 85, 105),   // slate
        _ => Color.FromArgb(37, 99, 235)                          // blue (Login)
    };

    // Small coloured rounded tile with a white glyph, used for record types in the list and the details card.
    private static void DrawRecordTypeTile(Graphics g, Rectangle bounds, string type)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = RecordTypeColor(type);
        using (var path = RoundedField.RoundRect(bounds, bounds.Width * 0.28f))
        using (var brush = new SolidBrush(color))
            g.FillPath(brush, path);
        var inset = (int)Math.Round(bounds.Width * 0.24f);
        Icons.Draw(g, RecordTypeIcon(type), Rectangle.Inflate(bounds, -inset, -inset), Color.White, Math.Max(1.3f, bounds.Width / 14f), color);
    }

    private static bool IsSectionCollapsed(string section) =>
        AppSettings.Current.CollapsedSections.Contains(section, StringComparer.OrdinalIgnoreCase);

    private void ToggleSection(string section)
    {
        var list = AppSettings.Current.CollapsedSections;
        if (list.RemoveAll(s => s.Equals(section, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(section);
        AppSettings.Current.Save();
        RefreshFolderList();
    }

    private static bool IsExpanded(string path) =>
        AppSettings.Current.ExpandedFolders.Contains(path, StringComparer.OrdinalIgnoreCase);

    private void SetExpanded(string path, bool expanded)
    {
        var list = AppSettings.Current.ExpandedFolders;
        list.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (expanded) list.Add(path);
        AppSettings.Current.Save();
        RefreshFolderList();
    }

    // Set when the window closes because the vault was locked (Program shows the unlock screen again).
    public bool LockRequested { get; private set; }

    public MainForm(VaultData data, string masterPassword)
    {
        _data = data;
        _masterPassword = masterPassword;

        Text = "SecretVault";
        Icon = AppIcon.Value;
        Size = new Size(1300, 820);
        MinimumSize = new Size(1060, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;

        BuildLayout();
        AppTheme.Apply(this);
        ApplyChromeColors();

        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);
        AppTheme.ThemeChanged += OnThemeChanged;
        FaviconCache.IconLoaded += OnFaviconLoaded;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        FormClosed += (_, _) =>
        {
            AppTheme.ThemeChanged -= OnThemeChanged;
            FaviconCache.IconLoaded -= OnFaviconLoaded;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            _idleTimer.Stop();
            _totpTimer.Stop();
            ClearClipboardIfPending();
        };

        _totpTimer.Tick += (_, _) => UpdateTotpLabel();
        _totpTimer.Start();
        _clipboardClearTimer.Tick += (_, _) => ClearClipboardIfPending();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.Visible = false; };
        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();

        RefreshFolderList();
        RefreshEntryList();
        _ = RefreshRecoveryBannerAsync();

        // Sync with the shared copy (phone etc.): on open, shortly after each change, and every few minutes.
        _syncDebounce.Tick += async (_, _) => { _syncDebounce.Stop(); await SyncNowAsync(interactive: false); };
        _syncTimer.Tick += async (_, _) => await SyncNowAsync(interactive: false);
        _syncTimer.Start();
        Shown += async (_, _) => await SyncNowAsync(interactive: false);
        FormClosed += (_, _) => { _syncTimer.Stop(); _syncDebounce.Stop(); };
    }

    // ---------- sync with other devices ----------

    private DesktopSync? _syncService;
    private DesktopSync Sync => _syncService ??= new DesktopSync(_store, _data, () => _masterPassword);
    private readonly System.Windows.Forms.Timer _syncDebounce = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _syncTimer = new() { Interval = 5 * 60 * 1000 };

    public async Task SyncNowAsync(bool interactive)
    {
        if (!DesktopSync.Enabled) return;
        // Never merge underneath an open editor: its result would be applied to a replaced record.
        if (!interactive && Application.OpenForms.Count > 1) { _syncDebounce.Start(); return; }
        try
        {
            var result = await Sync.SyncAsync(this, interactive);
            if (result is null) return;
            if (result.ChangedLocal)
            {
                var selected = _selectedEntry?.Id;
                RefreshFolderList();
                RefreshEntryList();
                if (selected is not null && _data.Entries.FirstOrDefault(e => e.Id == selected) is { } again) SelectEntry(again);
            }
            if (interactive || result.ChangedLocal) ShowToast("Synced · " + result.Summary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or VaultAuthenticationException)
        {
            if (interactive) AppDialog.Show(this, "Sync failed", AppSettings.Current.LastSyncError ?? ex.Message, AppDialog.Kind.Warning);
        }
        _settingsPanel?.RefreshSyncStatus();
    }

    // ---------- recovery key reminder ----------

    private async Task RefreshRecoveryBannerAsync()
    {
        _recoveryKeyMissing = !await _store.HasRecoveryKeyAsync();
        UpdateRecoveryBannerVisibility();
    }

    private void UpdateRecoveryBannerVisibility()
    {
        if (_recoveryBanner.IsDisposed) return;
        var snoozed = AppSettings.Current.RecoveryReminderSnoozedUntilUtc is { } until && until > DateTime.UtcNow;
        _recoveryBanner.Visible = _recoveryKeyMissing && !snoozed && _currentView is MainView.Vault or MainView.Deleted;
    }

    private async Task CreateRecoveryKeyFromBannerAsync()
    {
        var key = await _store.CreateRecoveryKeyAsync(_data, _masterPassword);
        using (var form = new RecoveryKeyForm(key)) form.ShowDialog(this);
        ShowToast("Recovery key created");
        await RefreshRecoveryBannerAsync();
    }

    private Panel BuildRecoveryBanner()
    {
        var amber = Color.FromArgb(217, 119, 6);
        var banner = CanvasPanel(new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(24, 0, 20, 12), Visible = false });
        var cardBack = UiKit.Tint(amber, AppTheme.Canvas);
        var card = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme, BackColor = cardBack };
        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.Canvas);
            using (var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, card.Width - 1.5f, card.Height - 1.5f), 12))
            using (var fill = new SolidBrush(card.BackColor))
            using (var pen = new Pen(UiKit.Blend(amber, card.BackColor, 0.35f)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            // Amber key tile.
            using (var tile = RoundedField.RoundRect(new RectangleF(16, (card.Height - 36) / 2f, 36, 36), 10))
            using (var tileBrush = new SolidBrush(amber))
                g.FillPath(tileBrush, tile);
            Icons.Draw(g, IconKind.Key, new Rectangle(25, (card.Height - 18) / 2, 18, 18), Color.White, 1.8f, amber);
        };
        card.Resize += (_, _) => card.Invalidate();

        var title = new Label { Text = "Set up a recovery key", Font = new Font("Segoe UI Semibold", 10f), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(66, 10), Tag = AppTheme.NoTheme };
        var text = new Label { Text = "If you forget your master password, a recovery key is the only way to reset it.", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(66, 31), Tag = AppTheme.NoTheme };
        var create = new PillButton("Create recovery key", primary: true, new Size(176, 34)) { LeadingIcon = IconKind.Key };
        var later = new PillButton("Remind me later", primary: false, new Size(140, 34));
        create.Click += async (_, _) => await CreateRecoveryKeyFromBannerAsync();
        later.Click += (_, _) =>
        {
            AppSettings.Current.RecoveryReminderSnoozedUntilUtc = DateTime.UtcNow.AddDays(7);
            AppSettings.Current.Save();
            UpdateRecoveryBannerVisibility();
            ShowToast("We'll remind you again in 7 days");
        };
        card.Resize += (_, _) =>
        {
            create.Location = new Point(card.Width - 14 - create.Width, (card.Height - create.Height) / 2);
            later.Location = new Point(create.Left - 8 - later.Width, create.Top);
            // Hide the explanation line rather than let it run under the buttons on narrow windows.
            text.Visible = text.Right < later.Left - 8;
        };
        card.Controls.AddRange(new Control[] { title, text, create, later });
        banner.Controls.Add(card);

        AppTheme.ThemeChanged += () =>
        {
            if (card.IsDisposed) return; // a previous window (before a lock/unlock) — nothing to update
            card.BackColor = UiKit.Tint(amber, AppTheme.Canvas);
            title.ForeColor = AppTheme.Text;
            text.ForeColor = AppTheme.SubText;
            card.Invalidate(true);
        };
        return banner;
    }

    private void OnThemeChanged()
    {
        DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);
        AppTheme.Apply(this);
        ApplyChromeColors();
        _navPanel.Invalidate(true);
        _lockHost.Invalidate(true);
        RenderDetail(_selectedEntry);

        switch (_currentView)
        {
            case MainView.SecurityAudit: _auditPanel.RefreshData(); break;
            case MainView.IdentityPayments: _identityPanel.RefreshView(); break;
            case MainView.BreachWatch: _breachPanel.RefreshView(); break;
            case MainView.Settings: _settingsPanel.RefreshView(); break;
        }
    }

    private void OnFaviconLoaded()
    {
        _entryListBox.Invalidate();
        _detailAvatar?.Invalidate();
    }

    // Colours for the long-lived chrome that AppTheme.Apply deliberately skips (NoTheme-tagged).
    private void ApplyChromeColors()
    {
        BackColor = AppTheme.Canvas;
        foreach (var panel in _canvasPanels) panel.BackColor = AppTheme.Canvas;
        _folderList.BackColor = AppTheme.Canvas;
        _folderSplitter.BackColor = AppTheme.Canvas;
        _entryListBox.BackColor = AppTheme.Canvas;
        _detailCard.BackColor = AppTheme.CardBg;
        _detailContent.BackColor = AppTheme.CardBg;
        _navPanel.BackColor = AppTheme.Sidebar;
        _pageTitle.ForeColor = AppTheme.Text;
        _resultCountLabel.ForeColor = AppTheme.SubText;
        _emptyListLabel.ForeColor = AppTheme.SubText;
        _searchField.ApplyTheme();
        _folderList.Invalidate();
        _entryListBox.Invalidate();
        _detailPanel.Invalidate(true);
        _sortButton.Invalidate();
    }

    private void SwitchView(MainView view)
    {
        _currentView = view;
        _searchBox.Text = string.Empty;

        var showVaultLike = view is MainView.Vault or MainView.Deleted;
        _folderColumn.Visible = _folderSplitter.Visible = view == MainView.Vault;
        _recordArea.Visible = showVaultLike;
        _detailPanel.Visible = showVaultLike && _selectedEntry is not null;
        _auditPanel.Visible = view == MainView.SecurityAudit;
        _identityPanel.Visible = view == MainView.IdentityPayments;
        _breachPanel.Visible = view == MainView.BreachWatch;
        _settingsPanel.Visible = view == MainView.Settings;
        _topBar.Visible = showVaultLike;
        UpdateRecoveryBannerVisibility();

        switch (view)
        {
            case MainView.SecurityAudit: _auditPanel.RefreshData(); break;
            case MainView.IdentityPayments: _identityPanel.RefreshView(); break;
            case MainView.BreachWatch: _breachPanel.RefreshView(); break;
            case MainView.Settings: _settingsPanel.RefreshView(); break;
            default:
                RefreshFolderList();
                RefreshEntryList();
                break;
        }

        _navPanel.Invalidate(true);
    }

    // ---------- locking ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private void CheckIdle()
    {
        var minutes = AppSettings.Current.AutoLockMinutes;
        if (minutes == AppSettings.NeverLock) return;

        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return;
        var idleMs = unchecked((uint)Environment.TickCount - info.Time);
        if (idleMs >= minutes * 60_000u) Lock();
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (AppSettings.Current.LockOnWindowsLock && e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect)
            BeginInvoke(Lock);
    }

    public void Lock()
    {
        if (LockRequested || IsDisposed) return;
        LockRequested = true;
        ClearClipboardIfPending();

        // Close any open dialogs first (their ShowDialog calls return Cancel), then this window.
        foreach (var form in Application.OpenForms.Cast<Form>().Where(f => f != this).ToList())
            form.Close();
        BeginInvoke(Close);
    }

    private async Task ChangeMasterPasswordAsync(string newPassword)
    {
        // Re-wraps the vault key with the new password (backs up first); an existing recovery key keeps working.
        await _store.ChangeMasterPasswordAsync(_data, _masterPassword, newPassword);
        _masterPassword = newPassword;

        // The Windows Hello copy holds the old password: re-protect the new one, or turn Hello off.
        if (WindowsHello.IsEnabled && await WindowsHello.EnableAsync(newPassword) != WindowsHello.Outcome.Success)
        {
            await WindowsHello.DisableAsync();
            ShowToast("Windows Hello was turned off — re-enable it in Settings");
        }

        if (WindowsPasswordUnlock.IsEnabled && await WindowsPasswordUnlock.EnableAsync(this, newPassword) != WindowsPasswordUnlock.Outcome.Success)
        {
            WindowsPasswordUnlock.Disable();
            ShowToast("Windows password unlock was turned off — re-enable it in Settings");
        }
    }

    // ---------- keyboard shortcuts ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var typing = ActiveControl is TextBoxBase || FindFocusedTextBox() is not null;
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                if (_currentView is not (MainView.Vault or MainView.Deleted)) SwitchView(MainView.Vault);
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;
            case Keys.Control | Keys.N:
                OnCreateNew();
                return true;
            case Keys.Control | Keys.L:
                Lock();
                return true;
            case Keys.Control | Keys.E:
                OnEditSelected();
                return true;
            case Keys.Control | Keys.C when !typing && _selectedEntry is { Secret.Length: > 0 } entry:
                CopyToClipboard(entry.Secret, "Password");
                return true;
            case Keys.Control | Keys.B when !typing && _selectedEntry is { Username.Length: > 0 } entry:
                CopyToClipboard(entry.Username, "Username");
                return true;
            case Keys.Delete when !typing && _selectedEntry is not null && _currentView == MainView.Vault:
                OnDeleteSelected();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control? FindFocusedTextBox()
    {
        Control? c = ActiveControl;
        while (c is ContainerControl { ActiveControl: { } inner }) c = inner;
        return c is TextBoxBase ? c : null;
    }

    // ---------- layout ----------

    private void BuildLayout()
    {
        // --- Left nav rail ---
        var rail = new Panel { Dock = DockStyle.Left, Width = RailExpandedWidth, Tag = AppTheme.RoleSidebar };

        var hamburger = new IconButton(IconKind.Hamburger, 30) { IconColor = AppTheme.RailIconInactive };
        var brand = new Panel { Size = new Size(170, 30), Tag = AppTheme.NoTheme, BackColor = AppTheme.RailBg };
        var brandFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        brand.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.RailBg);
            if (AppIcon.Value is { } icon)
            {
                using var small = new Icon(icon, 24, 24);
                g.DrawIcon(small, new Rectangle(0, 3, 24, 24));
            }
            TextRenderer.DrawText(g, "SecretVault", brandFont, new Rectangle(32, 0, 140, 30), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        var createButton = new PillButton("Create New", primary: true, new Size(RailExpandedWidth - 32, 42)) { LeadingIcon = IconKind.Plus };
        createButton.Click += (_, _) => OnCreateNew();
        var createCircleButton = new PrimaryCircleButton(40) { Visible = false };
        createCircleButton.Click += (_, _) => OnCreateNew();
        var tips = new ToolTip();
        tips.SetToolTip(createCircleButton, "Create new record (Ctrl+N)");

        _navPanel = new Panel { Size = new Size(RailExpandedWidth - 32, 6 * 44), BackColor = AppTheme.Sidebar, Tag = AppTheme.NoTheme };
        var navY = 0;
        navY = AddNavRow(_navPanel, IconKind.Shield, "My Vault", () => _currentView == MainView.Vault, navY, () => SwitchView(MainView.Vault));
        navY = AddNavRow(_navPanel, IconKind.Card, "Identity & Payments", () => _currentView == MainView.IdentityPayments, navY, () => SwitchView(MainView.IdentityPayments));
        navY = AddNavRow(_navPanel, IconKind.Search, "Security Audit", () => _currentView == MainView.SecurityAudit, navY, () => SwitchView(MainView.SecurityAudit));
        navY = AddNavRow(_navPanel, IconKind.Eye, "BreachWatch", () => _currentView == MainView.BreachWatch, navY, () => SwitchView(MainView.BreachWatch));
        navY = AddNavRow(_navPanel, IconKind.Trash, "Deleted Items", () => _currentView == MainView.Deleted, navY, () => SwitchView(MainView.Deleted));
        AddNavRow(_navPanel, IconKind.Gear, "Settings", () => _currentView == MainView.Settings, navY, () => SwitchView(MainView.Settings));

        _lockHost = new Panel { Size = new Size(RailExpandedWidth - 32, 44), BackColor = AppTheme.Sidebar, Tag = AppTheme.NoTheme };
        AddNavRow(_lockHost, IconKind.Lock, "Lock vault", () => false, 0, Lock, badge: "Ctrl+L");

        void LayoutRailChildren()
        {
            var railWidth = _railExpanded ? RailExpandedWidth : RailCollapsedWidth;
            hamburger.Location = _railExpanded ? new Point(14, 18) : new Point((railWidth - hamburger.Width) / 2, 18);
            brand.Location = new Point(52, 18);
            brand.Visible = _railExpanded;
            createButton.Location = new Point(16, 68);
            createCircleButton.Location = new Point((railWidth - createCircleButton.Width) / 2, 68);
            createButton.Visible = _railExpanded;
            createCircleButton.Visible = !_railExpanded;
            _navPanel.Width = _lockHost.Width = _railExpanded ? RailExpandedWidth - 32 : 44;
            var x = _railExpanded ? 16 : (railWidth - _navPanel.Width) / 2;
            _navPanel.Location = new Point(x, 132);
            _lockHost.Location = new Point(x, rail.Height - _themeToggle.Height - _lockHost.Height - 4);
        }

        hamburger.Click += (_, _) =>
        {
            SuspendLayout();
            _railExpanded = !_railExpanded;
            rail.Width = _railExpanded ? RailExpandedWidth : RailCollapsedWidth;
            LayoutRailChildren();
            ResumeLayout(true);
            Invalidate(true);
        };
        rail.Resize += (_, _) => LayoutRailChildren();

        _themeToggle.Dock = DockStyle.Bottom;
        _themeToggle.Height = 56;
        rail.Controls.Add(hamburger);
        rail.Controls.Add(brand);
        rail.Controls.Add(createButton);
        rail.Controls.Add(createCircleButton);
        rail.Controls.Add(_navPanel);
        rail.Controls.Add(_lockHost);
        rail.Controls.Add(_themeToggle);
        LayoutRailChildren();

        // --- Content area ---
        var content = CanvasPanel(new Panel { Dock = DockStyle.Fill });

        var topBar = _topBar = CanvasPanel(new Panel { Dock = DockStyle.Top, Height = 76 });
        _searchField = new RoundedField(_searchBox, 42) { LeadingIcon = IconKind.Search, Location = new Point(24, 20), Width = 560 };
        var clearSearch = new IconButton(IconKind.Close, 28) { Circular = true, Visible = false };
        clearSearch.Click += (_, _) => { _searchBox.Clear(); _searchBox.Focus(); };
        _searchField.AddTrailing(clearSearch);
        _searchBox.TextChanged += (_, _) =>
        {
            clearSearch.Visible = _searchBox.TextLength > 0;
            RefreshEntryList();
        };
        _searchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) _searchBox.Clear(); };
        topBar.Controls.Add(_searchField);

        var body = CanvasPanel(new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 20, 20) });

        // Folders, smart lists and tags
        _folderColumn = CanvasPanel(new Panel { Dock = DockStyle.Left, Width = Math.Clamp(AppSettings.Current.FolderPanelWidth, PanelSplitter.MinPanelWidth, 600), Padding = new Padding(8, 6, 0, 0) });
        _folderSplitter = new PanelSplitter(_folderColumn, _folderList);
        _folderSplitter.SplitterMoved += (_, _) =>
        {
            AppSettings.Current.FolderPanelWidth = _folderColumn.Width;
            AppSettings.Current.Save();
            _folderList.Invalidate();
        };
        _folderList.Dock = DockStyle.Fill;
        _folderList.DrawItem += FolderList_DrawItem;

        // Full folder / tag name on hover (long names are ellipsized).
        var folderTip = new ToolTip();
        var tipIndex = -1;
        _folderList.MouseMove += (_, e) =>
        {
            var index = _folderList.IndexFromPoint(e.Location);
            if (index == tipIndex) return;
            tipIndex = index;
            var text = index >= 0 && _folderList.Items[index] is NavItem { Kind: not NavKind.Header } item
                ? item.Kind == NavKind.Folder ? item.Path : item.Name
                : null;
            folderTip.SetToolTip(_folderList, text);
        };
        _folderList.MouseLeave += (_, _) => tipIndex = -1;
        _folderList.SelectedIndexChanged += (_, _) =>
        {
            // Section headers aren't selectable: clicking one collapses/expands it and keeps the current selection.
            if (_folderList.SelectedItem is NavItem { Kind: NavKind.Header } header)
            {
                _suppressFolderChange = true;
                _folderList.SelectedIndex = Math.Min(_lastFolderIndex, _folderList.Items.Count - 1);
                _suppressFolderChange = false;
                BeginInvoke(() => ToggleSection(header.Name));
                return;
            }
            _lastFolderIndex = _folderList.SelectedIndex;
            if (_suppressFolderChange) return;
            // Selecting a collapsed parent opens it.
            if (_folderList.SelectedItem is NavItem { Kind: NavKind.Folder, HasChildren: true } folder && !IsExpanded(folder.Path))
            {
                _autoExpandedOnSelect = true; // the MouseDown that caused this must not toggle it straight back
                SetExpanded(folder.Path, true);
                RefreshEntryList();
                return;
            }
            RefreshEntryList();
        };

        // Clicking the arrow toggles a parent open/closed.
        _folderList.MouseDown += (_, e) =>
        {
            var index = _folderList.IndexFromPoint(e.Location);
            if (index < 0 || _folderList.Items[index] is not NavItem { Kind: NavKind.Folder, HasChildren: true } item) return;
            var autoExpanded = _autoExpandedOnSelect;
            _autoExpandedOnSelect = false;
            var chevronLeft = item.Depth * TreeIndent;
            if (autoExpanded || e.X < chevronLeft || e.X >= chevronLeft + TreeChevronZone) return;
            var path = item.Path;
            BeginInvoke(() => SetExpanded(path, !IsExpanded(path)));
        };
        _folderList.MouseUp += (_, e) =>
        {
            var index = _folderList.IndexFromPoint(e.Location);
            if (index < 0) return;
            var item = (NavItem)_folderList.Items[index];
            var onPlus = e.Button == MouseButtons.Left && item.Kind is NavKind.Folder or NavKind.All && e.X >= _folderList.ClientSize.Width - FolderPlusZone;
            if (e.Button == MouseButtons.Right || onPlus) ShowFolderMenu(item, e.Location);
        };
        _folderColumn.Controls.Add(_folderList); // PanelSplitter is its scrollbar

        // Records
        _recordArea = CanvasPanel(new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) });
        var listHeader = CanvasPanel(new Panel { Dock = DockStyle.Top, Height = 64 });
        _pageTitle.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
        _pageTitle.AutoSize = true;
        _pageTitle.UseMnemonic = false;
        _pageTitle.Location = new Point(0, 2);
        _pageTitle.Tag = AppTheme.NoTheme;
        _resultCountLabel.AutoSize = true;
        _resultCountLabel.Location = new Point(2, 38);
        _resultCountLabel.Tag = AppTheme.NoTheme;

        _sortButton = new PillButton("Sort: Name", primary: false, new Size(150, 34)) { Dropdown = true };
        var sortMenu = ModernMenu.Create(showIcons: false, disposeOnClose: false);
        void SetSort(bool byDate)
        {
            _sortByDateModified = byDate;
            _sortButton.Text = byDate ? "Sort: Recent" : "Sort: Name";
            RefreshEntryList();
        }
        sortMenu.Items.Add("Name (A–Z)", null, (_, _) => SetSort(false));
        sortMenu.Items.Add("Recently modified", null, (_, _) => SetSort(true));
        _sortButton.Click += (_, _) => sortMenu.Show(_sortButton, new Point(0, _sortButton.Height + 4));
        _newRecordButton = new PillButton("New record", primary: true, new Size(132, 34)) { LeadingIcon = IconKind.Plus };
        _newRecordButton.Click += (_, _) => OnCreateNew();
        new ToolTip().SetToolTip(_newRecordButton, "New record in the selected folder (Ctrl+N)");
        void LayoutListHeader()
        {
            // Buttons sit beside the title when there is room; in a narrow column they get their own row,
            // so they never cover the title or the record count.
            var buttonsWidth = _newRecordButton.Width + 10 + 150;
            var titleWidth = Math.Max(_pageTitle.PreferredWidth, _resultCountLabel.PreferredWidth) + 16;
            var wrap = titleWidth + buttonsWidth > listHeader.Width;
            var top = wrap ? 66 : 10;
            _sortButton.Width = wrap ? Math.Clamp(listHeader.Width - _newRecordButton.Width - 10, 120, 150) : 150;
            _sortButton.Location = new Point(listHeader.Width - _sortButton.Width, top);
            _newRecordButton.Location = new Point(wrap ? 0 : _sortButton.Left - 10 - _newRecordButton.Width, top);
            if (wrap) _sortButton.Left = Math.Max(_newRecordButton.Right + 10, listHeader.Width - _sortButton.Width);
            var height = wrap ? 110 : 64;
            if (listHeader.Height != height) listHeader.Height = height;
        }
        listHeader.Resize += (_, _) => LayoutListHeader();
        _pageTitle.TextChanged += (_, _) => LayoutListHeader();
        _resultCountLabel.TextChanged += (_, _) => LayoutListHeader();
        listHeader.Controls.Add(_newRecordButton);
        listHeader.Controls.Add(_pageTitle);
        listHeader.Controls.Add(_resultCountLabel);
        listHeader.Controls.Add(_sortButton);

        _entryListBox.Dock = DockStyle.Fill;
        _entryListBox.ItemHeight = 72;
        _entryListBox.Tag = AppTheme.NoTheme;
        _entryListBox.DrawItem += EntryListBox_DrawItem;
        _entryListBox.SelectedIndexChanged += (_, _) => OnEntrySelected();
        _entryListBox.MouseDoubleClick += (e, args) => { if (args.Button == MouseButtons.Left) OnEditSelected(); };
        _entryListBox.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            if (_entryListBox.IndexFromPoint(e.Location) >= 0) ShowRecordMenu(e.Location); // the row was selected on mouse-down
            else ShowEmptyAreaMenu(e.Location);
        };

        // Drag a record onto a folder in the left panel to move it there.
        Point? dragStart = null;
        _entryListBox.MouseDown += (_, e) => dragStart = e.Button == MouseButtons.Left && _entryListBox.IndexFromPoint(e.Location) >= 0 ? e.Location : null;
        _entryListBox.MouseMove += (_, e) =>
        {
            if (dragStart is not { } start || e.Button != MouseButtons.Left || _currentView != MainView.Vault || _selectedEntry is null) return;
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - start.X) < drag.Width && Math.Abs(e.Y - start.Y) < drag.Height) return;
            dragStart = null;
            _entryListBox.DoDragDrop(_selectedEntry, DragDropEffects.Move);
        };
        _folderList.AllowDrop = true;
        _folderList.DragOver += (_, e) =>
        {
            var index = _folderList.IndexFromPoint(_folderList.PointToClient(new Point(e.X, e.Y)));
            var isFolder = index >= 0 && _folderList.Items[index] is NavItem { Kind: NavKind.Folder } && e.Data?.GetData(typeof(VaultEntry)) is VaultEntry;
            e.Effect = isFolder ? DragDropEffects.Move : DragDropEffects.None;
            _folderList.DropHighlightIndex = isFolder ? index : -1;
        };
        _folderList.DragLeave += (_, _) => _folderList.DropHighlightIndex = -1;
        _folderList.DragDrop += async (_, e) =>
        {
            _folderList.DropHighlightIndex = -1;
            var index = _folderList.IndexFromPoint(_folderList.PointToClient(new Point(e.X, e.Y)));
            if (index >= 0 && _folderList.Items[index] is NavItem { Kind: NavKind.Folder } target && e.Data?.GetData(typeof(VaultEntry)) is VaultEntry entry)
                await MoveEntryAsync(entry, target.Path);
        };
        _entryListBox.Resize += (_, _) => _entryListBox.Invalidate();

        _emptyListLabel.AutoSize = false;
        _emptyListLabel.Dock = DockStyle.Fill;
        _emptyListLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyListLabel.Font = new Font("Segoe UI", 10.5f);
        _emptyListLabel.Tag = AppTheme.NoTheme;
        _emptyListLabel.Visible = false;
        _emptyListLabel.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowEmptyAreaMenu(_entryListBox.PointToClient(_emptyListLabel.PointToScreen(e.Location))); };
        _canvasPanels.Add(_emptyListLabel);

        _recordArea.Controls.Add(_entryListBox);
        _recordArea.Controls.Add(_emptyListLabel);
        _recordArea.Controls.Add(listHeader);

        // Detail card
        _detailPanel.Dock = DockStyle.Right;
        _detailPanel.Width = 440;
        _detailPanel.Padding = new Padding(8, 0, 0, 0);
        _detailPanel.Visible = false;
        _canvasPanels.Add(_detailPanel);
        _detailCard.Paint += (_, e) => UiKit.PaintCard(e.Graphics, _detailCard, 16);
        _detailCard.Resize += (_, _) => _detailCard.Invalidate();
        _detailCard.Controls.Add(_detailContent);
        _detailPanel.Controls.Add(_detailCard);
        _detailContent.Resize += (_, _) =>
        {
            if (_detailContent.Width != _renderedDetailWidth) RenderDetail(_selectedEntry);
        };

        _auditPanel = new SecurityAuditPanel(_data, () => _masterPassword, () => SwitchView(MainView.Vault)) { Dock = DockStyle.Fill, Visible = false };
        _identityPanel = new IdentityPaymentsPanel(_data, SaveAsync) { Dock = DockStyle.Fill, Visible = false };
        _breachPanel = new BreachWatchPanel(_data, EditFromElsewhere) { Dock = DockStyle.Fill, Visible = false };
        _settingsPanel = new SettingsPanel(_data, _store, () => _masterPassword, ChangeMasterPasswordAsync, SaveAsync,
            () => { RefreshFolderList(); RefreshEntryList(); _ = RefreshRecoveryBannerAsync(); }, ShowToast,
            () => SyncNowAsync(interactive: true)) { Dock = DockStyle.Fill, Visible = false };

        body.Controls.Add(_recordArea);
        body.Controls.Add(_detailPanel);
        body.Controls.Add(_folderSplitter); // docks just right of the folder column
        body.Controls.Add(_folderColumn);
        body.Controls.Add(_auditPanel);
        body.Controls.Add(_identityPanel);
        body.Controls.Add(_breachPanel);
        body.Controls.Add(_settingsPanel);

        // Toast ("Copied …") floating at the bottom of the content area.
        _toast = new Panel { Size = new Size(320, 40), Visible = false, Tag = AppTheme.NoTheme };
        _toast.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.Canvas);
            using var path = RoundedField.RoundRect(new RectangleF(0, 0, _toast.Width - 1, _toast.Height - 1), 20);
            using var bg = new SolidBrush(Color.FromArgb(30, 41, 59));
            g.FillPath(bg, path);
            Icons.Draw(g, IconKind.Shield, new Rectangle(16, 12, 16, 16), Color.FromArgb(134, 239, 172), 1.6f, Color.FromArgb(30, 41, 59));
            TextRenderer.DrawText(g, _toastText, Font, new Rectangle(42, 0, _toast.Width - 52, _toast.Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        };
        content.Controls.Add(_toast);
        content.Resize += (_, _) => PositionToast(content);

        _recoveryBanner = BuildRecoveryBanner();
        content.Controls.Add(body);
        content.Controls.Add(_recoveryBanner); // docks just below the search bar
        content.Controls.Add(topBar);

        Controls.Add(content);
        Controls.Add(rail);
    }

    private Panel CanvasPanel(Panel panel)
    {
        panel.Tag = AppTheme.NoTheme;
        panel.BackColor = AppTheme.Canvas;
        _canvasPanels.Add(panel);
        return panel;
    }

    private void PositionToast(Control host) =>
        _toast.Location = new Point((host.Width - _toast.Width) / 2, host.Height - _toast.Height - 28);

    private void ShowToast(string text)
    {
        _toastText = text;
        using (var g = CreateGraphics())
            _toast.Width = TextRenderer.MeasureText(g, text, Font).Width + 64;
        PositionToast(_toast.Parent!);
        _toast.Visible = true;
        _toast.BringToFront();
        _toast.Invalidate();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private int AddNavRow(Panel parent, IconKind icon, string label, Func<bool> isActive, int y, Action? onClick, string? badge = null)
    {
        var row = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(parent.Width, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Tag = AppTheme.NoTheme,
            Cursor = onClick is not null ? Cursors.Hand : Cursors.Default
        };
        var hovered = false;
        var boldFont = new Font(Font, FontStyle.Bold);
        row.MouseEnter += (_, _) => { hovered = onClick is not null; row.Invalidate(); };
        row.MouseLeave += (_, _) => { hovered = false; row.Invalidate(); };
        row.Paint += (_, e) =>
        {
            var active = isActive();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.RailBg);

            var pillRect = new Rectangle(0, 2, row.Width - 1, row.Height - 4);
            var fg = active ? AppTheme.RailTextActive : onClick is not null ? AppTheme.RailTextInactive : Color.FromArgb(120, 145, 185);
            var pillColor = active ? AppTheme.RailSelection : hovered ? UiKit.Blend(AppTheme.RailSelection, AppTheme.RailBg, 0.5f) : AppTheme.RailBg;

            if (pillColor != AppTheme.RailBg)
            {
                using var pillBrush = new SolidBrush(pillColor);
                using var path = RoundedField.RoundRect(pillRect, 9);
                g.FillPath(pillBrush, path);
            }
            if (active && _railExpanded)
            {
                using var accent = new SolidBrush(AppTheme.RailMarker);
                using var bar = RoundedField.RoundRect(new RectangleF(0, 11, 3.5f, row.Height - 22), 1.5f);
                g.FillPath(accent, bar);
            }

            var iconX = _railExpanded ? pillRect.X + 14 : pillRect.X + (pillRect.Width - 18) / 2;
            var iconBounds = new Rectangle(iconX, pillRect.Y + (pillRect.Height - 18) / 2, 18, 18);
            Icons.Draw(g, icon, iconBounds, fg, 1.6f, pillColor);

            if (!_railExpanded) return;
            var textRect = new Rectangle(iconBounds.Right + 12, pillRect.Y, pillRect.Width - iconBounds.Right - 12, pillRect.Height);
            TextRenderer.DrawText(g, label, active ? boldFont : Font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            if (badge is not null)
            {
                var size = TextRenderer.MeasureText(badge, _chipFont);
                UiKit.DrawChip(g, new Point(pillRect.Right - size.Width - 18, pillRect.Y + (pillRect.Height - 22) / 2), badge, _chipFont,
                    Color.FromArgb(200, 215, 240), UiKit.Blend(Color.White, pillColor, 0.12f));
            }
        };
        if (onClick is not null) row.Click += (_, _) => onClick();
        new ToolTip().SetToolTip(row, label);
        AppTheme.ThemeChanged += row.Invalidate;

        parent.Controls.Add(row);
        return y + 44;
    }

    // ---------- list drawing ----------

    private void FolderList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _folderList.Items[e.Index] is not NavItem item) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(AppTheme.Canvas))
            g.FillRectangle(bg, e.Bounds);

        if (item.Kind == NavKind.Header)
        {
            var headerHot = (e.State & DrawItemState.HotLight) == DrawItemState.HotLight;
            var headerColor = headerHot ? AppTheme.Text : AppTheme.SubText;
            var textArea = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 10, e.Bounds.Width - 20, e.Bounds.Height - 10);
            TextRenderer.DrawText(g, item.Name, _sectionFont, textArea, headerColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            var textWidth = TextRenderer.MeasureText(g, item.Name, _sectionFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            var collapsed = IsSectionCollapsed(item.Name);
            var chevron = new Rectangle(textArea.X + textWidth + 10, textArea.Y + (textArea.Height - 8) / 2, 8, 8);
            Icons.Draw(g, collapsed ? IconKind.ChevronRight : IconKind.ChevronDown, chevron, headerColor, 1.5f, AppTheme.Canvas);
            if (collapsed)
            {
                var sectionCount = item.Count.ToString();
                var sectionCountSize = TextRenderer.MeasureText(g, sectionCount, _chipFont);
                TextRenderer.DrawText(g, sectionCount, _chipFont, new Rectangle(e.Bounds.Right - sectionCountSize.Width - 14, textArea.Y, sectionCountSize.Width + 4, textArea.Height),
                    AppTheme.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            }
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var hot = (e.State & DrawItemState.HotLight) == DrawItemState.HotLight;
        var pillRect = new Rectangle(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width - 1, e.Bounds.Height - 4);
        var pillColor = selected ? (AppTheme.Mode == ThemeMode.Dark ? AppTheme.Selection : AppTheme.CardBg)
            : hot ? UiKit.Blend(AppTheme.Accent, AppTheme.Canvas, AppTheme.Mode == ThemeMode.Dark ? 0.10f : 0.06f)
            : AppTheme.Canvas;
        if (hot && !selected)
        {
            using var hoverBrush = new SolidBrush(pillColor);
            using var hoverPath = RoundedField.RoundRect(pillRect, 9);
            g.FillPath(hoverBrush, hoverPath);
        }
        if (selected)
        {
            using var pillBrush = new SolidBrush(pillColor);
            using var path = RoundedField.RoundRect(pillRect, 9);
            g.FillPath(pillBrush, path);
            if (AppTheme.Mode == ThemeMode.Light)
            {
                using var pen = new Pen(AppTheme.Border);
                g.DrawPath(pen, path);
            }
        }

        var (icon, iconColor) = item.Kind switch
        {
            NavKind.All => (IconKind.Shield, selected ? AppTheme.Accent : AppTheme.SubText),
            NavKind.Favorites => (IconKind.StarFilled, Color.FromArgb(245, 166, 35)),
            NavKind.Tag => (IconKind.Tag, selected ? AppTheme.Accent : AppTheme.SubText),
            NavKind.RecordType => (RecordTypeIcon(item.Name), selected ? AppTheme.Accent : AppTheme.SubText),
            _ => (IconKind.Folder, selected ? AppTheme.Accent : AppTheme.SubText)
        };
        var indent = item.Kind == NavKind.Folder ? item.Depth * TreeIndent : 0;
        if (item.HasChildren)
        {
            var chevron = new Rectangle(pillRect.X + indent + 6, pillRect.Y + (pillRect.Height - 9) / 2, 9, 9);
            Icons.Draw(g, IsExpanded(item.Path) ? IconKind.ChevronDown : IconKind.ChevronRight, chevron, AppTheme.SubText, 1.5f, pillColor);
        }
        var iconBounds = new Rectangle(pillRect.X + indent + TreeChevronZone, pillRect.Y + (pillRect.Height - 18) / 2, 18, 18);
        if (item.Kind == NavKind.RecordType)
            DrawRecordTypeTile(g, Rectangle.Inflate(iconBounds, 2, 2), item.Name);
        else
            Icons.Draw(g, icon, iconBounds, iconColor, 1.5f, pillColor);

        var countText = item.Count.ToString();
        var countSize = TextRenderer.MeasureText(g, countText, _chipFont);
        var countRect = new Rectangle(pillRect.Right - countSize.Width - 14, pillRect.Y, countSize.Width + 4, pillRect.Height);
        var showPlus = (e.State & DrawItemState.HotLight) != 0 && item.Kind is NavKind.Folder or NavKind.All && _folderList.DropHighlightIndex != e.Index;
        if (showPlus)
        {
            // Hover: a small "+" (new record / subfolder menu) replaces the count.
            var plus = new Rectangle(pillRect.Right - 30, pillRect.Y + (pillRect.Height - 22) / 2, 22, 22);
            using (var plusBack = new SolidBrush(UiKit.Tint(AppTheme.Accent, pillColor)))
                g.FillEllipse(plusBack, plus);
            Icons.Draw(g, IconKind.Plus, Rectangle.Inflate(plus, -6, -6), AppTheme.Accent, 1.8f, pillColor);
            countRect = new Rectangle(plus.Left - 4, pillRect.Y, 4, pillRect.Height);
        }
        else
        {
            TextRenderer.DrawText(g, countText, _chipFont, countRect, AppTheme.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }

        var textRect = new Rectangle(iconBounds.Right + 10, pillRect.Y, countRect.Left - iconBounds.Right - 14, pillRect.Height);
        TextRenderer.DrawText(g, item.Name, selected ? _folderFontBold : _folderFont, textRect, selected ? AppTheme.Text : AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // Letter avatar, or the site's icon on a white disc when available.
    private void DrawEntryAvatar(Graphics g, Rectangle rect, VaultEntry entry)
    {
        var favicon = string.IsNullOrEmpty(entry.Url) ? null : FaviconCache.Get(entry.Url, this);
        if (favicon is null)
        {
            UiKit.DrawAvatar(g, rect, entry.Name);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var disc = new SolidBrush(Color.White)) g.FillEllipse(disc, rect);
        using (var ring = new Pen(AppTheme.Border)) g.DrawEllipse(ring, rect);
        var inset = rect.Width / 4;
        g.DrawImage(favicon, Rectangle.Inflate(rect, -inset, -inset));
    }

    private void EntryListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var entry = (VaultEntry)_entryListBox.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(AppTheme.Canvas))
            g.FillRectangle(bg, e.Bounds);

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var tile = new RectangleF(e.Bounds.Left, e.Bounds.Top + 4, e.Bounds.Width - 1, e.Bounds.Height - 8);
        var tileColor = selected ? AppTheme.Selection : AppTheme.CardBg;
        using (var path = RoundedField.RoundRect(tile, 12))
        {
            using var fill = new SolidBrush(tileColor);
            var hovered = (e.State & DrawItemState.HotLight) == DrawItemState.HotLight;
            using var pen = new Pen(selected ? AppTheme.Accent : hovered ? UiKit.Blend(AppTheme.Accent, AppTheme.Border, 0.45f) : AppTheme.Border, selected ? 1.4f : 1f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        var avatarRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + (e.Bounds.Height - 40) / 2, 40, 40);
        DrawEntryAvatar(g, avatarRect, entry);

        // Right side: folder chip in "All Items"/search/smart lists, deletion date in Deleted Items.
        var right = e.Bounds.Right - 14;
        var showFolder = _folderList.SelectedItem is not NavItem { Kind: NavKind.Folder, HasChildren: false } || _searchBox.TextLength > 0;
        string? chip = _currentView == MainView.Deleted && entry.DeletedAt is not null
            ? $"Deleted {entry.DeletedAt.Value.LocalDateTime:MMM d}"
            : showFolder ? LeafFolderName(entry.Folder) : null;
        if (!string.IsNullOrEmpty(chip))
        {
            // Keep the record name readable: the tag never takes more than about a third of the row.
            var maxChipText = Math.Max(40, e.Bounds.Width / 3 - 36);
            while (chip.Length > 2 && TextRenderer.MeasureText(g, chip, _chipFont, Size.Empty, TextFormatFlags.NoPadding).Width > maxChipText)
                chip = chip[..^2] + "…";
            var chipW = TextRenderer.MeasureText(g, chip, _chipFont, Size.Empty, TextFormatFlags.NoPadding).Width + 20 + 16;
            right -= chipW;
            UiKit.DrawChip(g, new Point(right, e.Bounds.Top + (e.Bounds.Height - 22) / 2), chip, _chipFont,
                AppTheme.SubText, selected ? UiKit.Blend(AppTheme.CardBg, tileColor, 0.6f) : AppTheme.Hover,
                _currentView == MainView.Deleted ? IconKind.Trash : IconKind.Folder);
            right -= 10;
        }

        var textLeft = avatarRect.Right + 14;
        var nameWidth = TextRenderer.MeasureText(g, entry.Name, _listTitleFont).Width;
        var nameRect = new Rectangle(textLeft, e.Bounds.Top + 15, Math.Min(nameWidth + 6, right - textLeft - (entry.Favorite ? 22 : 0)), 22);
        var host = CsvTransfer.HostOf(entry.Url);
        var subtitle = string.IsNullOrEmpty(entry.Username) ? host ?? "No username" : host is null ? entry.Username : $"{entry.Username}  ·  {host}";
        var subRect = new Rectangle(textLeft, e.Bounds.Top + 37, right - textLeft, 20);

        TextRenderer.DrawText(g, entry.Name, _listTitleFont, nameRect, AppTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (entry.Favorite)
            Icons.Draw(g, IconKind.StarFilled, new Rectangle(nameRect.Right + 2, nameRect.Top + 4, 13, 13), Color.FromArgb(245, 166, 35), 1.4f, tileColor);
        TextRenderer.DrawText(g, subtitle, _listSubFont, subRect, AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ---------- data ----------

    private void RefreshFolderList()
    {
        var previous = _folderList.SelectedItem as NavItem;
        var live = _data.Entries.Where(e => e.DeletedAt is null).ToList();

        var items = new List<NavItem>
        {
            new(NavKind.All, "All Items", live.Count),
            new(NavKind.Favorites, "Favorites", live.Count(e => e.Favorite))
        };

        // Record types first (a short, fixed list), then the potentially long folder tree, then tags.
        var types = live.GroupBy(RecordTypes.Of).ToDictionary(g => g.Key, g => g.Count());
        void AddSection(string title, IReadOnlyCollection<NavItem> children)
        {
            if (children.Count == 0) return;
            items.Add(new NavItem(NavKind.Header, title, children.Count, HasChildren: true));
            if (!IsSectionCollapsed(title)) items.AddRange(children);
        }

        AddSection("RECORD TYPES", RecordTypes.All.Where(types.ContainsKey).Select(t => new NavItem(NavKind.RecordType, t, types[t])).ToList());
        AddSection("FOLDERS", BuildFolderTree(live, _data.Folders).ToList());

        var tags = live.SelectMany(e => e.Tags).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        AddSection("TAGS", tags.Select(g => new NavItem(NavKind.Tag, g.Key, g.Count())).ToList());

        // Keep the scroll position across the rebuild (expanding a folder shouldn't jump the list to the top).
        var topIndex = _folderList.TopIndex;
        _folderList.BeginUpdate();
        _folderList.Items.Clear();
        foreach (var item in items) _folderList.Items.Add(item);
        if (items.Count > 0) _folderList.TopIndex = Math.Min(topIndex, items.Count - 1);
        _folderList.EndUpdate();

        // Restore the selection; if it is now hidden inside a collapsed parent, select that parent instead.
        var restore = previous is null ? 0 : items.FindIndex(i => i.Kind == previous.Kind &&
            string.Equals(i.Kind == NavKind.Folder ? i.Path : i.Name, previous.Kind == NavKind.Folder ? previous.Path : previous.Name, StringComparison.OrdinalIgnoreCase));
        if (restore < 0 && previous is { Kind: NavKind.Folder })
            restore = items.FindLastIndex(i => i.Kind == NavKind.Folder && previous.Path.StartsWith(i.Path + "\\", StringComparison.OrdinalIgnoreCase));
        var newIndex = restore >= 0 ? restore : 0;
        var selectionChanged = previous is null || !Equals(items[newIndex], previous);
        _lastFolderIndex = newIndex;
        _suppressFolderChange = !selectionChanged; // keep the record selection when only expanding/collapsing
        _folderList.SelectedIndex = newIndex;
        _suppressFolderChange = false;
        _folderList.Invalidate();
    }

    // Depth-first folder tree. Parents that only exist as part of a path (no records directly in them) are included.
    private static IEnumerable<NavItem> BuildFolderTree(List<VaultEntry> live, IEnumerable<string> explicitFolders)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in explicitFolders)
        {
            var parts = NormalizeFolder(folder).Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i <= parts.Length; i++)
                counts.TryAdd(string.Join("\\", parts.Take(i)), 0); // empty folders still show
        }
        foreach (var entry in live)
        {
            var parts = NormalizeFolder(entry.Folder).Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) parts = new[] { "General" };
            for (var i = 1; i <= parts.Length; i++)
            {
                var path = string.Join("\\", parts.Take(i));
                counts[path] = counts.GetValueOrDefault(path) + 1; // parents count everything beneath them
            }
        }

        var paths = counts.Keys.ToList();
        IEnumerable<NavItem> Children(string? parent, int depth)
        {
            var prefix = parent is null ? "" : parent + "\\";
            var direct = paths
                .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Length > prefix.Length && p.IndexOf('\\', prefix.Length) < 0)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var path in direct)
            {
                var hasChildren = paths.Any(p => p.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase));
                yield return new NavItem(NavKind.Folder, path[prefix.Length..], counts[path], path, depth, hasChildren);
                if (hasChildren && IsExpanded(path))
                    foreach (var child in Children(path, depth + 1))
                        yield return child;
            }
        }
        return Children(null, 0).ToList();
    }

    private void RefreshEntryList()
    {
        List<VaultEntry> list;
        var term = _searchBox.Text.Trim();

        if (_currentView == MainView.Deleted)
        {
            list = _data.Entries
                .Where(e => e.DeletedAt is not null)
                .OrderByDescending(e => e.DeletedAt)
                .ToList();
            _pageTitle.Text = "Deleted Items";
            _resultCountLabel.Text = list.Count == 1 ? "1 deleted item" : $"{list.Count} deleted items";
            _emptyListLabel.Text = "Nothing here. Deleted records will appear in this list.";
        }
        else
        {
            var nav = _folderList.SelectedItem as NavItem ?? new NavItem(NavKind.All, "All Items", 0);
            IEnumerable<VaultEntry> entries = _data.Entries.Where(e => e.DeletedAt is null);

            if (!string.IsNullOrEmpty(term))
            {
                entries = entries.Where(e =>
                    e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Folder.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Url.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    e.Notes.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                entries = nav.Kind switch
                {
                    NavKind.Favorites => entries.Where(e => e.Favorite),
                    NavKind.Folder => entries.Where(e => IsInFolder(e, nav.Path)),
                    NavKind.Tag => entries.Where(e => e.Tags.Contains(nav.Name, StringComparer.OrdinalIgnoreCase)),
                    NavKind.RecordType => entries.Where(e => RecordTypes.Of(e) == nav.Name),
                    _ => entries
                };
            }

            list = _sortByDateModified
                ? entries.OrderByDescending(e => e.ModifiedAt).ToList()
                : entries.OrderByDescending(e => e.Favorite).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

            _pageTitle.Text = !string.IsNullOrEmpty(term) ? "Search results" : nav.Kind switch
            {
                NavKind.All => "My Vault",
                NavKind.Tag => "#" + nav.Name,
                NavKind.Folder => nav.Path,
                _ => nav.Name
            };
            _resultCountLabel.Text = list.Count == 1 ? "1 record" : $"{list.Count} records";
            _emptyListLabel.Text = !string.IsNullOrEmpty(term) ? $"No records match \"{term}\"."
                : nav.Kind == NavKind.Favorites ? "No favourites yet. Open a record and tap the ☆ star to pin it here."
                : "Your vault is empty. Click \"Create New\" to add your first record.";
        }

        _entryListBox.BeginUpdate();
        _entryListBox.Items.Clear();
        foreach (var entry in list)
            _entryListBox.Items.Add(entry);
        _entryListBox.EndUpdate();

        _emptyListLabel.Visible = list.Count == 0;
        _entryListBox.Visible = list.Count > 0;
        _sortButton.Visible = _newRecordButton.Visible = _currentView != MainView.Deleted;

        RenderDetail(null);
    }

    private void OnEntrySelected()
    {
        var entry = _entryListBox.SelectedItem as VaultEntry;
        RenderDetail(entry);
    }

    // ---------- detail card ----------

    private void RenderDetail(VaultEntry? entry)
    {
        _selectedEntry = entry;
        _totpCodeLabel = null;
        _totpRing = null;
        _detailAvatar = null;

        // Toggle visibility first: it resizes _detailContent, and the resulting width is what we lay out against.
        _detailPanel.Visible = entry is not null && _currentView is MainView.Vault or MainView.Deleted;
        _renderedDetailWidth = _detailContent.Width;

        _detailContent.SuspendLayout();
        foreach (Control c in _detailContent.Controls.Cast<Control>().ToList()) c.Dispose();
        _detailContent.AutoScrollPosition = Point.Empty;

        if (entry is null)
        {
            _detailContent.ResumeLayout();
            return;
        }

        var width = _renderedDetailWidth - SystemInformation.VerticalScrollBarWidth - 6;
        var y = 0;

        // Header: avatar, name, folder + tag chips, favourite, close.
        var avatar = _detailAvatar = new Panel { Size = new Size(56, 56), Location = new Point(0, y), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        avatar.Paint += (_, e) => { e.Graphics.Clear(AppTheme.CardBg); DrawEntryAvatar(e.Graphics, new Rectangle(0, 0, 55, 55), entry); };
        var title = new Label
        {
            Text = entry.Name,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoEllipsis = true,
            Location = new Point(70, y + 1),
            Size = new Size(width - 70 - 72, 30),
            Tag = AppTheme.NoTheme
        };
        var chips = new Panel { Location = new Point(70, y + 33), Size = new Size(width - 70, 24), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        chips.Paint += (_, e) =>
        {
            e.Graphics.Clear(AppTheme.CardBg);
            var x = UiKit.DrawChip(e.Graphics, Point.Empty, entry.Folder, _chipFont, AppTheme.Accent, UiKit.Tint(AppTheme.Accent, AppTheme.CardBg), IconKind.Folder) + 6;
            var type = RecordTypes.Of(entry);
            x += UiKit.DrawChip(e.Graphics, new Point(x, 0), type, _chipFont, AppTheme.SubText, AppTheme.Hover, RecordTypeIcon(type)) + 6;
            foreach (var tag in entry.Tags)
            {
                if (x > chips.Width - 40) break;
                x += UiKit.DrawChip(e.Graphics, new Point(x, 0), tag, _chipFont, AppTheme.SubText, AppTheme.Hover, IconKind.Tag) + 6;
            }
        };
        var starBtn = new IconButton(entry.Favorite ? IconKind.StarFilled : IconKind.Star, 32)
        {
            IconColor = entry.Favorite ? Color.FromArgb(245, 166, 35) : AppTheme.SubText,
            Location = new Point(width - 70, y - 1),
            Visible = _currentView == MainView.Vault
        };
        new ToolTip().SetToolTip(starBtn, entry.Favorite ? "Remove from favourites" : "Add to favourites");
        starBtn.Click += async (_, _) =>
        {
            VaultOperations.ToggleFavorite(entry);
            await SaveAsync();
            RefreshFolderList();
            RefreshEntryList();
            SelectEntry(entry);
        };
        var closeBtn = new IconButton(IconKind.Close, 32) { IconColor = AppTheme.SubText, Location = new Point(width - 32, y - 1) };
        closeBtn.Click += (_, _) => { _entryListBox.ClearSelected(); RenderDetail(null); };
        _detailContent.Controls.AddRange(new Control[] { avatar, title, chips, starBtn, closeBtn });
        y += 74;

        // Actions.
        if (_currentView == MainView.Deleted)
        {
            var restoreBtn = new PillButton("Restore", primary: true, new Size(120, 36)) { LeadingIcon = IconKind.Back, Location = new Point(0, y) };
            restoreBtn.Click += (_, _) => OnRestoreSelected();
            var purgeBtn = new PillButton("Delete forever", primary: false, new Size(140, 36)) { LeadingIcon = IconKind.Trash, AccentColor = AppTheme.Danger, Location = new Point(130, y) };
            purgeBtn.Click += (_, _) => OnPurgeSelected();
            _detailContent.Controls.Add(restoreBtn);
            _detailContent.Controls.Add(purgeBtn);
        }
        else
        {
            var editBtn = new PillButton("Edit", primary: true, new Size(100, 36)) { LeadingIcon = IconKind.Edit, Location = new Point(0, y) };
            editBtn.Click += (_, _) => OnEditSelected();
            var deleteBtn = new PillButton("Delete", primary: false, new Size(100, 36)) { LeadingIcon = IconKind.Trash, AccentColor = AppTheme.Danger, Location = new Point(110, y) };
            deleteBtn.Click += (_, _) => OnDeleteSelected();
            var moveBtn = new PillButton("Move", primary: false, new Size(96, 36)) { LeadingIcon = IconKind.Folder, Location = new Point(220, y) };
            moveBtn.Click += async (_, _) => await ChooseFolderAndMoveAsync(entry);
            new ToolTip().SetToolTip(moveBtn, "Move to another folder");
            _detailContent.Controls.Add(editBtn);
            _detailContent.Controls.Add(deleteBtn);
            _detailContent.Controls.Add(moveBtn);
            if (CsvTransfer.HostOf(entry.Url) is not null)
            {
                var openBtn = new PillButton("", primary: false, new Size(36, 36)) { LeadingIcon = IconKind.External, Location = new Point(326, y) };
                openBtn.Click += (_, _) => OpenWebsite(entry.Url);
                new ToolTip().SetToolTip(openBtn, "Open website");
                _detailContent.Controls.Add(openBtn);
            }
        }
        y += 56;

        y = AddSectionTitle("Login", y);
        y = AddReadField("Username", entry.Username, width, y, copyable: true);
        y = AddSecretField("Password", entry.Secret, width, y, showStrength: true);
        if (!string.IsNullOrEmpty(entry.Url))
            y = AddWebsiteField(entry.Url, width, y);

        if (entry.TotpSecret is not null)
            y = AddTotpField(entry.TotpSecret, width, y);

        if (entry.CustomFields.Count > 0)
        {
            y = AddSectionTitle("Custom fields", y + 8);
            foreach (var field in entry.CustomFields)
            {
                var label = string.IsNullOrWhiteSpace(field.Label) ? "Untitled" : field.Label;
                y = field.IsSecret
                    ? AddSecretField(label, field.Value, width, y, showStrength: false)
                    : AddReadField(label, field.Value, width, y, copyable: true);
            }
        }

        if (!string.IsNullOrEmpty(entry.Notes))
        {
            y = AddSectionTitle("Notes", y + 8);
            y = AddReadField(null, entry.Notes, width, y, copyable: false);
        }

        if (entry.PasswordHistory.Count > 0)
        {
            y = AddSectionTitle($"Password history ({entry.PasswordHistory.Count})", y + 8);
            foreach (var old in entry.PasswordHistory.Take(5))
                y = AddSecretField($"Used until {old.ChangedAt.LocalDateTime:MMM d, yyyy}", old.Password, width, y, showStrength: false);
        }

        var meta = new Label
        {
            Text = $"Created {entry.CreatedAt.LocalDateTime:MMM d, yyyy}   ·   Modified {entry.ModifiedAt.LocalDateTime:MMM d, yyyy h:mm tt}",
            ForeColor = AppTheme.SubText,
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(0, y + 14),
            Tag = AppTheme.NoTheme
        };
        _detailContent.Controls.Add(meta);
        _detailContent.Controls.Add(new Panel { Location = new Point(0, meta.Bottom), Size = new Size(1, 16), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg });

        _detailContent.ResumeLayout();
    }

    private int AddSectionTitle(string text, int y)
    {
        _detailContent.Controls.Add(new Label
        {
            Text = text.ToUpperInvariant(),
            Font = _sectionFont,
            ForeColor = AppTheme.SubText,
            AutoSize = true,
            Location = new Point(0, y),
            Tag = AppTheme.NoTheme
        });
        return y + 24;
    }

    // A field "row": rounded tinted box with caption, value and trailing actions.
    private Panel AddFieldBox(int width, int y, int height)
    {
        var fieldBack = AppTheme.Mode == ThemeMode.Dark ? AppTheme.Field : AppTheme.Canvas;
        var box = new Panel { Location = new Point(0, y), Size = new Size(width, height), Tag = AppTheme.NoTheme, BackColor = fieldBack };
        box.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, box.Width - 1.5f, box.Height - 1.5f), 10);
            using var fill = new SolidBrush(box.BackColor);
            g.FillPath(fill, path);
        };
        _detailContent.Controls.Add(box);
        return box;
    }

    // Makes the whole field box (and its text) copy the value on click, with a hover highlight.
    // Icon buttons inside keep their own actions; the website link keeps opening the site.
    private void MakeClickToCopy(Panel box, Func<string> value, string what)
    {
        var normal = box.BackColor;
        var hot = UiKit.Blend(AppTheme.Accent, normal, AppTheme.Mode == ThemeMode.Dark ? 0.14f : 0.08f);
        var tip = new ToolTip();

        void SetHot(bool on)
        {
            if (box.IsDisposed) return;
            box.BackColor = on ? hot : normal;
            box.Invalidate(true);
        }

        void Hook(Control c)
        {
            c.Cursor = Cursors.Hand;
            tip.SetToolTip(c, $"Click to copy {what.ToLowerInvariant()}");
            c.MouseEnter += (_, _) => SetHot(true);
            c.MouseLeave += (_, _) =>
            {
                // Moving onto a child control is not leaving the box.
                if (!box.IsDisposed && !box.ClientRectangle.Contains(box.PointToClient(Cursor.Position))) SetHot(false);
            };
            c.Click += (_, _) => CopyToClipboard(value(), what);
        }

        Hook(box);
        foreach (Control child in box.Controls)
        {
            if (child is IconButton or LinkLabel)
                child.MouseLeave += (_, _) => { if (!box.IsDisposed && !box.ClientRectangle.Contains(box.PointToClient(Cursor.Position))) SetHot(false); };
            else
                Hook(child);
        }
    }

    private Label AddCaption(Panel box, string text)
    {
        var caption = new Label { Text = text, UseMnemonic = false, ForeColor = AppTheme.SubText, Font = new Font("Segoe UI Semibold", 8.5f), AutoSize = true, Location = new Point(13, 9), Tag = AppTheme.NoTheme };
        box.Controls.Add(caption);
        return caption;
    }

    private IconButton AddBoxButton(Panel box, IconKind icon, string tip, int right, Action onClick)
    {
        var button = new IconButton(icon, 32) { Circular = false, Location = new Point(right - 32, (box.Height - 32) / 2) };
        new ToolTip().SetToolTip(button, tip);
        button.Click += (_, _) => onClick();
        box.Controls.Add(button);
        return button;
    }

    private int AddReadField(string? label, string value, int width, int y, bool copyable)
    {
        var display = string.IsNullOrEmpty(value) ? "—" : value;
        var valueFont = new Font("Segoe UI", 10.5f);
        var valueWidth = width - 28 - (copyable ? 40 : 0);
        var textHeight = TextRenderer.MeasureText(display, valueFont, new Size(valueWidth, 0), TextFormatFlags.WordBreak).Height;
        var top = label is null ? 12 : 30;
        var box = AddFieldBox(width, y, Math.Max(label is null ? 44 : 60, top + textHeight + 10));

        if (label is not null) AddCaption(box, label);
        box.Controls.Add(new Label
        {
            Text = display,
            UseMnemonic = false,
            ForeColor = string.IsNullOrEmpty(value) ? AppTheme.SubText : AppTheme.Text,
            Font = valueFont,
            Location = new Point(12, top),
            Size = new Size(valueWidth, textHeight),
            Tag = AppTheme.NoTheme
        });

        if (copyable && !string.IsNullOrEmpty(value))
        {
            AddBoxButton(box, IconKind.Copy, "Copy", width - 8, () => CopyToClipboard(value, label ?? "Value"));
            MakeClickToCopy(box, () => value, label ?? "Value");
        }

        return box.Bottom + 8;
    }

    private int AddWebsiteField(string url, int width, int y)
    {
        var box = AddFieldBox(width, y, 60);
        AddCaption(box, "Website");
        var link = new LinkLabel
        {
            Text = url,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 10.5f),
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AutoEllipsis = true,
            Location = new Point(12, 30),
            Size = new Size(width - 28 - 76, 22),
            Tag = AppTheme.NoTheme
        };
        link.LinkClicked += (_, _) => OpenWebsite(url);
        box.Controls.Add(link);
        AddBoxButton(box, IconKind.Copy, "Copy", width - 8, () => CopyToClipboard(url, "Website"));
        AddBoxButton(box, IconKind.External, "Open in browser", width - 44, () => OpenWebsite(url));
        MakeClickToCopy(box, () => url, "Website");
        return box.Bottom + 8;
    }

    private int AddSecretField(string label, string value, int width, int y, bool showStrength)
    {
        var box = AddFieldBox(width, y, 60);
        var caption = AddCaption(box, label);

        var masked = string.IsNullOrEmpty(value) ? "—" : new string('•', Math.Min(14, Math.Max(8, value.Length)));
        var valueLabel = new Label
        {
            Text = masked,
            UseMnemonic = false,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 10.5f),
            AutoEllipsis = true,
            Location = new Point(12, 30),
            Size = new Size(width - 28 - 76, 22),
            Tag = AppTheme.NoTheme
        };
        box.Controls.Add(valueLabel);

        if (showStrength && !string.IsNullOrEmpty(value))
        {
            var level = PasswordStrength.Evaluate(value);
            var color = AppTheme.StrengthColor(level);
            var chip = new Panel { Location = new Point(caption.Right + 8, 6), Size = new Size(110, 22), Tag = AppTheme.NoTheme, BackColor = box.BackColor };
            chip.Paint += (_, e) =>
            {
                e.Graphics.Clear(box.BackColor);
                UiKit.DrawChip(e.Graphics, Point.Empty, UiKit.StrengthText(level), _chipFont, color, UiKit.Tint(color, box.BackColor));
            };
            box.Controls.Add(chip);
        }

        if (!string.IsNullOrEmpty(value))
        {
            AddBoxButton(box, IconKind.Copy, "Copy", width - 8, () => CopyToClipboard(value, label));
            IconButton? showButton = null;
            showButton = AddBoxButton(box, IconKind.Eye, "Show / hide", width - 44, () =>
            {
                var reveal = valueLabel.Text == masked;
                valueLabel.Text = reveal ? value : masked;
                valueLabel.Font = new Font(reveal ? "Consolas" : "Segoe UI", 10.5f);
                showButton!.Icon = reveal ? IconKind.EyeOff : IconKind.Eye;
                showButton.Invalidate();
            });
            MakeClickToCopy(box, () => value, label);
        }

        return box.Bottom + 8;
    }

    private int AddTotpField(string totpSecret, int width, int y)
    {
        var box = AddFieldBox(width, y, 66);
        AddCaption(box, "One-time code");

        _totpCodeLabel = new Label
        {
            Font = new Font("Consolas", 17f, FontStyle.Bold),
            ForeColor = AppTheme.Accent,
            AutoSize = true,
            Location = new Point(10, 28),
            Tag = AppTheme.NoTheme
        };
        box.Controls.Add(_totpCodeLabel);

        // Countdown ring.
        _totpRing = new Panel { Size = new Size(30, 30), Location = new Point(width - 80, 18), Tag = AppTheme.NoTheme, BackColor = box.BackColor };
        var ring = _totpRing;
        ring.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(box.BackColor);
            var remaining = TotpGenerator.SecondsRemaining();
            var color = remaining <= 5 ? AppTheme.Danger : AppTheme.Accent;
            using var track = new Pen(AppTheme.Border, 3);
            using var arc = new Pen(color, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(track, 3, 3, 23, 23);
            g.DrawArc(arc, 3, 3, 23, 23, -90, 360f * remaining / 30f);
            TextRenderer.DrawText(g, remaining.ToString(), _chipFont, new Rectangle(0, 0, 30, 30), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        box.Controls.Add(ring);
        AddBoxButton(box, IconKind.Copy, "Copy code", width - 8, () => CopyToClipboard(TotpGenerator.GenerateCode(totpSecret), "Code"));
        MakeClickToCopy(box, () => TotpGenerator.GenerateCode(totpSecret), "Code");

        UpdateTotpLabel();
        return box.Bottom + 8;
    }

    private void UpdateTotpLabel()
    {
        if (_selectedEntry?.TotpSecret is null || _totpCodeLabel is null) return;
        var code = TotpGenerator.GenerateCode(_selectedEntry.TotpSecret);
        _totpCodeLabel.Text = code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
        _totpRing?.Invalidate();
    }

    private void OpenWebsite(string url)
    {
        var candidate = url.Contains("://") ? url : "https://" + url;
        // Only ever hand http(s) links to the shell, never arbitrary commands or file paths.
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // ---------- clipboard ----------

    private void CopyToClipboard(string value, string what)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Mark the data as private so Windows keeps it out of Clipboard History (Win+V) and cloud clipboard sync.
        var data = new DataObject();
        data.SetText(value);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        try
        {
            Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 100);
        }
        catch (ExternalException)
        {
            ShowToast("Couldn't copy — another app is using the clipboard");
            return;
        }

        _pendingClipboardValue = value;
        _clipboardClearTimer.Stop();
        _clipboardClearTimer.Start();
        ShowToast($"{what} copied · clears from clipboard in {ClipboardClearSeconds / 60} min");
    }

    private void ClearClipboardIfPending()
    {
        _clipboardClearTimer.Stop();
        if (_pendingClipboardValue is null) return;
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _pendingClipboardValue)
                Clipboard.Clear();
        }
        catch (ExternalException)
        {
            // Clipboard was locked by another process; nothing to do.
        }
        _pendingClipboardValue = null;
    }

    // ---------- commands ----------

    private IEnumerable<string> AllFolders() => VaultOperations.AllFolders(_data);

    private async void OnCreateNew() => await CreateRecordAsync(null, null, useSelection: true);

    private async Task CreateRecordAsync(string? folder, string? type, bool useSelection = false)
    {
        // Pre-fill the folder (or record type) that is selected in the left panel.
        var nav = useSelection && _currentView == MainView.Vault ? _folderList.SelectedItem as NavItem : null;
        var defaultFolder = folder ?? (nav is { Kind: NavKind.Folder } ? nav.Path : null);
        var defaultType = type ?? (nav is { Kind: NavKind.RecordType } ? nav.Name : null);
        using var editForm = new EntryEditForm(AllFolders(), defaultFolder: defaultFolder, defaultType: defaultType);
        if (editForm.ShowDialog(this) != DialogResult.OK || editForm.Result is null) return;

        _data.Entries.Add(editForm.Result);
        await SaveAsync();
        if (_currentView != MainView.Vault) SwitchView(MainView.Vault);
        RefreshFolderList();
        RefreshEntryList();
        SelectEntry(editForm.Result);
    }

    private async void OnEditSelected()
    {
        if (_selectedEntry is null || _currentView == MainView.Deleted) return;
        await EditEntryAsync(_selectedEntry);
    }

    private async Task EditEntryAsync(VaultEntry entry)
    {
        using var editForm = new EntryEditForm(AllFolders(), entry);
        if (editForm.ShowDialog(this) != DialogResult.OK || editForm.Result is null) return;

        var index = _data.Entries.IndexOf(entry);
        if (index < 0) return;
        _data.Entries[index] = editForm.Result;
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
        SelectEntry(editForm.Result);
    }

    // Opens a record for editing from another page (e.g. BreachWatch "Change").
    private async void EditFromElsewhere(VaultEntry entry)
    {
        SwitchView(MainView.Vault);
        _folderList.SelectedIndex = 0;
        SelectEntry(entry);
        await EditEntryAsync(entry);
    }

    private const int FolderPlusZone = 36;

    private void ShowFolderMenu(NavItem item, Point location)
    {
        var menu = ModernMenu.Create();
        switch (item.Kind)
        {
            case NavKind.Folder:
                menu.AddItem("New record here", IconKind.Plus, async () => await CreateRecordAsync(item.Path, null));
                menu.AddItem("New subfolder…", IconKind.Folder, async () => await CreateFolderAsync(item.Path));
                menu.AddSeparator();
                menu.AddItem("Rename…", IconKind.Edit, async () => await RenameFolderAsync(item.Path));
                var delete = menu.AddItem("Delete folder", IconKind.Trash, async () => await DeleteFolderAsync(item.Path), danger: true, enabled: item.Count == 0);
                if (!delete.Enabled) delete.ToolTipText = "Move or delete its records first";
                break;
            case NavKind.Header when item.Name == "FOLDERS":
            case NavKind.All:
                menu.AddItem("New record", IconKind.Plus, async () => await CreateRecordAsync(null, null), "Ctrl+N");
                menu.AddItem("New folder…", IconKind.Folder, async () => await CreateFolderAsync(null));
                break;
            case NavKind.RecordType:
                menu.AddItem($"New {item.Name.ToLowerInvariant()} record", IconKind.Plus, async () => await CreateRecordAsync(null, item.Name));
                break;
            default:
                menu.Dispose();
                return;
        }
        menu.Show(_folderList, location);
    }

    private bool FolderExists(string path) => VaultOperations.FolderExists(_data, path);

    private static string? CheckFolderName(string name) => VaultOperations.CheckFolderName(name);

    private async Task CreateFolderAsync(string? parent)
    {
        using var prompt = new TextPromptForm(parent is null ? "New folder" : "New subfolder",
            parent is null ? "Name of the new folder." : $"Create a folder inside \"{parent}\".", actionText: "Create",
            validate: name => VaultOperations.CheckNewFolder(_data, parent, name));
        if (prompt.ShowDialog(this) != DialogResult.OK) return;

        var path = VaultOperations.CreateFolder(_data, parent, prompt.Value);
        await SaveAsync();
        if (parent is not null && !IsExpanded(parent)) SetExpanded(parent, true);
        RefreshFolderList();
        SelectFolder(path);
        ShowToast($"Folder \"{prompt.Value}\" created");
    }

    private async Task RenameFolderAsync(string path)
    {
        var cut = path.LastIndexOf('\\');
        var parent = cut < 0 ? null : path[..cut];
        var oldName = cut < 0 ? path : path[(cut + 1)..];
        using var prompt = new TextPromptForm("Rename folder", $"New name for \"{oldName}\" (records and subfolders move with it).", oldName, "Rename",
            name => CheckFolderName(name) ?? (!name.Equals(oldName, StringComparison.OrdinalIgnoreCase) && FolderExists(Join(parent, name)) ? "A folder with that name already exists." : null));
        if (prompt.ShowDialog(this) != DialogResult.OK || prompt.Value == oldName) return;

        // Moves records (including Deleted Items, so restores land correctly), subfolders and the expanded state.
        var newPath = VaultOperations.RenameFolder(_data, path, prompt.Value, AppSettings.Current.ExpandedFolders);
        AppSettings.Current.Save();

        await SaveAsync();
        RefreshFolderList();
        SelectFolder(newPath);
        ShowToast($"Renamed to \"{prompt.Value}\"");
    }

    private async Task DeleteFolderAsync(string path)
    {
        if (!VaultOperations.DeleteFolder(_data, path)) return; // only empty folders can be deleted
        await SaveAsync();
        RefreshFolderList();
        ShowToast("Folder deleted");
    }

    private static string Join(string? parent, string name) => parent is null ? name.Trim() : parent + "\\" + name.Trim();

    private void SelectFolder(string path)
    {
        var index = _folderList.Items.FindIndex(i => i is NavItem { Kind: NavKind.Folder } n && n.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _folderList.SelectedIndex = index;
    }

    private async Task ChooseFolderAndMoveAsync(VaultEntry entry)
    {
        using var picker = new FolderPickerForm(AllFolders(), entry.Folder, $"Move \"{entry.Name}\"", "Move");
        if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedFolder is not null)
            await MoveEntryAsync(entry, picker.SelectedFolder);
    }

    private async Task MoveEntryAsync(VaultEntry entry, string folder)
    {
        if (!VaultOperations.MoveEntry(entry, folder)) return;
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
        SelectEntry(entry); // stays selected if it's still in the current view (e.g. All Items)
        ShowToast($"Moved \"{entry.Name}\" to {folder}");
    }

    private void ShowRecordMenu(Point location)
    {
        if (_selectedEntry is not { } entry) return;
        var menu = ModernMenu.Create();
        if (_currentView == MainView.Deleted)
        {
            menu.AddItem("Restore", IconKind.Back, OnRestoreSelected);
            menu.AddSeparator();
            menu.AddItem("Delete forever", IconKind.Trash, OnPurgeSelected, danger: true);
        }
        else
        {
            menu.AddItem("Edit", IconKind.Edit, OnEditSelected, "Ctrl+E");
            menu.AddItem("Move to folder…", IconKind.Folder, async () => await ChooseFolderAndMoveAsync(entry));
            menu.AddItem(entry.Favorite ? "Remove from favourites" : "Add to favourites", entry.Favorite ? IconKind.StarFilled : IconKind.Star, async () =>
            {
                VaultOperations.ToggleFavorite(entry);
                await SaveAsync();
                RefreshFolderList();
                RefreshEntryList();
                SelectEntry(entry);
            });
            if (entry.Username.Length > 0 || entry.Secret.Length > 0 || CsvTransfer.HostOf(entry.Url) is not null) menu.AddSeparator();
            if (entry.Username.Length > 0) menu.AddItem("Copy username", IconKind.Person, () => CopyToClipboard(entry.Username, "Username"), "Ctrl+B");
            if (entry.Secret.Length > 0) menu.AddItem("Copy password", IconKind.Key, () => CopyToClipboard(entry.Secret, "Password"), "Ctrl+C");
            if (CsvTransfer.HostOf(entry.Url) is not null) menu.AddItem("Open website", IconKind.External, () => OpenWebsite(entry.Url));
            menu.AddSeparator();
            menu.AddItem("Delete", IconKind.Trash, OnDeleteSelected, "Del", danger: true);
        }
        menu.Show(_entryListBox, location);
    }

    // Right-click on the empty part of the record list: create something here.
    private void ShowEmptyAreaMenu(Point location)
    {
        if (_currentView != MainView.Vault) return;
        var nav = _folderList.SelectedItem as NavItem;
        var folder = nav is { Kind: NavKind.Folder } ? nav.Path : null;
        var menu = ModernMenu.Create();
        menu.AddItem(folder is null ? "New record" : $"New record in \"{LeafFolderName(folder)}\"", IconKind.Plus, OnCreateNew, "Ctrl+N");
        menu.AddItem(folder is null ? "New folder…" : "New subfolder…", IconKind.Folder, async () => await CreateFolderAsync(folder));
        menu.Show(_entryListBox, location);
    }

    private void SelectEntry(VaultEntry entry)
    {
        var index = _entryListBox.Items.IndexOf(entry);
        if (index >= 0) _entryListBox.SelectedIndex = index;
    }

    private async void OnDeleteSelected()
    {
        if (_selectedEntry is null) return;

        if (!AppDialog.Confirm(this, "Delete this record?", $"\"{_selectedEntry.Name}\" will move to Deleted Items. You can restore it from there.",
                "Move to Deleted Items", danger: true)) return;

        VaultOperations.MoveToDeletedItems(_selectedEntry);
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async void OnRestoreSelected()
    {
        if (_selectedEntry is null) return;

        VaultOperations.Restore(_selectedEntry);
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async void OnPurgeSelected()
    {
        if (_selectedEntry is null) return;

        if (!AppDialog.Confirm(this, "Delete forever?", $"\"{_selectedEntry.Name}\" will be permanently deleted. This can't be undone.",
                "Delete forever", danger: true)) return;

        VaultOperations.DeleteForever(_data, _selectedEntry);
        await SaveAsync();
        RefreshFolderList();
        RefreshEntryList();
    }

    private async Task SaveAsync()
    {
        await _store.SaveAsync(_data, _masterPassword);
        if (DesktopSync.Enabled) { _syncDebounce.Stop(); _syncDebounce.Start(); }
    }
}
