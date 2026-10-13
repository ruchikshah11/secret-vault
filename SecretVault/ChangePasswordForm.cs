using SecretVault.Crypto;

namespace SecretVault.Gui;

// Collects and validates a new master password. The caller re-encrypts the vault with it.
public class ChangePasswordForm : Form
{
    private const int Pad = 24;
    private const int FieldHeight = 40;

    private readonly string _currentPassword;
    private readonly TextBox _currentBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _newBox = new() { UseSystemPasswordChar = true, PlaceholderText = "At least 12 characters" };
    private readonly TextBox _confirmBox = new() { UseSystemPasswordChar = true };
    private readonly RoundedField _currentField;
    private readonly RoundedField _newField;
    private readonly RoundedField _confirmField;
    private readonly Label _strength = new();
    private readonly Label _error = new();

    public string? NewPassword { get; private set; }

    public ChangePasswordForm(string currentPassword)
    {
        _currentPassword = currentPassword;

        Text = "Change Master Password";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var card = UiKit.NewCard();
        card.Location = new Point(20, 20);
        card.Width = 420;
        var inner = card.Width - Pad * 2;
        var y = Pad;

        card.Controls.Add(new Label { Text = "Change master password", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad - 2, y), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label
        {
            Text = "Your vault will be re-locked with the new password. A recovery key, if you have one, keeps working.",
            ForeColor = AppTheme.SubText, Location = new Point(Pad, y + 32), Size = new Size(inner, 38), Tag = AppTheme.NoTheme
        });
        y += 82;

        _currentField = new RoundedField(_currentBox, FieldHeight) { LeadingIcon = IconKind.Lock };
        var currentTop = y;
        y = AddLabeled(card, "Current password", _currentField, y, inner);

        // Forgot the current password? Windows Hello (fingerprint / face / PIN) can confirm it's you instead.
        if (WindowsHello.IsEnabled)
        {
            var helloLink = new LinkLabel
            {
                Text = "Use Windows Hello instead",
                AutoSize = true,
                LinkColor = AppTheme.Accent,
                ActiveLinkColor = AppTheme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font("Segoe UI Semibold", 8.75f),
                Tag = AppTheme.NoTheme
            };
            helloLink.Location = new Point(Pad + inner - TextRenderer.MeasureText(helloLink.Text, helloLink.Font).Width, currentTop);
            helloLink.LinkClicked += async (_, _) => await VerifyWithHelloAsync(helloLink);
            card.Controls.Add(helloLink);
        }
        _newField = new RoundedField(_newBox, FieldHeight) { LeadingIcon = IconKind.Key };
        _newBox.TextChanged += (_, _) => UpdateStrength();
        y = AddLabeled(card, "New password", _newField, y, inner) - 10;
        _strength.SetBounds(Pad, y, inner, 18);
        _strength.Font = new Font("Segoe UI Semibold", 8.5f);
        _strength.Tag = AppTheme.NoTheme;
        card.Controls.Add(_strength);
        y += 26;
        _confirmField = new RoundedField(_confirmBox, FieldHeight) { LeadingIcon = IconKind.Key };
        y = AddLabeled(card, "Confirm new password", _confirmField, y, inner);

        _error.SetBounds(Pad, y - 8, inner, 20);
        _error.ForeColor = AppTheme.Danger;
        _error.Font = new Font("Segoe UI Semibold", 9f);
        _error.Tag = AppTheme.NoTheme;
        card.Controls.Add(_error);
        y += 16;

        var save = new PillButton("Change password", primary: true, new Size(160, 40)) { Location = new Point(card.Width - Pad - 160, y) };
        var cancel = new PillButton("Cancel", primary: false, new Size(96, 40)) { Location = new Point(save.Left - 10 - 96, y) };
        save.Click += (_, _) => OnSave();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        card.Controls.Add(save);
        card.Controls.Add(cancel);
        card.Height = save.Bottom + Pad;

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(card);
        ClientSize = new Size(card.Width + 40, card.Height + 40);
        ActiveControl = _currentBox;
    }

    private static int AddLabeled(Panel card, string label, Control field, int y, int width)
    {
        card.Controls.Add(UiKit.Caption(label, new Point(Pad - 1, y)));
        field.SetBounds(Pad, y + 22, width, field.Height);
        card.Controls.Add(field);
        return field.Bottom + 16;
    }

    private async Task VerifyWithHelloAsync(LinkLabel link)
    {
        _error.Text = string.Empty;
        link.Enabled = false;
        var (outcome, password) = await WindowsHello.UnlockAsync();
        if (outcome == WindowsHello.Outcome.Success && password == _currentPassword)
        {
            _currentBox.Text = password;
            _currentBox.ReadOnly = true;
            link.Text = "✓ Verified with Windows Hello";
            link.LinkColor = AppTheme.StrengthColor(StrengthLevel.Strong);
            link.Left = _currentField.Right - TextRenderer.MeasureText(link.Text, link.Font).Width;
            _newBox.Focus();
            return;
        }

        link.Enabled = true;
        if (outcome == WindowsHello.Outcome.Success)
            _error.Text = "Windows Hello holds an older password. Type your current password instead.";
        else if (outcome != WindowsHello.Outcome.Canceled)
            _error.Text = "Windows Hello isn't available right now.";
    }

    private void UpdateStrength()
    {
        if (_newBox.TextLength == 0) { _strength.Text = string.Empty; return; }
        var level = PasswordStrength.Evaluate(_newBox.Text);
        _strength.Text = $"Strength: {UiKit.StrengthText(level)}";
        _strength.ForeColor = AppTheme.StrengthColor(level);
    }

    private void OnSave()
    {
        _error.Text = string.Empty;
        if (_currentBox.Text != _currentPassword)
        {
            Fail(_currentField, _currentBox, "Current password is incorrect.");
            return;
        }
        if (_newBox.TextLength < 12)
        {
            Fail(_newField, _newBox, "Use at least 12 characters.");
            return;
        }
        if (_newBox.Text == _currentPassword)
        {
            Fail(_newField, _newBox, "The new password must be different.");
            return;
        }
        if (_newBox.Text != _confirmBox.Text)
        {
            Fail(_confirmField, _confirmBox, "Passwords do not match.");
            return;
        }

        NewPassword = _newBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Fail(RoundedField field, TextBox box, string message)
    {
        _error.Text = message;
        field.HasError = true;
        box.Focus();
    }
}
