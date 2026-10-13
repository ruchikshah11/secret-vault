namespace SecretVault.Mobile;

// "Forgot password": recovery key (or fingerprint, when set up) + a new master password. Same as the desktop flow.
public sealed class ForgotPasswordPage : ContentPage
{
    public ForgotPasswordPage()
    {
        this.CanvasBackground();
        var key = Forms.Entry(placeholder: "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", keyboard: Keyboard.Plain);
        var password = Forms.Entry(password: true, placeholder: "At least 12 characters");
        var confirm = Forms.Entry(password: true, placeholder: "Type it again");
        var error = Forms.Error();
        var busy = new ActivityIndicator { Color = Ui.Primary, IsVisible = false, IsRunning = false };
        var reset = Ui.PrimaryButton("Set new master password");
        var cancel = Ui.SecondaryButton("Cancel");
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var useFingerprint = BiometricUnlock.IsEnabled;
        var keyField = Forms.Field("Recovery key", key, hint: "Spaces, dashes and upper/lower case don't matter.");
        var method = Ui.Text(useFingerprint
            ? "Confirm it's you with your fingerprint or face, then choose a new master password. (Or enter your recovery key.)"
            : "Enter the recovery key you saved when you set up the vault, then choose a new master password.", 15, sub: true);

        reset.Clicked += async (_, _) =>
        {
            var problem = NewPasswordForm.Check(password.Text, confirm.Text);
            if (problem is not null) { error.Show(problem); return; }
            var hasKey = !string.IsNullOrWhiteSpace(key.Text);
            if (hasKey && !Storage.RecoveryKey.LooksValid(key.Text!)) { error.Show("That doesn't look like a recovery key (30 letters and digits)."); return; }
            if (!hasKey && !useFingerprint) { error.Show("Enter your recovery key."); return; }

            reset.IsEnabled = false;
            busy.IsVisible = busy.IsRunning = true;
            error.IsVisible = false;
            try
            {
                if (hasKey)
                {
                    var data = await Task.Run(() => VaultSession.Store.ResetMasterPasswordAsync(key.Text!, password.Text!));
                    VaultSession.Opened(data, password.Text!);
                    BiometricUnlock.Disable();
                }
                else
                {
                    var current = await BiometricUnlock.UnlockAsync();
                    if (current is null || !await VaultSession.TryUnlockAsync(current))
                    {
                        error.Show("Fingerprint / face unlock didn't work. Use your recovery key instead.");
                        return;
                    }
                    await VaultSession.ChangeMasterPasswordAsync(password.Text!);
                }
                MobileSync.SyncSoon();
                await Navigation.PopModalAsync();
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync("Master password reset",
                    "Your new password is set. Use it on your other devices too after they sync.", "OK");
                App.ShowVault();
            }
            catch (Storage.VaultAuthenticationException)
            {
                error.Show("That recovery key doesn't open this vault.");
            }
            catch (InvalidOperationException)
            {
                error.Show("This vault has no recovery key, so the master password can't be reset with one.");
            }
            catch (Exception ex)
            {
                error.Show(ex.Message);
            }
            finally
            {
                reset.IsEnabled = true;
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
                    Ui.Text("Reset master password", 26, bold: true), method, keyField,
                    Forms.PasswordField("New master password", password), Forms.StrengthBar(password),
                    Forms.PasswordField("Confirm new password", confirm), error, busy, reset, cancel
                }
            }
        };
    }
}
