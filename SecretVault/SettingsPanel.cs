using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

// Settings page: security, appearance, import/export, backups and shortcut reference.
public class SettingsPanel : Panel
{
    private const int Pad = 28;
    private const int CardPad = 22;
    private const int MaxWidth = 820;

    private readonly VaultData _data;
    private readonly FileVaultStore _store;
    private readonly Func<string> _masterPassword;
    private readonly Func<string, Task> _changeMasterPassword;
    private readonly Func<Task> _save;
    private readonly Action _dataChanged;
    private readonly Action<string> _toast;
    private readonly Func<Task> _syncNow;
    private int _builtWidth = -1;

    public SettingsPanel(VaultData data, FileVaultStore store, Func<string> masterPassword, Func<string, Task> changeMasterPassword,
        Func<Task> save, Action dataChanged, Action<string> toast, Func<Task> syncNow)
    {
        _syncNow = syncNow;
        _data = data;
        _store = store;
        _masterPassword = masterPassword;
        _changeMasterPassword = changeMasterPassword;
        _save = save;
        _dataChanged = dataChanged;
        _toast = toast;
        Tag = AppTheme.NoTheme;
        AutoScroll = true;
        Resize += (_, _) => { if (Visible && ClientSize.Width != _builtWidth) RefreshView(); };
    }

    private AppSettings Settings => AppSettings.Current;

    public void RefreshView()
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
        AutoScrollPosition = Point.Empty;
        BackColor = AppTheme.Canvas;
        _builtWidth = ClientSize.Width;

        var width = Math.Clamp(ClientSize.Width - Pad * 2, 520, MaxWidth);
        var y = Pad - 4;
        Controls.Add(new Label { Text = "Settings", Font = new Font("Segoe UI", 17f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad - 3, y), Tag = AppTheme.NoTheme });
        Controls.Add(new Label { Text = "Security, appearance, import / export and backups.", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(Pad - 1, y + 36), Tag = AppTheme.NoTheme });
        y += 76;

        y = BuildSecurity(width, y) + 16;
        y = BuildAppearance(width, y) + 16;
        y = BuildSync(width, y) + 16;
        y = BuildImportExport(width, y) + 16;
        y = BuildBackups(width, y) + 16;
        y = BuildShortcuts(width, y);
        Controls.Add(new Panel { Location = new Point(Pad, y), Size = new Size(1, Pad), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas });

        ResumeLayout();
    }

    // ---------- sections ----------

    private int BuildSecurity(int width, int y)
    {
        var card = Section("Security", IconKind.Lock, width, y, out var cy);

        var lockChoices = new (int Minutes, string Label)[] { (1, "1 minute"), (5, "5 minutes"), (15, "15 minutes"), (30, "30 minutes"), (60, "1 hour"), (AppSettings.NeverLock, "Never") };
        var current = lockChoices.FirstOrDefault(c => c.Minutes == Settings.AutoLockMinutes, (Minutes: Settings.AutoLockMinutes, Label: $"{Settings.AutoLockMinutes} minutes"));
        var lockButton = new PillButton(current.Label, primary: false, new Size(150, 34)) { Dropdown = true };
        var menu = ModernMenu.Create(showIcons: false, disposeOnClose: false);
        foreach (var choice in lockChoices)
            menu.Items.Add(choice.Label, null, (_, _) =>
            {
                Settings.AutoLockMinutes = choice.Minutes;
                Settings.Save();
                lockButton.Text = choice.Label;
            });
        lockButton.Click += (_, _) => menu.Show(lockButton, new Point(0, lockButton.Height + 2));
        cy = Row(card, "Auto-lock", "Lock the vault after this much time without keyboard or mouse activity.", lockButton, cy);

        var winLock = new ToggleSwitch { Checked = Settings.LockOnWindowsLock };
        winLock.CheckedChanged += (_, _) => { Settings.LockOnWindowsLock = winLock.Checked; Settings.Save(); };
        cy = Row(card, "Lock with Windows", "Lock the vault when you lock your PC (Win + L) or switch user.", winLock, cy);

        var hello = new ToggleSwitch { Checked = WindowsHello.IsEnabled, Enabled = false };
        var helloRowTop = cy;
        cy = Row(card, "Windows Hello", "Unlock with your face, fingerprint or Windows Hello PIN.", hello, cy);
        var suppress = false;
        hello.CheckedChanged += async (_, _) =>
        {
            if (suppress) return;
            if (hello.Checked)
            {
                var outcome = await WindowsHello.EnableAsync(_masterPassword());
                if (outcome != WindowsHello.Outcome.Success)
                {
                    suppress = true;
                    hello.Checked = false;
                    suppress = false;
                    if (outcome != WindowsHello.Outcome.Canceled)
                        AppDialog.Show(FindForm(), "Windows Hello isn't ready",
                            "Make sure face, fingerprint or a PIN is set up in Windows Settings › Accounts › Sign-in options, then try again.", AppDialog.Kind.Warning);
                    return;
                }
                _toast("Windows Hello unlock turned on");
            }
            else
            {
                await WindowsHello.DisableAsync();
                _toast("Windows Hello unlock turned off");
            }
        };
        _ = EnableHelloToggleAsync(card, hello, helloRowTop);

        var winPass = new ToggleSwitch { Checked = WindowsPasswordUnlock.IsEnabled };
        cy = Row(card, "Windows password", $"Unlock with the password of your Windows account ({WindowsPasswordUnlock.CurrentAccount}).", winPass, cy);
        var suppressWinPass = false;
        winPass.CheckedChanged += async (_, _) =>
        {
            if (suppressWinPass) return;
            if (winPass.Checked)
            {
                var outcome = await WindowsPasswordUnlock.EnableAsync(FindForm()!, _masterPassword());
                if (outcome != WindowsPasswordUnlock.Outcome.Success)
                {
                    suppressWinPass = true;
                    winPass.Checked = false;
                    suppressWinPass = false;
                    return;
                }
                _toast("Windows password unlock turned on");
            }
            else
            {
                WindowsPasswordUnlock.Disable();
                _toast("Windows password unlock turned off");
            }
        };

        // Recovery key: buttons depend on whether one exists (read from the vault file asynchronously).
        var recoveryButtons = new Panel { Size = new Size(210, 34), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        var recoveryRowTop = cy;
        cy = Row(card, "Recovery key", "Lets you set a new master password if you forget it — on this or any PC with your vault file.", recoveryButtons, cy);
        _ = FillRecoveryButtonsAsync(card, recoveryButtons, recoveryRowTop);

        var change = new PillButton("Change…", primary: false, new Size(110, 34)) { LeadingIcon = IconKind.Key };
        change.Click += async (_, _) =>
        {
            using var form = new ChangePasswordForm(_masterPassword());
            if (form.ShowDialog(FindForm()) != DialogResult.OK || form.NewPassword is null) return;
            await _changeMasterPassword(form.NewPassword);
            _toast("Master password changed");
        };
        cy = Row(card, "Master password", "Re-encrypt the vault with a new master password.", change, cy, last: true);

        card.Height = cy + CardPad - 8;
        return card.Bottom;
    }

    private int BuildAppearance(int width, int y)
    {
        var card = Section("Appearance", IconKind.Eye, width, y, out var cy);

        var light = new PillButton("Light", primary: false, new Size(80, 34)) { Selected = AppTheme.Mode == ThemeMode.Light };
        var dark = new PillButton("Dark", primary: false, new Size(80, 34)) { Selected = AppTheme.Mode == ThemeMode.Dark };
        light.Click += (_, _) => { if (AppTheme.Mode != ThemeMode.Light) AppTheme.SetMode(ThemeMode.Light); };
        dark.Click += (_, _) => { if (AppTheme.Mode != ThemeMode.Dark) AppTheme.SetMode(ThemeMode.Dark); };
        var pair = new Panel { Size = new Size(168, 34), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        dark.Location = new Point(88, 0);
        pair.Controls.Add(light);
        pair.Controls.Add(dark);
        cy = Row(card, "Theme", "White & blue (Light) or Dark.", pair, cy);

        var icons = new ToggleSwitch { Checked = Settings.ShowWebsiteIcons };
        icons.CheckedChanged += (_, _) => { Settings.ShowWebsiteIcons = icons.Checked; Settings.Save(); _dataChanged(); };
        cy = Row(card, "Website icons", "Show each site's icon. Icons are fetched directly from the website and kept only in memory.", icons, cy, last: true);

        card.Height = cy + CardPad - 8;
        return card.Bottom;
    }

    public void RefreshSyncStatus()
    {
        if (Visible) RefreshView();
    }

    private int BuildSync(int width, int y)
    {
        var card = Section("Sync with your phone", IconKind.History, width, y, out var cy);
        var path = DesktopSync.SharedPath;

        if (path is null)
        {
            var choose = new PillButton("Set up sync…", primary: true, new Size(150, 34));
            choose.Click += async (_, _) => await ChooseSyncFileAsync();
            cy = Row(card, "Not set up",
                "Share an encrypted copy via OneDrive or Google Drive, so the Android app shows the same records.",
                choose, cy, last: true);
        }
        else
        {
            var status = Settings.LastSyncError is { } error ? "Last sync failed: " + error
                : Settings.LastSyncUtc is { } at ? $"Last synced {at.ToLocalTime():d MMM yyyy, HH:mm}" : "Not synced yet";
            var syncNow = new PillButton("Sync now", primary: true, new Size(120, 34));
            syncNow.Click += async (_, _) => await _syncNow();
            cy = Row(card, "Shared copy: " + Path.GetFileName(path), $"{status}. Syncs on open, after changes and every 5 minutes.", syncNow, cy);
            new ToolTip().SetToolTip(syncNow, path);

            var change = new PillButton("Change…", primary: false, new Size(110, 34));
            change.Click += async (_, _) => await ChooseSyncFileAsync();
            var stop = new PillButton("Stop", primary: false, new Size(84, 34)) { AccentColor = AppTheme.Danger };
            stop.Click += (_, _) =>
            {
                if (!AppDialog.Confirm(FindForm(), "Stop syncing?", "This PC stops updating the shared copy. The file itself is kept, and your phone keeps its own copy.", "Stop syncing")) return;
                Settings.SyncFilePath = null;
                Settings.LastSyncError = null;
                Settings.Save();
                RefreshView();
            };
            var buttons = new Panel { Size = new Size(change.Width + 8 + stop.Width, 34), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
            change.Location = Point.Empty;
            stop.Location = new Point(change.Width + 8, 0);
            buttons.Controls.Add(change);
            buttons.Controls.Add(stop);
            cy = Row(card, "Sync file", Path.GetDirectoryName(path) ?? "", buttons, cy, last: true);
        }

        card.Height = cy + CardPad - 8;
        return card.Bottom;
    }

    private async Task ChooseSyncFileAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Choose or create the shared vault file",
            Filter = "SecretVault vault (*.db)|*.db",
            FileName = "SecretVault-sync.db",
            InitialDirectory = DesktopSync.SuggestedFolder(),
            OverwritePrompt = false,
            CheckFileExists = false
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        if (File.Exists(dialog.FileName))
        {
            var header = new byte[5];
            await using (var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                _ = await stream.ReadAsync(header);
            if (!FileVaultStore.LooksLikeVault(header))
            {
                AppDialog.Show(FindForm(), "Not a vault file", "Choose an existing SecretVault sync file, or type a new file name to create one.", AppDialog.Kind.Warning);
                return;
            }
        }

        Settings.SyncFilePath = dialog.FileName;
        Settings.LastSyncError = null;
        Settings.Save();
        await _syncNow();
        RefreshView();
    }

    private int BuildImportExport(int width, int y)
    {
        var card = Section("Import & export", IconKind.Download, width, y, out var cy);

        var import = new PillButton("Import CSV…", primary: false, new Size(140, 34)) { LeadingIcon = IconKind.Download };
        import.Click += async (_, _) => await ImportCsvAsync();
        cy = Row(card, "Import passwords", "From Keeper, Chrome, Edge, Bitwarden or a SecretVault CSV export.", import, cy);

        var backup = new PillButton("Export backup…", primary: false, new Size(150, 34)) { LeadingIcon = IconKind.Upload };
        backup.Click += (_, _) => ExportEncrypted();
        cy = Row(card, "Encrypted backup", "Save a copy of your encrypted vault file. It opens with your current master password.", backup, cy);

        var restore = new PillButton("Restore…", primary: false, new Size(120, 34)) { LeadingIcon = IconKind.History };
        restore.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Restore vault backup", Filter = "SecretVault backup (*.db)|*.db|All files (*.*)|*.*" };
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) await RestoreAsync(dialog.FileName);
        };
        cy = Row(card, "Restore from file", "Replace this vault with one from an encrypted backup file.", restore, cy);

        var csv = new PillButton("Export CSV…", primary: false, new Size(140, 34)) { LeadingIcon = IconKind.Upload, AccentColor = AppTheme.Danger };
        csv.Click += (_, _) => ExportCsv();
        cy = Row(card, "Export unencrypted CSV", "For moving to another password manager. The file is NOT encrypted — delete it after use.", csv, cy, last: true);

        card.Height = cy + CardPad - 8;
        return card.Bottom;
    }

    private int BuildBackups(int width, int y)
    {
        var card = Section("Automatic backups", IconKind.History, width, y, out var cy);
        var inner = width - CardPad * 2;

        card.Controls.Add(new Label
        {
            Text = $"Before saving, SecretVault keeps an encrypted copy of the previous vault (at most every {FileVaultStore.BackupInterval.TotalMinutes:0} minutes, last {FileVaultStore.BackupsToKeep} kept). Each backup opens with the master password in use when it was made.",
            ForeColor = AppTheme.SubText, Location = new Point(CardPad, cy), Size = new Size(inner, 40), Tag = AppTheme.NoTheme
        });
        cy += 50;

        var backups = _store.ListBackups();
        if (backups.Count == 0)
        {
            card.Controls.Add(new Label { Text = "No backups yet — one is made the next time you save a change.", ForeColor = AppTheme.SubText, Font = new Font("Segoe UI", 9f, FontStyle.Italic), AutoSize = true, Location = new Point(CardPad, cy), Tag = AppTheme.NoTheme });
            cy += 30;
        }

        foreach (var file in backups.Take(5))
        {
            var row = new Panel { Location = new Point(CardPad, cy), Size = new Size(inner, 48), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
            row.Paint += (_, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1); };
            row.Controls.Add(new Label { Text = file.LastWriteTime.ToString("ddd, MMM d yyyy  ·  h:mm tt"), ForeColor = AppTheme.Text, Font = new Font("Segoe UI Semibold", 9.5f), AutoSize = true, Location = new Point(0, 6), Tag = AppTheme.NoTheme });
            row.Controls.Add(new Label { Text = $"{file.Length / 1024.0:0.#} KB", ForeColor = AppTheme.SubText, Font = new Font("Segoe UI", 8.5f), AutoSize = true, Location = new Point(0, 26), Tag = AppTheme.NoTheme });
            var restore = new PillButton("Restore", primary: false, new Size(90, 30)) { Location = new Point(inner - 90, 9) };
            var path = file.FullName;
            restore.Click += async (_, _) => await RestoreAsync(path);
            row.Controls.Add(restore);
            card.Controls.Add(row);
            cy += 50;
        }

        var open = new PillButton("Open backups folder", primary: false, new Size(180, 32)) { LeadingIcon = IconKind.Folder, Location = new Point(CardPad, cy + 6) };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(_store.BackupDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_store.BackupDirectory}\"") { UseShellExecute = true });
        };
        card.Controls.Add(open);
        cy = open.Bottom;

        card.Height = cy + CardPad;
        return card.Bottom;
    }

    private int BuildShortcuts(int width, int y)
    {
        var card = Section("Keyboard shortcuts", IconKind.Key, width, y, out var cy);
        var shortcuts = new (string Keys, string Action)[]
        {
            ("Ctrl + F", "Search"), ("Ctrl + N", "New record"), ("Ctrl + E", "Edit selected record"),
            ("Ctrl + C", "Copy password of selected record"), ("Ctrl + B", "Copy username of selected record"),
            ("Ctrl + L", "Lock vault"), ("Delete", "Move selected record to Deleted Items"), ("Esc", "Clear search"),
        };
        var colWidth = (width - CardPad * 2) / 2;
        for (var i = 0; i < shortcuts.Length; i++)
        {
            var x = CardPad + (i % 2) * colWidth;
            var rowY = cy + (i / 2) * 34;
            var (keys, action) = shortcuts[i];
            var chip = new Panel { Location = new Point(x, rowY), Size = new Size(84, 24), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
            var font = new Font("Segoe UI Semibold", 8.25f);
            chip.Paint += (_, e) => { e.Graphics.Clear(AppTheme.CardBg); UiKit.DrawChip(e.Graphics, Point.Empty, keys, font, AppTheme.Accent, UiKit.Tint(AppTheme.Accent, AppTheme.CardBg)); };
            card.Controls.Add(chip);
            card.Controls.Add(new Label { Text = action, ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(x + 92, rowY + 3), Tag = AppTheme.NoTheme });
        }
        cy += (shortcuts.Length + 1) / 2 * 34;
        card.Height = cy + CardPad - 8;
        return card.Bottom;
    }

    private async Task FillRecoveryButtonsAsync(Panel card, Panel host, int rowTop)
    {
        var hasKey = await _store.HasRecoveryKeyAsync();
        if (host.IsDisposed) return;
        host.Controls.Clear();

        var description = card.Controls.OfType<Label>().FirstOrDefault(l => l.Top == rowTop + 22);
        if (description is not null)
            description.Text = hasKey
                ? "Set up. Use it on the login screen (\"Forgot password?\") if you forget your master password."
                : "Not set up. Without it, a forgotten master password can't be reset.";
        if (description is not null && !hasKey) description.ForeColor = AppTheme.StrengthColor(Crypto.StrengthLevel.Weak);

        async Task CreateAsync()
        {
            if (hasKey && !AppDialog.Confirm(FindForm(), "Replace your recovery key?", "A new key will be created and the old one will stop working.",
                    "Create new key", kind: AppDialog.Kind.Warning)) return;
            var key = await _store.CreateRecoveryKeyAsync(_data, _masterPassword());
            using (var form = new RecoveryKeyForm(key)) form.ShowDialog(FindForm());
            _toast(hasKey ? "New recovery key created" : "Recovery key created");
            _dataChanged();
            RefreshView();
        }

        if (!hasKey)
        {
            var create = new PillButton("Create…", primary: true, new Size(110, 34)) { LeadingIcon = IconKind.Key, Location = new Point(host.Width - 110, 0) };
            create.Click += async (_, _) => await CreateAsync();
            host.Controls.Add(create);
            return;
        }

        var replace = new PillButton("Replace…", primary: false, new Size(104, 34)) { LeadingIcon = IconKind.Key, Location = new Point(host.Width - 104 - 8 - 96, 0) };
        replace.Click += async (_, _) => await CreateAsync();
        var remove = new PillButton("Remove", primary: false, new Size(96, 34)) { AccentColor = AppTheme.Danger, LeadingIcon = IconKind.Trash, Location = new Point(host.Width - 96, 0) };
        remove.Click += async (_, _) =>
        {
            if (!AppDialog.Confirm(FindForm(), "Remove the recovery key?", "If you then forget your master password, the vault can't be reset.",
                    "Remove key", danger: true)) return;
            await _store.RemoveRecoveryKeyAsync(_data, _masterPassword());
            _toast("Recovery key removed");
            _dataChanged();
            RefreshView();
        };
        host.Controls.Add(replace);
        host.Controls.Add(remove);
    }

    // Windows Hello availability is async; enable the switch (or explain how to set Hello up) once known.
    private static async Task EnableHelloToggleAsync(Panel card, ToggleSwitch toggle, int rowTop)
    {
        var available = await WindowsHello.IsAvailableAsync();
        if (toggle.IsDisposed) return;
        toggle.Enabled = available || toggle.Checked;
        if (available) return;
        var description = card.Controls.OfType<Label>().FirstOrDefault(l => l.Top == rowTop + 22);
        if (description is not null)
            description.Text = "Not set up on this PC. Add a face, fingerprint or PIN in Windows Settings › Accounts › Sign-in options.";
    }

    // ---------- building blocks ----------

    private Panel Section(string title, IconKind icon, int width, int y, out int contentY)
    {
        var card = UiKit.NewCard();
        card.SetBounds(Pad, y, width, 100);
        var badge = new Panel { Size = new Size(30, 30), Location = new Point(CardPad, CardPad - 2), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        badge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            var tint = UiKit.Tint(AppTheme.Accent, AppTheme.CardBg);
            using var path = RoundedField.RoundRect(new RectangleF(0, 0, 29, 29), 8);
            using var b = new SolidBrush(tint);
            g.FillPath(b, path);
            Icons.Draw(g, icon, new Rectangle(8, 8, 14, 14), AppTheme.Accent, 1.6f, tint);
        };
        card.Controls.Add(badge);
        card.Controls.Add(new Label { Text = title, UseMnemonic = false, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(CardPad + 40, CardPad + 1), Tag = AppTheme.NoTheme });
        Controls.Add(card);
        contentY = CardPad + 46;
        return card;
    }

    private static int Row(Panel card, string title, string description, Control control, int y, bool last = false)
    {
        var textWidth = card.Width - CardPad * 2 - control.Width - 24;
        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 10f), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(CardPad, y), Tag = AppTheme.NoTheme });
        var desc = new Label { Text = description, UseMnemonic = false, ForeColor = AppTheme.SubText, Location = new Point(CardPad, y + 22), Size = new Size(textWidth, 36), Tag = AppTheme.NoTheme };
        card.Controls.Add(desc);
        control.Location = new Point(card.Width - CardPad - control.Width, y + (52 - control.Height) / 2);
        card.Controls.Add(control);
        if (!last)
            card.Controls.Add(new Panel { Location = new Point(CardPad, y + 62), Size = new Size(card.Width - CardPad * 2, 1), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border });
        return y + 74;
    }

    // ---------- actions ----------

    private async Task ImportCsvAsync()
    {
        using var dialog = new OpenFileDialog { Title = "Import passwords", Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        CsvTransfer.ImportResult result;
        try
        {
            // Shared read so the import also works while the CSV is open in Excel.
            await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            result = CsvTransfer.Import(await reader.ReadToEndAsync());
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            AppDialog.Show(FindForm(), "Import failed", ex.Message, AppDialog.Kind.Warning);
            return;
        }

        // Skip exact duplicates of records already in the vault (shared with the Android app).
        var plan = VaultOperations.PlanImport(_data, result);
        var fresh = plan.Fresh;

        if (fresh.Count == 0)
        {
            AppDialog.Show(FindForm(), "Nothing new to import", "Every record in this file is already in your vault.");
            return;
        }

        if (!AppDialog.Confirm(FindForm(), "Import passwords?", plan.Summary, $"Import {fresh.Count}")) return;

        _store.CreateBackupNow();
        _data.Entries.AddRange(fresh);
        await _save();
        _dataChanged();
        _toast($"Imported {fresh.Count} record(s)");

        if (AppDialog.Confirm(FindForm(), "Delete the CSV file?", "It contains your passwords in plain text. Now that they're safely in your vault, it's best to delete it.",
                "Delete file", "Keep it", danger: true))
        {
            try { File.Delete(dialog.FileName); }
            catch (IOException) { }
        }
    }

    private void ExportEncrypted()
    {
        using var dialog = new SaveFileDialog { Title = "Export encrypted backup", Filter = "SecretVault backup (*.db)|*.db", FileName = $"SecretVault-backup-{DateTime.Now:yyyy-MM-dd}.db" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _store.ExportEncryptedCopy(dialog.FileName);
        _toast("Encrypted backup saved");
    }

    private void ExportCsv()
    {
        if (!AppDialog.Confirm(FindForm(), "Export without encryption?",
                "The file will contain ALL your passwords in plain text. Anyone who can read the file can read your passwords.",
                "Export anyway", danger: true)) return;

        using var dialog = new SaveFileDialog { Title = "Export CSV", Filter = "CSV files (*.csv)|*.csv", FileName = "SecretVault-export.csv" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, CsvTransfer.Export(_data.Entries.Where(e => e.DeletedAt is null)), new System.Text.UTF8Encoding(true));
        _toast("CSV exported — delete it when you're done");
    }

    private async Task RestoreAsync(string path)
    {
        VaultData restored;
        var password = _masterPassword();
        try
        {
            restored = await FileVaultStore.LoadFromAsync(path, password);
        }
        catch (VaultAuthenticationException)
        {
            using var prompt = new PasswordPromptForm("Backup password", "This backup was saved with a different master password. Enter that password to open it.");
            if (prompt.ShowDialog(FindForm()) != DialogResult.OK) return;
            try
            {
                restored = await FileVaultStore.LoadFromAsync(path, prompt.Password);
            }
            catch (VaultAuthenticationException)
            {
                AppDialog.Show(FindForm(), "Wrong password", "That password doesn't open this backup.", AppDialog.Kind.Warning);
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
        {
            AppDialog.Show(FindForm(), "Can't restore this file", "It isn't a readable SecretVault backup.", AppDialog.Kind.Warning);
            return;
        }

        var summary = $"Replace your current vault ({_data.Entries.Count(e => e.DeletedAt is null)} records) with this backup ({restored.Entries.Count(e => e.DeletedAt is null)} records)?\n\nYour current vault is backed up automatically first.";
        if (!AppDialog.Confirm(FindForm(), "Restore this backup?", summary, "Restore", kind: AppDialog.Kind.Warning)) return;

        _store.CreateBackupNow();
        VaultOperations.ReplaceContents(_data, restored);
        await _save(); // re-encrypts with the current master password
        _dataChanged();
        _toast("Backup restored");
        RefreshView();
    }
}
