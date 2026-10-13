using System.Text;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Mobile;

// Settings: security, sync, appearance, import / export, backups — the same set as the desktop app.
public sealed class SettingsPage : AppPage
{
    public SettingsPage() : base("Settings") { }

    private static View Row(string title, string detail, View? trailing = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            Padding = new Thickness(0, 10),
            Children = { new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { Ui.Text(title, 16, bold: true), Ui.Text(detail, 13, sub: true) } } }
        };
        if (trailing is not null) grid.Children.Add(trailing.Column(1));
        return grid;
    }

    private static Button Action(string text, Func<Task> action, bool danger = false)
    {
        var button = Ui.PillButton(text);
        if (danger) button.Themed(Button.TextColorProperty, Ui.DangerLight, Ui.DangerDark);
        button.Clicked += async (_, _) => await action();
        return button;
    }

    protected override void Refresh()
    {
        if (VaultSession.Data is null) return;
        _ = BuildAsync();
    }

    private async Task BuildAsync()
    {
        var hasRecoveryKey = await VaultSession.Store.HasRecoveryKeyAsync();

        // Security
        var fingerprint = new Switch { IsToggled = BiometricUnlock.IsEnabled, IsEnabled = BiometricUnlock.IsAvailable || BiometricUnlock.IsEnabled, VerticalOptions = LayoutOptions.Center }
            .Themed(Switch.OnColorProperty, Ui.Primary, Ui.PrimaryDark);
        fingerprint.Toggled += async (_, e) => await SetFingerprintAsync(e.Value, fingerprint);
        var autoLock = new Picker { FontSize = 14, ItemsSource = AppPrefs.AutoLockChoices.Select(AppPrefs.DescribeAutoLock).ToList() }
            .Themed(Picker.TextColorProperty, Ui.Primary, Ui.PrimaryDark);
        autoLock.SelectedIndex = Math.Max(0, Array.IndexOf(AppPrefs.AutoLockChoices, AppPrefs.AutoLockMinutes));
        autoLock.SelectedIndexChanged += (_, _) => AppPrefs.AutoLockMinutes = AppPrefs.AutoLockChoices[autoLock.SelectedIndex];

        var recoveryButtons = new HorizontalStackLayout { Spacing = 6 };
        recoveryButtons.Children.Add(Action(hasRecoveryKey ? "Replace" : "Create", ReplaceRecoveryKeyAsync));
        if (hasRecoveryKey) recoveryButtons.Children.Add(Action("Remove", RemoveRecoveryKeyAsync, danger: true));
        var recoveryDetail = hasRecoveryKey
            ? "Set. Lets you reset a forgotten master password on any device with your vault."
            : "Not set up. Without it, a forgotten master password can't be reset.";

        var security = FieldRow.Section("Security", new[]
        {
            Row("Fingerprint / face unlock", BiometricUnlock.IsAvailable || BiometricUnlock.IsEnabled ? "Open the vault with your fingerprint or face." : "Set up a fingerprint or face in the phone's settings first.", fingerprint),
            Row("Auto-lock", "Lock after leaving the app for this long.", autoLock),
            Row("Recovery key", recoveryDetail, recoveryButtons),
            Row("Master password", "Re-encrypt the vault with a new master password.", Action("Change", async () => await Navigation.PushAsync(new ChangePasswordPage()))),
            Row("Clipboard & screenshots", "Copies are hidden from previews and cleared after 5 minutes. Screenshots are blocked.")
        });

        // Sync
        View sync;
        if (!MobileSync.Enabled)
        {
            sync = FieldRow.Section("Sync with your PC", new[]
            {
                Row("Not set up", "Pick the shared vault file your PC syncs with (in OneDrive or Google Drive). Records added on either device then show up on both.",
                    Action("Set up", SetUpSyncAsync))
            });
        }
        else
        {
            var buttons = new HorizontalStackLayout { Spacing = 6, Children = { Action("Change file", SetUpSyncAsync), Action("Stop", StopSyncAsync, danger: true) } };
            sync = FieldRow.Section("Sync with your PC", new[]
            {
                Row(MobileSync.FileName ?? "Shared vault file", MobileSync.Status + ". Syncs when you unlock and after each change.", Action("Sync now", SyncNowAsync)),
                Row("Sync file", "Pick another shared file, or stop syncing on this phone.", buttons)
            });
        }

        // Appearance
        var theme = new Picker { FontSize = 14, ItemsSource = new[] { "System", "Light", "Dark" } }.Themed(Picker.TextColorProperty, Ui.Primary, Ui.PrimaryDark);
        theme.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "System", "Light", "Dark" }, AppPrefs.Theme));
        theme.SelectedIndexChanged += (_, _) => AppPrefs.Theme = (string)theme.SelectedItem;
        var appearance = FieldRow.Section("Appearance", new[] { Row("Theme", "White & blue, dark, or follow the phone.", theme) });

        // Import & export
        var data = FieldRow.Section("Import & export", new[]
        {
            Row("Import passwords", "From Keeper, Chrome, Edge, Bitwarden or a SecretVault CSV export.", Action("Import CSV", ImportCsvAsync)),
            Row("Encrypted backup", "Save a copy of your encrypted vault file. It opens with your current master password.", Action("Export", ExportBackupAsync)),
            Row("Restore from file", "Replace this vault with one from an encrypted backup file.", Action("Restore", RestoreFromFileAsync)),
            Row("Export unencrypted CSV", "For moving to another password manager. The file is NOT encrypted — delete it after use.", Action("Export CSV", ExportCsvAsync, danger: true))
        });

        // Backups
        var backups = VaultSession.Store.ListBackups();
        var backupSection = FieldRow.Section("Automatic backups", new[]
        {
            Row(backups.Count == 0 ? "No backups yet" : $"{backups.Count} backup(s)",
                $"An encrypted copy of the previous vault is kept before saving (at most every {FileVaultStore.BackupInterval.TotalMinutes:0} minutes, last {FileVaultStore.BackupsToKeep}).",
                backups.Count == 0 ? null : Action("View", async () => await Navigation.PushAsync(new BackupsPage())))
        });

        // Danger zone & about
        var live = VaultSession.LiveEntries.Count();
        var danger = FieldRow.Section("This phone", new[]
        {
            Row($"{live} records · {VaultSession.Vault.PaymentCards.Count} cards", VaultSession.ImportedAt is { } at ? $"Vault on this phone since {at.LocalDateTime:d MMM yyyy}" : "Vault on this phone"),
            Row("Remove vault from this phone", "Deletes the vault, backups and fingerprint / face unlock from this phone. Your PC and the shared copy aren't affected.", Action("Remove", RemoveAsync, danger: true))
        });
        var about = FieldRow.Section("About", new[]
        {
            Row("SecretVault for Android", $"Version {AppInfo.Current.VersionString}"),
            Row("Encryption", "AES-256-GCM with an Argon2id key — the same vault format and code as the desktop app.")
        });

        Body = new ScrollView
        {
            Content = new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 40), Spacing = 14, Children = { security, sync, appearance, data, backupSection, danger, about } }
        };
    }

    // ---------- security ----------

    private async Task SetFingerprintAsync(bool on, Switch toggle)
    {
        if (on == BiometricUnlock.IsEnabled) return;
        if (!on) { BiometricUnlock.Disable(); await ToastAsync("Fingerprint / face unlock turned off"); return; }
        try
        {
            if (await BiometricUnlock.EnableAsync(VaultSession.Password)) { await ToastAsync("Fingerprint / face unlock is on"); return; }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Fingerprint / face unlock", "Couldn't turn it on: " + ex.Message, "OK");
        }
        toggle.IsToggled = false;
    }

    private async Task ReplaceRecoveryKeyAsync()
    {
        if (await VaultSession.Store.HasRecoveryKeyAsync() &&
            !await ConfirmAsync("Replace your recovery key?", "A new key will be created and the old one will stop working.", "Create new key")) return;
        await RecoveryKeyPage.CreateAndShowAsync(this);
    }

    private async Task RemoveRecoveryKeyAsync()
    {
        if (!await ConfirmAsync("Remove the recovery key?", "If you then forget your master password, the vault can't be reset.", "Remove key")) return;
        await Task.Run(() => VaultSession.Store.RemoveRecoveryKeyAsync(VaultSession.Vault, VaultSession.Password));
        MobileSync.SyncSoon();
        Refresh();
    }

    // ---------- sync ----------

    private async Task SetUpSyncAsync()
    {
        var choice = await DisplayActionSheetAsync("Sync with your PC", "Cancel", null,
            "Use the file my PC already syncs with", "Create a new shared file here");
        if (choice is null or "Cancel") return;
        var picked = await DeviceFiles.PickSyncFileAsync(create: choice.StartsWith("Create"));
        if (picked is not { } file) return;
        MobileSync.Use(file.Token, file.Name);
        var summary = await MobileSync.SyncAsync(this);
        if (summary is not null) await ToastAsync("Synced · " + summary);
        Refresh();
    }

    private async Task SyncNowAsync()
    {
        var summary = await MobileSync.SyncAsync(this);
        if (summary is not null) await ToastAsync("Synced · " + summary);
        Refresh();
    }

    private async Task StopSyncAsync()
    {
        if (!await ConfirmAsync("Stop syncing?", "This phone stops updating the shared copy. The file itself is kept.", "Stop syncing")) return;
        MobileSync.Forget();
        Refresh();
    }

    // ---------- import & export ----------

    private async Task ImportCsvAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a CSV export" });
        if (file is null) return;
        CsvTransfer.ImportResult result;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            result = CsvTransfer.Import(await reader.ReadToEndAsync());
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            await DisplayAlertAsync("Import failed", ex.Message, "OK");
            return;
        }

        var plan = VaultOperations.PlanImport(VaultSession.Vault, result);
        if (plan.Fresh.Count == 0) { await DisplayAlertAsync("Nothing new to import", "Every record in this file is already in your vault.", "OK"); return; }
        if (!await ConfirmAsync("Import passwords?", plan.Summary, $"Import {plan.Fresh.Count}")) return;

        VaultSession.Store.CreateBackupNow();
        VaultSession.Vault.Entries.AddRange(plan.Fresh);
        if (!await SaveAsync($"Imported {plan.Fresh.Count} record(s)")) return;
        await DisplayAlertAsync("Delete the CSV file", "It contains your passwords in plain text. Now that they're in your vault, delete it from your phone's Files or Downloads.", "OK");
    }

    private async Task ExportBackupAsync()
    {
        var bytes = await File.ReadAllBytesAsync(VaultSession.VaultPath);
        if (await DeviceFiles.SaveAsAsync($"SecretVault-backup-{DateTime.Now:yyyy-MM-dd}.db", "application/octet-stream", bytes))
            await ToastAsync("Encrypted backup saved");
    }

    private async Task ExportCsvAsync()
    {
        if (!await ConfirmAsync("Export without encryption?",
                "The file will contain ALL your passwords in plain text. Anyone who can read the file can read your passwords.", "Export anyway")) return;
        var csv = CsvTransfer.Export(VaultSession.LiveEntries);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        if (await DeviceFiles.SaveAsAsync("SecretVault-export.csv", "text/csv", bytes))
            await ToastAsync("CSV exported — delete it when you're done");
    }

    private async Task RestoreFromFileAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an encrypted backup" });
        if (file is null) return;
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        await BackupsPage.RestoreAsync(this, buffer.ToArray());
    }

    private async Task RemoveAsync()
    {
        if (!await ConfirmAsync("Remove the vault from this phone?",
                "The encrypted vault, its backups and fingerprint / face unlock are deleted from this phone. Your PC and the shared copy aren't affected.", "Remove")) return;
        VaultSession.RemoveFromPhone();
        App.ShowRoot(new UnlockPage());
    }
}

public sealed class ChangePasswordPage : AppPage
{
    public ChangePasswordPage() : base("Change master password", lockButton: false)
    {
        var current = Forms.Entry(password: true, placeholder: "Current master password");
        var password = Forms.Entry(password: true, placeholder: "At least 12 characters");
        var confirm = Forms.Entry(password: true, placeholder: "Type it again");
        var error = Forms.Error();
        var busy = new ActivityIndicator { Color = Ui.Primary, IsVisible = false, IsRunning = false };
        var change = Ui.PrimaryButton("Change password");
        change.Clicked += async (_, _) =>
        {
            if (current.Text != VaultSession.Password) { error.Show("The current master password isn't right."); return; }
            var problem = NewPasswordForm.Check(password.Text, confirm.Text);
            if (problem is not null) { error.Show(problem); return; }
            change.IsEnabled = false;
            busy.IsVisible = busy.IsRunning = true;
            try
            {
                await VaultSession.ChangeMasterPasswordAsync(password.Text!);
                MobileSync.SyncSoon();
                await Navigation.PopAsync();
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Master password changed",
                    "Use the new password from now on. Your recovery key still works. On other devices, enter the new password when they sync." +
                    (BiometricUnlock.IsAvailable ? " Turn fingerprint / face unlock on again in Settings." : ""), "OK");
            }
            catch (Exception ex)
            {
                error.Show(ex.Message);
            }
            finally
            {
                change.IsEnabled = true;
                busy.IsVisible = busy.IsRunning = false;
            }
        };
        Body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 40), Spacing = 14,
                Children =
                {
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            Forms.PasswordField("Current master password", current),
                            Forms.PasswordField("New master password", password), Forms.StrengthBar(password),
                            Forms.PasswordField("Confirm new password", confirm)
                        }
                    }),
                    error, busy, change
                }
            }
        };
    }
}

public sealed class BackupsPage : AppPage
{
    public BackupsPage() : base("Backups", lockButton: false) { }

    protected override void Refresh()
    {
        var stack = new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 40), Spacing = 10 };
        stack.Children.Add(Ui.Text("Each backup opens with the master password in use when it was made. Tap one to restore it.", 13, sub: true));
        foreach (var backup in VaultSession.Store.ListBackups())
        {
            var row = Ui.Card(new VerticalStackLayout
            {
                Spacing = 2,
                Children = { Ui.Text(backup.LastWriteTime.ToString("d MMM yyyy, HH:mm:ss"), 16, bold: true), Ui.Text($"{backup.Length / 1024.0:0.#} KB", 12, sub: true) }
            }, padding: 14);
            row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await RestoreAsync(this, await File.ReadAllBytesAsync(backup.FullName))) });
            stack.Children.Add(row);
        }
        Body = new ScrollView { Content = stack };
    }

    // Same flow as the desktop: open with the current password (or ask for the backup's), confirm, replace, save.
    public static async Task RestoreAsync(Page page, byte[] bytes)
    {
        VaultData restored;
        try
        {
            restored = await Task.Run(() => FileVaultStore.LoadFromBytes(bytes, VaultSession.Password));
        }
        catch (VaultAuthenticationException)
        {
            var password = await PasswordPromptPage.AskAsync(page, "Backup password", "This backup was saved with a different master password. Enter that password to open it.");
            if (password is null) return;
            try { restored = await Task.Run(() => FileVaultStore.LoadFromBytes(bytes, password)); }
            catch (VaultAuthenticationException)
            {
                await page.DisplayAlertAsync("Wrong password", "That password doesn't open this backup.", "OK");
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
        {
            await page.DisplayAlertAsync("Can't restore this file", "It isn't a readable SecretVault backup.", "OK");
            return;
        }

        var summary = $"Replace your current vault ({VaultSession.LiveEntries.Count()} records) with this backup ({VaultOperations.Live(restored).Count()} records)?\n\nYour current vault is backed up automatically first.";
        if (!await page.DisplayAlertAsync("Restore this backup?", summary, "Restore", "Cancel")) return;
        VaultSession.Store.CreateBackupNow();
        VaultOperations.ReplaceContents(VaultSession.Vault, restored);
        await VaultSession.SaveAsync();
        VaultSession.NotifyChanged();
        await page.DisplayAlertAsync("Backup restored", "Your vault now contains the backup's records.", "OK");
    }
}
