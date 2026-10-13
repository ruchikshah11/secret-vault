using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

// "Forgot password?": set a new master password using the recovery key. On success the vault is unlocked.
public class ForgotPasswordForm : Form
{
    private const int Pad = 26;
    private const int FieldHeight = 42;

    private readonly FileVaultStore _store;
    private readonly TextBox _keyBox = new() { PlaceholderText = "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _newBox = new() { UseSystemPasswordChar = true, PlaceholderText = "At least 12 characters" };
    private readonly TextBox _confirmBox = new() { UseSystemPasswordChar = true };
    private readonly RoundedField _keyField;
    private readonly RoundedField _newField;
    private readonly RoundedField _confirmField;
    private readonly Label _strength = new();
    private readonly Label _error = new();
    private readonly PillButton _resetButton;

    public VaultData? UnlockedData { get; private set; }
    public string? NewPassword { get; private set; }

    public ForgotPasswordForm(FileVaultStore store)
    {
        _store = store;

        Text = "Reset master password";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var card = UiKit.NewCard();
        card.SetBounds(20, 20, 460, 100);
        var inner = card.Width - Pad * 2;
        var y = Pad;

        card.Controls.Add(new Label { Text = "Forgot your master password?", Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad - 2, y), Tag = AppTheme.NoTheme });
        y += 38;
        card.Controls.Add(new Label
        {
            Text = "Enter the recovery key you saved when you set it up, then choose a new master password.",
            ForeColor = AppTheme.SubText, Location = new Point(Pad, y), Size = new Size(inner, 40), Tag = AppTheme.NoTheme
        });
        y += 50;

        _keyField = new RoundedField(_keyBox, FieldHeight) { LeadingIcon = IconKind.Key };
        _keyBox.Font = new Font("Consolas", 10.5f);
        y = AddLabeled(card, "Recovery key", _keyField, y, inner);
        _newField = new RoundedField(_newBox, FieldHeight) { LeadingIcon = IconKind.Lock };
        _newBox.TextChanged += (_, _) => UpdateStrength();
        y = AddLabeled(card, "New master password", _newField, y, inner) - 10;
        _strength.SetBounds(Pad, y, inner, 18);
        _strength.Font = new Font("Segoe UI Semibold", 8.5f);
        _strength.Tag = AppTheme.NoTheme;
        card.Controls.Add(_strength);
        y += 26;
        _confirmField = new RoundedField(_confirmBox, FieldHeight) { LeadingIcon = IconKind.Lock };
        y = AddLabeled(card, "Confirm new password", _confirmField, y, inner);

        _error.SetBounds(Pad, y - 8, inner, 36);
        _error.ForeColor = AppTheme.Danger;
        _error.Font = new Font("Segoe UI Semibold", 9f);
        _error.Tag = AppTheme.NoTheme;
        card.Controls.Add(_error);
        y += 30;

        _resetButton = new PillButton("Reset password", primary: true, new Size(150, 40)) { Location = new Point(card.Width - Pad - 150, y) };
        var cancel = new PillButton("Cancel", primary: false, new Size(96, 40)) { Location = new Point(_resetButton.Left - 10 - 96, y) };
        _resetButton.Click += async (_, _) => await ResetAsync();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        card.Controls.Add(_resetButton);
        card.Controls.Add(cancel);
        card.Height = _resetButton.Bottom + Pad;

        AcceptButton = _resetButton;
        CancelButton = cancel;
        Controls.Add(card);
        ClientSize = new Size(card.Width + 40, card.Height + 40);
        ActiveControl = _keyBox;
    }

    private static int AddLabeled(Panel card, string label, Control field, int y, int width)
    {
        card.Controls.Add(UiKit.Caption(label, new Point(Pad - 1, y)));
        field.SetBounds(Pad, y + 22, width, field.Height);
        card.Controls.Add(field);
        return field.Bottom + 16;
    }

    private void UpdateStrength()
    {
        if (_newBox.TextLength == 0) { _strength.Text = string.Empty; return; }
        var level = PasswordStrength.Evaluate(_newBox.Text);
        _strength.Text = $"Strength: {UiKit.StrengthText(level)}";
        _strength.ForeColor = AppTheme.StrengthColor(level);
    }

    private async Task ResetAsync()
    {
        _error.Text = string.Empty;
        if (!RecoveryKey.LooksValid(_keyBox.Text)) { Fail(_keyField, _keyBox, "That doesn't look like a recovery key (30 letters and digits)."); return; }
        if (_newBox.TextLength < 12) { Fail(_newField, _newBox, "Use at least 12 characters."); return; }
        if (_newBox.Text != _confirmBox.Text) { Fail(_confirmField, _confirmBox, "Passwords do not match."); return; }

        _resetButton.Enabled = false;
        _resetButton.Text = "Resetting…";
        try
        {
            UnlockedData = await _store.ResetMasterPasswordAsync(_keyBox.Text, _newBox.Text);
            NewPassword = _newBox.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (VaultAuthenticationException)
        {
            Fail(_keyField, _keyBox, "That recovery key doesn't match this vault.");
        }
        catch (InvalidOperationException)
        {
            _error.Text = "This vault has no recovery key.";
        }
        finally
        {
            if (!IsDisposed && DialogResult != DialogResult.OK)
            {
                _resetButton.Enabled = true;
                _resetButton.Text = "Reset password";
            }
        }
    }

    private void Fail(RoundedField field, TextBox box, string message)
    {
        _error.Text = message;
        field.HasError = true;
        box.Focus();
    }
}
