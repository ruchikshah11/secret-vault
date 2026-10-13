namespace SecretVault.Mobile;

// Brings the vault from the PC onto the phone: Android's file picker (phone storage, Google Drive, OneDrive…),
// then offers to keep syncing with that file.
public static class VaultFileFlow
{
    public static async Task<bool> OpenSharedVaultAsync(Page page)
    {
        var picked = await DeviceFiles.PickSyncFileAsync(create: false);
        if (picked is not { } file) return false;
        var bytes = await DeviceFiles.ReadAsync(file.Token) ?? throw new IOException("The file couldn't be read.");
        VaultSession.ImportBytes(bytes);
        var sync = await page.DisplayAlertAsync("Keep in sync?",
            $"Sync this phone with \"{file.Name}\"? Changes made here and on the PC are then merged automatically.", "Keep in sync", "Just copy it");
        if (sync) MobileSync.Use(file.Token, file.Name);
        else MobileSync.Forget();
        return true;
    }
}

internal static class ViewExtensions
{
    public static T Column<T>(this T view, int column) where T : BindableObject { Grid.SetColumn(view, column); return view; }
    public static T Row<T>(this T view, int row) where T : BindableObject { Grid.SetRow(view, row); return view; }
    public static T ColumnSpan<T>(this T view, int span) where T : BindableObject { Grid.SetColumnSpan(view, span); return view; }

    public static Label Center(this Label label)
    {
        label.HorizontalTextAlignment = TextAlignment.Center;
        return label;
    }
}

// New vault on the phone: choose a master password, then (recommended) create a recovery key.
public sealed class CreateVaultPage : ContentPage
{
    public CreateVaultPage()
    {
        this.CanvasBackground();
        var password = Forms.Entry(password: true, placeholder: "At least 12 characters");
        var confirm = Forms.Entry(password: true, placeholder: "Type it again");
        var error = Forms.Error();
        var busy = new ActivityIndicator { Color = Ui.Primary, IsVisible = false, IsRunning = false };
        var create = Ui.PrimaryButton("Create vault");
        var cancel = Ui.SecondaryButton("Back");
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();

        create.Clicked += async (_, _) =>
        {
            var problem = NewPasswordForm.Check(password.Text, confirm.Text);
            if (problem is not null) { error.Show(problem); return; }
            if (VaultSession.HasVault && !await DisplayAlertAsync("Replace the vault on this phone?",
                    "There is already a vault on this phone. Creating a new one replaces it (a backup is kept).", "Replace", "Cancel")) return;
            create.IsEnabled = false;
            busy.IsVisible = busy.IsRunning = true;
            try
            {
                if (VaultSession.HasVault) VaultSession.Store.CreateBackupNow();
                if (File.Exists(VaultSession.VaultPath)) File.Delete(VaultSession.VaultPath);
                MobileSync.Forget();
                BiometricUnlock.Disable();
                await VaultSession.CreateAsync(password.Text!);
                // Open the vault first; this page is gone after that, so the question is asked from the new root page.
                App.ShowVault();
                var root = Application.Current!.Windows[0].Page!;
                await Task.Delay(400);
                if (await root.DisplayAlertAsync("Create a recovery key?", "If you ever forget your master password, the recovery key is the only way to set a new one. Recommended.", "Create recovery key", "Not now"))
                    await RecoveryKeyPage.CreateAndShowAsync(root);
            }
            catch (Exception ex)
            {
                error.Show(ex.Message);
            }
            finally
            {
                create.IsEnabled = true;
                busy.IsVisible = busy.IsRunning = false;
            }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 40, 24, 24), Spacing = 16,
                Children =
                {
                    Ui.Text("Create your vault", 26, bold: true),
                    Ui.Text("Choose a master password. It's the only key to your vault — SecretVault can't recover it for you.", 15, sub: true),
                    Forms.PasswordField("Master password", password),
                    Forms.StrengthBar(password),
                    Forms.PasswordField("Confirm master password", confirm),
                    error, busy, create, cancel
                }
            }
        };
    }
}

public static class NewPasswordForm
{
    public const int MinLength = 12;

    // Same rules as the desktop app.
    public static string? Check(string? password, string? confirm) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength ? $"Use at least {MinLength} characters."
        : password != confirm ? "The passwords don't match."
        : null;
}
