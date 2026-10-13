using Microsoft.Maui.Controls.Shapes;

namespace SecretVault.Mobile;

// The login page — the app always opens here, like the desktop app. Same design as the desktop login, stacked
// for a phone: blue brand panel on top, "Welcome back" + master password below. When the phone has no vault yet,
// the same page offers to bring the vault over from the PC (or create a new one).
public sealed class UnlockPage : ContentPage
{
    // Set by Settings › Fingerprint unlock: the next successful password unlock turns it on.
    public const string EnrollPendingKey = "bio_enroll_pending";

    private static readonly string[] Footers =
    {
        "🔒 Only you can open your vault — not even us.",
        "Encrypted on this device. Your master password never leaves it."
    };

    private readonly Entry _password = Forms.Entry(password: true, placeholder: "");
    private readonly Button _unlock = Ui.PrimaryButton("Unlock");
    private readonly Button _fingerprint = Ui.SecondaryButton("Unlock with fingerprint or face");
    private readonly Label _error = Forms.Error();
    private readonly ActivityIndicator _busy = new() { IsRunning = false, IsVisible = false, Color = Ui.Primary, HeightRequest = 28 };
    private readonly string? _message;
    private bool _promptedFingerprint;

    public UnlockPage(string? message = null)
    {
        this.Themed(BackgroundColorProperty, Ui.CardLight, Ui.CardDark);
        _message = message ?? (Preferences.Get(EnrollPendingKey, false) ? "Enter your master password once to turn on fingerprint / face unlock." : null);
        _error.HorizontalTextAlignment = TextAlignment.Center;

        _password.ReturnType = ReturnType.Go;
        _password.Completed += async (_, _) => await UnlockWithPasswordAsync();
        _unlock.Clicked += async (_, _) => await UnlockWithPasswordAsync();
        _fingerprint.Clicked += async (_, _) => await UnlockWithFingerprintAsync();
        _unlock.Background = new LinearGradientBrush(new GradientStopCollection { new(Ui.Primary, 0), new(Color.FromArgb("#4F46E5"), 1) }, new Point(0, 0), new Point(1, 0));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children = { BrandPanel(), VaultSession.HasVault ? LoginForm() : SetUpForm() }
            }
        };
    }

    // Same as the desktop's left panel: blue gradient, logo, name, tagline.
    private static View BrandPanel() => new Border
    {
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, 28, 28) },
        Padding = new Thickness(24, 56, 24, 36),
        Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb("#2F6FEB"), 0), new(Ui.Navy, 1) }, new Point(0, 0), new Point(1, 1)),
        Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Border
                {
                    WidthRequest = 96, HeightRequest = 96, StrokeThickness = 0, HorizontalOptions = LayoutOptions.Center,
                    StrokeShape = new RoundRectangle { CornerRadius = 24 }, BackgroundColor = Colors.White.WithAlpha(0.14f),
                    Content = Ui.Logo(64)
                },
                new Label { Text = "SecretVault", TextColor = Colors.White, FontSize = 30, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Your passwords, locked down.", TextColor = Colors.White.WithAlpha(0.85f), FontSize = 16, HorizontalTextAlignment = TextAlignment.Center }
            }
        }
    };

    private View LoginForm()
    {
        _fingerprint.IsVisible = BiometricUnlock.IsEnabled;
        var forgot = Link("Forgot password?", async () => await Navigation.PushModalAsync(new ForgotPasswordPage()));
        forgot.HorizontalOptions = LayoutOptions.End;
        forgot.Padding = 0;
        var subtitle = Ui.Text(_message ?? "Enter your master password to unlock your vault.", 15, sub: true);
        if (_message is not null) subtitle.Themed(Label.TextColorProperty, Ui.Primary, Ui.PrimaryDark);

        var orRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12, IsVisible = BiometricUnlock.IsEnabled,
            Children =
            {
                new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Center }.Themed(BoxView.ColorProperty, Ui.BorderLight, Ui.BorderDark),
                Ui.Text("or", 13, sub: true).Column(1),
                new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Center }.Themed(BoxView.ColorProperty, Ui.BorderLight, Ui.BorderDark).Column(2)
            }
        };

        return new VerticalStackLayout
        {
            Padding = new Thickness(24, 28, 24, 24), Spacing = 14,
            Children =
            {
                Ui.Text("Welcome back", 30, bold: true),
                subtitle,
                new Grid
                {
                    Margin = new Thickness(0, 8, 0, -6),
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    Children = { Ui.Text("Master password", 13, sub: true), forgot.Column(1) }
                },
                PasswordBox(),
                _unlock, orRow, _fingerprint, _busy, _error,
                Ui.Text(Footers[Random.Shared.Next(Footers.Length)], 12, sub: true).Center()
            }
        };
    }

    // No vault on this phone yet: bring it over from the PC, or start a new one.
    private View SetUpForm()
    {
        var open = Ui.PrimaryButton("Open my vault from the PC");
        open.Background = _unlock.Background;
        open.Clicked += async (_, _) =>
        {
            _error.IsVisible = false;
            try
            {
                if (await VaultFileFlow.OpenSharedVaultAsync(this)) App.ShowRoot(new UnlockPage());
            }
            catch (Exception ex)
            {
                _error.Show(ex.Message);
            }
        };
        var create = Ui.SecondaryButton("Create a new vault");
        create.Clicked += async (_, _) => await Navigation.PushModalAsync(new CreateVaultPage());

        return new VerticalStackLayout
        {
            Padding = new Thickness(24, 28, 24, 24), Spacing = 14,
            Children =
            {
                Ui.Text("Welcome", 30, bold: true),
                Ui.Text("Open the vault from your PC to use the same passwords here. On the PC: Settings › Sync with your phone, then pick that file below (from Google Drive or OneDrive).", 15, sub: true),
                open, create, _error,
                Ui.Text(@"No sync set up? You can also pick vault.db itself (%LOCALAPPDATA%\SecretVault on the PC).", 12, sub: true).Center()
            }
        };
    }

    // Rounded field with a lock icon and an eye button, like the desktop.
    private View PasswordBox()
    {
        var eye = new Button { Text = "👁", FontSize = 18, BackgroundColor = Colors.Transparent, Padding = 0, WidthRequest = 44 }
            .Themed(Button.TextColorProperty, Ui.SubLight, Ui.SubDark);
        eye.Clicked += (_, _) => _password.IsPassword = !_password.IsPassword;
        return new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 1.5, Padding = new Thickness(12, 2, 4, 2),
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 8,
                Children =
                {
                    new Label { Text = "🔒", FontSize = 16, VerticalOptions = LayoutOptions.Center },
                    _password.Column(1),
                    eye.Column(2)
                }
            }
        }.Themed(Border.StrokeProperty, Ui.Primary, Ui.PrimaryDark).Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark);
    }

    private static Label Link(string text, Func<Task> action)
    {
        var link = new Label { Text = text, FontSize = 13, Padding = 6 }.Themed(Label.TextColorProperty, Ui.Primary, Ui.PrimaryDark);
        link.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await action()) });
        return link;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!VaultSession.HasVault) return;
        if (BiometricUnlock.IsEnabled && !_promptedFingerprint)
        {
            _promptedFingerprint = true;
            await Task.Delay(300); // let the page finish showing before the system prompt slides up
            await UnlockWithFingerprintAsync();
        }
        else
        {
            _password.Focus();
        }
    }

    private async Task UnlockWithPasswordAsync()
    {
        if (string.IsNullOrEmpty(_password.Text)) { _error.Show("Enter your master password."); return; }
        var password = _password.Text;
        if (await TryUnlockAsync(password))
        {
            _password.Text = "";
            await OfferFingerprintAsync(password);
            App.ShowVault();
        }
    }

    private async Task UnlockWithFingerprintAsync()
    {
        string? password;
        try { password = await BiometricUnlock.UnlockAsync(); }
        catch (Exception ex)
        {
            _fingerprint.IsVisible = BiometricUnlock.IsEnabled;
            _error.Show(ex.Message);
            return;
        }
        if (password is null) { _password.Focus(); return; }

        if (await TryUnlockAsync(password))
        {
            App.ShowVault();
            return;
        }
        // The master password was changed (on the PC, or a synced change) since fingerprint unlock was set up.
        BiometricUnlock.Disable();
        _fingerprint.IsVisible = false;
        _error.Show("Your master password has changed. Enter the new one; you can then turn fingerprint / face unlock on again.");
    }

    private async Task<bool> TryUnlockAsync(string password)
    {
        var blocked = VaultSession.RemainingBlock();
        if (blocked > TimeSpan.Zero) { _error.Show($"Too many wrong passwords. Try again in {Describe(blocked)}."); return false; }

        SetBusy(true);
        try
        {
            if (await VaultSession.TryUnlockAsync(password)) return true;
            var wait = VaultSession.RemainingBlock();
            _error.Show(wait > TimeSpan.Zero ? $"Wrong master password. Try again in {Describe(wait)}." : "Wrong master password.");
            return false;
        }
        catch (Exception ex)
        {
            _error.Show("This vault file can't be opened: " + ex.Message);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OfferFingerprintAsync(string password)
    {
        var pending = Preferences.Get(EnrollPendingKey, false);
        Preferences.Remove(EnrollPendingKey);
        if (!BiometricUnlock.IsAvailable || BiometricUnlock.IsEnabled) return;
        if (!pending)
        {
            if (BiometricUnlock.Declined) return;
            var yes = await DisplayAlertAsync("Unlock with fingerprint or face?", "Next time, open SecretVault with your fingerprint or face instead of typing the master password.", "Turn on", "Not now");
            if (!yes) { BiometricUnlock.Declined = true; return; }
        }
        try { await BiometricUnlock.EnableAsync(password); }
        catch (Exception ex) { await DisplayAlertAsync("Fingerprint / face unlock", "Couldn't turn it on: " + ex.Message, "OK"); }
    }

    private void SetBusy(bool busy)
    {
        _busy.IsRunning = _busy.IsVisible = busy;
        _unlock.IsEnabled = _fingerprint.IsEnabled = _password.IsEnabled = !busy;
        _unlock.Text = busy ? "Unlocking…" : "Unlock";
        if (busy) _error.IsVisible = false;
    }

    private static string Describe(TimeSpan wait) =>
        wait.TotalSeconds < 60 ? $"{Math.Ceiling(wait.TotalSeconds)} seconds" : $"{Math.Ceiling(wait.TotalMinutes)} minutes";
}
