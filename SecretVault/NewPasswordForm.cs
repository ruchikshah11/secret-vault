using SecretVault.Crypto;

namespace SecretVault.Gui;

// Asks for a new master password (twice) after the user has proven who they are another way (e.g. Windows Hello).
public class NewPasswordForm : Form
{
    private const int Pad = 26;
    private const int FieldHeight = 42;

    private readonly TextBox _newBox = new() { UseSystemPasswordChar = true, PlaceholderText = "At least 12 characters" };
    private readonly TextBox _confirmBox = new() { UseSystemPasswordChar = true };
    private readonly RoundedField _newField;
    private readonly RoundedField _confirmField;
    private readonly Label _strength = new();
    private readonly Label _error = new();

    public string? NewPassword { get; private set; }

    public NewPasswordForm(string title, string message)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var card = UiKit.NewCard();
        card.SetBounds(20, 20, 440, 100);
        var inner = card.Width - Pad * 2;
        var y = Pad;

        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad - 2, y), Tag = AppTheme.NoTheme });
        y += 38;
        card.Controls.Add(new Label { Text = message, ForeColor = AppTheme.SubText, Location = new Point(Pad, y), Size = new Size(inner, 40), Tag = AppTheme.NoTheme });
        y += 50;

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

        _error.SetBounds(Pad, y - 8, inner, 20);
        _error.ForeColor = AppTheme.Danger;
        _error.Font = new Font("Segoe UI Semibold", 9f);
        _error.Tag = AppTheme.NoTheme;
        card.Controls.Add(_error);
        y += 18;

        var save = new PillButton("Set password", primary: true, new Size(140, 40)) { Location = new Point(card.Width - Pad - 140, y) };
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
        ActiveControl = _newBox;
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

    private void OnSave()
    {
        _error.Text = string.Empty;
        if (_newBox.TextLength < 12) { Fail(_newField, _newBox, "Use at least 12 characters."); return; }
        if (_newBox.Text != _confirmBox.Text) { Fail(_confirmField, _confirmBox, "Passwords do not match."); return; }
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
