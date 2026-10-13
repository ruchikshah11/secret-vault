using System.Drawing.Drawing2D;
using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

// Unlock / create-vault screen: blue brand panel on the left, sign-in form on the right.
public class LoginForm : Form
{
    private const int BrandWidth = 340;
    private const int FormPad = 56;
    private const int FieldHeight = 46;

    // One of these is shown at random in the brand panel footer each time the screen opens.
    private static readonly string[] FooterMessages =
    {
        "🔒  Only you can open your vault — not even us.",
        "🔒  Encrypted on this PC. Your master password never leaves your device.",
    };

    private readonly FileVaultStore _store = new();
    private readonly bool _isNewVault;

    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirmBox = new() { UseSystemPasswordChar = true, PlaceholderText = "Re-enter the password" };
    private readonly RoundedField _passwordField;
    private readonly RoundedField _confirmField;
    private readonly Label _errorLabel = new();
    private readonly Label _capsLabel = new();
    private readonly Label _strengthLabel = new();
    private readonly PillButton _actionButton;
    private readonly IconButton _showPasswordButton = new(IconKind.Eye, 32) { Circular = false };
    private readonly PillButton _helloButton = new("Unlock with Windows Hello", primary: false, new Size(100, 46)) { LeadingIcon = IconKind.Fingerprint };
    private readonly Panel _orDivider = new() { Tag = AppTheme.NoTheme };
    private readonly PillButton _winPassButton = new("Unlock with Windows password", primary: false, new Size(100, 46)) { LeadingIcon = IconKind.Person };
    private readonly bool _offerHello;
    private readonly bool _offerWinPass;
    private bool _helloBusy;

    // Throttling of wrong master passwords: the first FreeAttempts are immediate, then each wrong try adds a growing wait.
    private const int FreeAttempts = 3;
    private readonly System.Windows.Forms.Timer _throttleTimer = new() { Interval = 500 };

    public VaultData? UnlockedData { get; private set; }
    public string? MasterPassword { get; private set; }

    public LoginForm(string? notice = null)
    {
        _isNewVault = !_store.Exists();
        _offerHello = !_isNewVault && WindowsHello.IsEnabled;
        _offerWinPass = !_isNewVault && WindowsPasswordUnlock.IsEnabled;

        Text = "SecretVault";
        Icon = AppIcon.Value;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(900, 580);
        BackColor = AppTheme.CardBg;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        Controls.Add(BuildBrandPanel());

        // ---- Right: the form ----
        var form = new Panel { Location = new Point(BrandWidth, 0), Size = new Size(ClientSize.Width - BrandWidth, ClientSize.Height), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        Controls.Add(form);
        var inner = form.Width - FormPad * 2;
        var y = _isNewVault ? 64 : _offerHello || _offerWinPass ? 52 : 92;

        var title = new Label
        {
            Text = _isNewVault ? "Create your vault" : "Welcome back",
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(FormPad - 4, y),
            Tag = AppTheme.NoTheme
        };
        form.Controls.Add(title);
        y += 54;

        form.Controls.Add(new Label
        {
            Text = _isNewVault
                ? "Pick one strong master password. It's the only key to your vault — it can't be reset or recovered."
                : "Enter your master password to unlock your vault.",
            ForeColor = AppTheme.SubText,
            Font = new Font("Segoe UI", 10f),
            Location = new Point(FormPad - 1, y),
            Size = new Size(inner, _isNewVault ? 44 : 24),
            Tag = AppTheme.NoTheme
        });
        y += _isNewVault ? 60 : 44;

        if (notice is not null)
        {
            var banner = new Panel { Location = new Point(FormPad, y), Size = new Size(inner, 44), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
            var bannerBack = UiKit.Tint(AppTheme.Accent, AppTheme.CardBg);
            banner.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(AppTheme.CardBg);
                using var path = RoundedField.RoundRect(new RectangleF(0, 0, banner.Width - 1, banner.Height - 1), 10);
                using var b = new SolidBrush(bannerBack);
                g.FillPath(b, path);
                Icons.Draw(g, IconKind.Lock, new Rectangle(14, 13, 16, 18), AppTheme.Accent, 1.7f, bannerBack);
                TextRenderer.DrawText(g, notice, Font, new Rectangle(40, 0, banner.Width - 50, banner.Height), AppTheme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            };
            form.Controls.Add(banner);
            y += 60;
        }

        form.Controls.Add(UiKit.Caption("Master password", new Point(FormPad - 1, y)));
        if (!_isNewVault)
        {
            var forgot = new LinkLabel
            {
                Text = "Forgot password?",
                AutoSize = true,
                LinkColor = AppTheme.Accent,
                ActiveLinkColor = AppTheme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font("Segoe UI Semibold", 8.75f),
                Tag = AppTheme.NoTheme
            };
            forgot.Location = new Point(FormPad + inner - TextRenderer.MeasureText(forgot.Text, forgot.Font).Width, y);
            forgot.LinkClicked += async (_, _) => await OnForgotPasswordAsync();
            form.Controls.Add(forgot);
        }
        _passwordBox.PlaceholderText = _isNewVault ? "At least 12 characters" : "Enter master password";
        _passwordField = new RoundedField(_passwordBox, FieldHeight) { LeadingIcon = IconKind.Lock };
        _passwordField.SetBounds(FormPad, y + 24, inner, FieldHeight);
        _passwordBox.Font = new Font("Segoe UI", 11f);
        _showPasswordButton.Click += (_, _) =>
        {
            _passwordBox.UseSystemPasswordChar = !_passwordBox.UseSystemPasswordChar;
            _confirmBox.UseSystemPasswordChar = _passwordBox.UseSystemPasswordChar;
            _showPasswordButton.Icon = _passwordBox.UseSystemPasswordChar ? IconKind.Eye : IconKind.EyeOff;
            _showPasswordButton.Invalidate();
        };
        new ToolTip().SetToolTip(_showPasswordButton, "Show / hide password");
        _passwordField.AddTrailing(_showPasswordButton);
        form.Controls.Add(_passwordField);
        y = _passwordField.Bottom + 6;

        // Caps Lock warning (left) and, for a new vault, live strength (right).
        _capsLabel.Text = "⚠  Caps Lock is on";
        _capsLabel.ForeColor = AppTheme.StrengthColor(StrengthLevel.Weak);
        _capsLabel.Font = new Font("Segoe UI Semibold", 8.5f);
        _capsLabel.SetBounds(FormPad, y, inner / 2, 20);
        _capsLabel.Tag = AppTheme.NoTheme;
        _strengthLabel.SetBounds(FormPad + inner / 2, y, inner / 2, 20);
        _strengthLabel.TextAlign = ContentAlignment.MiddleRight;
        _strengthLabel.Font = new Font("Segoe UI Semibold", 8.5f);
        _strengthLabel.Tag = AppTheme.NoTheme;
        form.Controls.Add(_capsLabel);
        form.Controls.Add(_strengthLabel);
        y += 24;

        _confirmField = new RoundedField(_confirmBox, FieldHeight) { LeadingIcon = IconKind.Lock };
        _confirmBox.Font = new Font("Segoe UI", 11f);
        if (_isNewVault)
        {
            form.Controls.Add(UiKit.Caption("Confirm password", new Point(FormPad - 1, y)));
            _confirmField.SetBounds(FormPad, y + 24, inner, FieldHeight);
            form.Controls.Add(_confirmField);
            y = _confirmField.Bottom + 10;
            _passwordBox.TextChanged += (_, _) => UpdateStrength();
        }

        _errorLabel.ForeColor = AppTheme.Danger;
        _errorLabel.Font = new Font("Segoe UI Semibold", 9f);
        _errorLabel.SetBounds(FormPad, y, inner, 20);
        _errorLabel.Tag = AppTheme.NoTheme;
        form.Controls.Add(_errorLabel);
        y += 28;

        _actionButton = new PillButton(_isNewVault ? "Create vault" : "Unlock", primary: true, new Size(inner, 50))
        {
            LeadingIcon = _isNewVault ? IconKind.Shield : IconKind.Lock,
            Location = new Point(FormPad, y),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        };
        _actionButton.Click += async (_, _) => await OnActionAsync();
        form.Controls.Add(_actionButton);
        y = _actionButton.Bottom + 22;

        if (_offerHello || _offerWinPass)
        {
            // "──── or ────" then the Windows Hello button.
            _orDivider.SetBounds(FormPad, y - 8, inner, 20);
            _orDivider.BackColor = AppTheme.CardBg;
            _orDivider.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.Clear(AppTheme.CardBg);
                var size = TextRenderer.MeasureText("or", Font);
                var mid = _orDivider.Height / 2;
                using var pen = new Pen(AppTheme.Border);
                g.DrawLine(pen, 0, mid, (_orDivider.Width - size.Width) / 2 - 8, mid);
                g.DrawLine(pen, (_orDivider.Width + size.Width) / 2 + 8, mid, _orDivider.Width, mid);
                TextRenderer.DrawText(g, "or", Font, new Rectangle(0, 0, _orDivider.Width, _orDivider.Height), AppTheme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            form.Controls.Add(_orDivider);
            // One full-width button, or two side by side when both alternatives are enabled.
            var both = _offerHello && _offerWinPass;
            var buttonWidth = both ? (inner - 10) / 2 : inner;
            var x = FormPad;
            if (_offerHello)
            {
                if (both) _helloButton.Text = "Windows Hello";
                _helloButton.SetBounds(x, y + 20, buttonWidth, 46);
                _helloButton.Font = new Font("Segoe UI Semibold", 10f);
                _helloButton.Click += async (_, _) => await UnlockWithHelloAsync();
                form.Controls.Add(_helloButton);
                x += buttonWidth + 10;
            }
            if (_offerWinPass)
            {
                if (both) _winPassButton.Text = "Windows password";
                _winPassButton.SetBounds(x, y + 20, buttonWidth, 46);
                _winPassButton.Font = new Font("Segoe UI Semibold", 10f);
                _winPassButton.Click += async (_, _) => await UnlockWithWindowsPasswordAsync();
                form.Controls.Add(_winPassButton);
            }
            y += 20 + 46 + 18;

            // Offer Windows Hello straight away, like Windows sign-in does.
            if (_offerHello)
                Shown += async (_, _) => await UnlockWithHelloAsync();
        }

        form.Controls.Add(new Label
        {
            Text = _isNewVault
                ? "Tip: a passphrase of 4–5 random words is strong and easy to remember."
                : "Locked out? Use \"Forgot password?\" above with your recovery key.",
            ForeColor = AppTheme.SubText,
            Font = new Font("Segoe UI", 8.75f),
            TextAlign = ContentAlignment.TopCenter,
            Location = new Point(FormPad, y),
            Size = new Size(inner, 36),
            Tag = AppTheme.NoTheme
        });

        AcceptButton = _actionButton;
        ActiveControl = _passwordBox;
        _throttleTimer.Tick += (_, _) => UpdateThrottle();
        FormClosed += (_, _) =>
        {
            _throttleTimer.Dispose();
            if (DialogResult == DialogResult.OK) ResetThrottle(); // any successful unlock clears the counter
        };
        UpdateThrottle();
        UpdateCapsLock();
        KeyUp += (_, _) => UpdateCapsLock();
        Activated += (_, _) => UpdateCapsLock();
    }

    // ---- Left: brand panel ----
    private Panel BuildBrandPanel()
    {
        var panel = new Panel { Location = Point.Empty, Size = new Size(BrandWidth, ClientSize.Height), Tag = AppTheme.NoTheme };
        var titleFont = new Font("Segoe UI", 24f, FontStyle.Bold);
        var taglineFont = new Font("Segoe UI", 11f);
        var footerFont = new Font("Segoe UI", 8.5f);
        var footerText = FooterMessages[Random.Shared.Next(FooterMessages.Length)];
        const TextFormatFlags centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix;

        // Just the essentials, centred: app icon, name, tagline; a small reassurance line at the bottom.
        panel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var rect = panel.ClientRectangle;

            using (var bg = new LinearGradientBrush(rect, Color.FromArgb(37, 99, 235), Color.FromArgb(30, 58, 138), 60f))
                g.FillRectangle(bg, rect);

            // Decorative soft circles.
            using (var glow = new SolidBrush(Color.FromArgb(22, Color.White)))
            {
                g.FillEllipse(glow, rect.Width - 170, -120, 340, 340);
                g.FillEllipse(glow, -140, rect.Height - 200, 320, 320);
            }

            const int tileSize = 96;
            var top = rect.Height / 2 - 120;
            var tileX = (rect.Width - tileSize) / 2;
            using (var tile = RoundedField.RoundRect(new RectangleF(tileX, top, tileSize, tileSize), 24))
            using (var tileBrush = new SolidBrush(Color.FromArgb(40, Color.White)))
                g.FillPath(tileBrush, tile);
            if (AppIcon.Value is { } icon)
            {
                using var big = new Icon(icon, 64, 64);
                g.DrawIcon(big, new Rectangle(tileX + 16, top + 16, 64, 64));
            }

            TextRenderer.DrawText(g, "SecretVault", titleFont, new Rectangle(0, top + tileSize + 22, rect.Width, 48), Color.White, centered);
            TextRenderer.DrawText(g, "Your passwords, locked down.", taglineFont, new Rectangle(0, top + tileSize + 72, rect.Width, 26), Color.FromArgb(219, 234, 254), centered);

            TextRenderer.DrawText(g, footerText, footerFont, new Rectangle(24, rect.Height - 56, rect.Width - 48, 40), Color.FromArgb(147, 197, 253),
                centered | TextFormatFlags.WordBreak | TextFormatFlags.Bottom);
        };
        return panel;
    }

    private void UpdateCapsLock() => _capsLabel.Visible = IsKeyLocked(Keys.CapsLock);

    // ---------- wrong-password throttling ----------

    // 5s, 15s, 45s, 2m 15s, then 5 minutes for every further wrong attempt.
    private static TimeSpan DelayAfter(int failures) =>
        failures < FreeAttempts ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(3, failures - FreeAttempts)));

    private static TimeSpan RemainingBlock =>
        AppSettings.Current.UnlockBlockedUntilUtc is { } until && until > DateTime.UtcNow ? until - DateTime.UtcNow : TimeSpan.Zero;

    private static string Describe(TimeSpan t) =>
        t.TotalSeconds >= 60 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{Math.Ceiling(t.TotalSeconds)}s";

    private void UpdateThrottle()
    {
        if (_isNewVault) return;
        var remaining = RemainingBlock;
        if (remaining > TimeSpan.Zero)
        {
            _actionButton.Enabled = false;
            _actionButton.Text = $"Try again in {Describe(remaining)}";
            if (!_throttleTimer.Enabled) _throttleTimer.Start();
            return;
        }

        if (!_throttleTimer.Enabled) return;
        _throttleTimer.Stop();
        _actionButton.Text = "Unlock";
        _actionButton.Enabled = true;
        _errorLabel.Text = string.Empty;
        _passwordBox.Focus();
    }

    private void RegisterFailedAttempt()
    {
        var settings = AppSettings.Current;
        settings.FailedUnlockAttempts++;
        var delay = DelayAfter(settings.FailedUnlockAttempts);
        settings.UnlockBlockedUntilUtc = delay > TimeSpan.Zero ? DateTime.UtcNow + delay : null;
        settings.Save();

        var message = delay > TimeSpan.Zero
            ? $"Incorrect master password. Too many attempts — please wait {Describe(delay)}."
            : $"Incorrect master password. Please try again. ({FreeAttempts - settings.FailedUnlockAttempts} left before a short wait)";
        Fail(_passwordField, _passwordBox, message);
        UpdateThrottle();
    }

    private static void ResetThrottle()
    {
        var settings = AppSettings.Current;
        if (settings.FailedUnlockAttempts == 0 && settings.UnlockBlockedUntilUtc is null) return;
        settings.FailedUnlockAttempts = 0;
        settings.UnlockBlockedUntilUtc = null;
        settings.Save();
    }

    private void UpdateStrength()
    {
        if (_passwordBox.TextLength == 0) { _strengthLabel.Text = string.Empty; return; }
        var level = PasswordStrength.Evaluate(_passwordBox.Text);
        _strengthLabel.Text = $"Strength: {UiKit.StrengthText(level)}";
        _strengthLabel.ForeColor = AppTheme.StrengthColor(level);
    }

    private async Task OnActionAsync()
    {
        _errorLabel.Text = string.Empty;
        var password = _passwordBox.Text;

        if (string.IsNullOrEmpty(password))
        {
            Fail(_passwordField, _passwordBox, "Password cannot be empty.");
            return;
        }

        if (_isNewVault)
        {
            if (password.Length < 12)
            {
                Fail(_passwordField, _passwordBox, "Use at least 12 characters.");
                return;
            }
            if (password != _confirmBox.Text)
            {
                Fail(_confirmField, _confirmBox, "Passwords do not match.");
                return;
            }

            var data = new VaultData();
            await _store.SaveAsync(data, password);
            await OfferRecoveryKeyAsync(data, password);
            UnlockedData = data;
            MasterPassword = password;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (RemainingBlock > TimeSpan.Zero) return; // still waiting after too many wrong attempts

        try
        {
            _actionButton.Enabled = false;
            _actionButton.Text = "Unlocking…";
            UnlockedData = await _store.LoadAsync(password);
            MasterPassword = password;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (VaultAuthenticationException)
        {
            _actionButton.Text = "Unlock";
            _actionButton.Enabled = true;
            RegisterFailedAttempt();
            _passwordBox.SelectAll();
        }
    }

    private async Task UnlockWithHelloAsync()
    {
        if (_helloBusy) return;
        _helloBusy = true;
        _errorLabel.Text = string.Empty;
        _helloButton.Enabled = false;
        _helloButton.Text = "Waiting for Windows Hello…";

        try
        {
            var (outcome, password) = await WindowsHello.UnlockAsync();
            switch (outcome)
            {
                case WindowsHello.Outcome.Success when password is not null:
                    try
                    {
                        UnlockedData = await _store.LoadAsync(password);
                        MasterPassword = password;
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    catch (VaultAuthenticationException)
                    {
                        // The master password changed since Hello was set up; the stored copy is stale.
                        await WindowsHello.DisableAsync();
                        _errorLabel.Text = "Windows Hello was reset because your master password changed. Use your password.";
                        _helloButton.Visible = false;
                        _orDivider.Visible = _winPassButton.Visible;
                    }
                    break;
                case WindowsHello.Outcome.Canceled:
                    break;
                default:
                    _errorLabel.Text = "Windows Hello isn't available right now. Use your master password.";
                    break;
            }
        }
        finally
        {
            _helloBusy = false;
            if (!IsDisposed)
            {
                _helloButton.Text = _offerWinPass ? "Windows Hello" : "Unlock with Windows Hello";
                _helloButton.Enabled = true;
                _passwordBox.Focus();
            }
        }
    }

    private async Task UnlockWithWindowsPasswordAsync()
    {
        _errorLabel.Text = string.Empty;
        _winPassButton.Enabled = false;
        try
        {
            var (outcome, master) = await WindowsPasswordUnlock.UnlockAsync(this);
            switch (outcome)
            {
                case WindowsPasswordUnlock.Outcome.Success when master is not null:
                    try
                    {
                        UnlockedData = await _store.LoadAsync(master);
                        MasterPassword = master;
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    catch (VaultAuthenticationException)
                    {
                        // The master password changed since this was set up.
                        WindowsPasswordUnlock.Disable();
                        _errorLabel.Text = "Windows password unlock was reset because your master password changed.";
                        _winPassButton.Visible = false;
                        _orDivider.Visible = _helloButton.Visible;
                    }
                    break;
                case WindowsPasswordUnlock.Outcome.PasswordChanged:
                    WindowsPasswordUnlock.Disable();
                    _errorLabel.Text = "Your Windows password changed. Use your master password, then turn this on again in Settings.";
                    _winPassButton.Visible = false;
                    _orDivider.Visible = _helloButton.Visible;
                    break;
                case WindowsPasswordUnlock.Outcome.WrongPassword:
                    _errorLabel.Text = "That isn't your Windows password.";
                    break;
                case WindowsPasswordUnlock.Outcome.Failed:
                    _errorLabel.Text = "Windows password unlock isn't available. Use your master password.";
                    break;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _winPassButton.Enabled = true;
                _passwordBox.Focus();
            }
        }
    }

    // New vault: strongly suggest a recovery key straight away.
    private async Task OfferRecoveryKeyAsync(VaultData data, string password)
    {
        if (!AppDialog.Confirm(this, "Create a recovery key?",
                "If you ever forget your master password, the recovery key is the only way to set a new one. Recommended.",
                "Create recovery key", "Not now", AppDialog.Kind.Question)) return;
        var key = await _store.CreateRecoveryKeyAsync(data, password);
        using var form = new RecoveryKeyForm(key);
        form.ShowDialog(this);
    }

    private async Task OnForgotPasswordAsync()
    {
        _errorLabel.Text = string.Empty;
        if (!await _store.HasRecoveryKeyAsync())
        {
            if (!WindowsHello.IsEnabled)
            {
                AppDialog.Show(this, "Password can't be reset",
                    "No recovery key has been set up for this vault. For your security there is no other way to open the vault without the master password.",
                    AppDialog.Kind.Warning);
                return;
            }

            if (AppDialog.Confirm(this, "Reset with Windows Hello?",
                    "No recovery key has been set up, but Windows Hello is on. Confirm it's you (fingerprint / face / PIN), then choose a new master password.",
                    "Use Windows Hello", kind: AppDialog.Kind.Question))
                await ResetWithWindowsHelloAsync();
            return;
        }

        using var reset = new ForgotPasswordForm(_store);
        if (reset.ShowDialog(this) != DialogResult.OK || reset.UnlockedData is null || reset.NewPassword is null) return;

        // Unlock shortcuts still hold the forgotten password: refresh Windows Hello (one fingerprint), turn the Windows-password option off.
        if (WindowsHello.IsEnabled && await WindowsHello.EnableAsync(reset.NewPassword) != WindowsHello.Outcome.Success)
            await WindowsHello.DisableAsync();
        if (WindowsPasswordUnlock.IsEnabled)
            WindowsPasswordUnlock.Disable();

        AppDialog.Show(this, "Master password reset", "Your new password is set. Your recovery key still works — keep it safe.", AppDialog.Kind.Success);
        UnlockedData = reset.UnlockedData;
        MasterPassword = reset.NewPassword;
        DialogResult = DialogResult.OK;
        Close();
    }

    // Forgot the password but Windows Hello is set up: Hello releases the stored password, the user picks a new one,
    // and the vault is re-locked with it. Ends unlocked, after offering a recovery key for next time.
    private async Task ResetWithWindowsHelloAsync()
    {
        if (_helloBusy) return;
        _helloBusy = true;
        try
        {
            var (outcome, oldPassword) = await WindowsHello.UnlockAsync();
            if (outcome == WindowsHello.Outcome.Canceled) return;
            if (outcome != WindowsHello.Outcome.Success || oldPassword is null)
            {
                _errorLabel.Text = "Windows Hello isn't available right now.";
                return;
            }

            VaultData data;
            try
            {
                data = await _store.LoadAsync(oldPassword);
            }
            catch (VaultAuthenticationException)
            {
                await WindowsHello.DisableAsync();
                _errorLabel.Text = "Windows Hello was out of date and has been turned off.";
                return;
            }

            using var form = new NewPasswordForm("Choose a new master password", "Confirmed with Windows Hello. Your vault will be re-locked with the new password.");
            if (form.ShowDialog(this) != DialogResult.OK || form.NewPassword is null) return;
            var newPassword = form.NewPassword;

            await _store.ChangeMasterPasswordAsync(data, oldPassword, newPassword);

            // Keep Windows Hello working with the new password (one more touch); the Windows-password option is reset.
            if (await WindowsHello.EnableAsync(newPassword) != WindowsHello.Outcome.Success)
                await WindowsHello.DisableAsync();
            if (WindowsPasswordUnlock.IsEnabled)
                WindowsPasswordUnlock.Disable();

            AppDialog.Show(this, "Master password reset", "Your new password is set and Windows Hello has been updated.", AppDialog.Kind.Success);
            await OfferRecoveryKeyAsync(data, newPassword);

            UnlockedData = data;
            MasterPassword = newPassword;
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            _helloBusy = false;
        }
    }

    private void Fail(RoundedField field, TextBox box, string message)
    {
        _errorLabel.Text = message;
        field.HasError = true;
        box.Focus();
    }
}
