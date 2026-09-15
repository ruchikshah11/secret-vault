using SecretVault.Models;

namespace SecretVault.Gui;

public class PaymentCardEditForm : Form
{
    private readonly PaymentCard? _existing;

    private readonly TextBox _cardholderBox = new();
    private readonly TextBox _numberBox = new();
    private readonly TextBox _expiryBox = new() { PlaceholderText = "MM/YY" };
    private readonly TextBox _cvvBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _notesBox = new() { Multiline = true, Height = 60 };

    public PaymentCard? Result { get; private set; }

    public PaymentCardEditForm(PaymentCard? existing = null)
    {
        _existing = existing;

        Text = existing is null ? "Add Payment Card" : "Edit Payment Card";
        Size = new Size(460, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.Shell;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20), Tag = AppTheme.RoleShell };
        var contentHost = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };
        content.Controls.Add(contentHost);

        var y = 0;
        y = AddLabeled(contentHost, "Cardholder Name", _cardholderBox, y);
        y = AddLabeled(contentHost, "Card Number", _numberBox, y);
        y = AddLabeled(contentHost, "Expiration Date", _expiryBox, y);
        y = AddLabeled(contentHost, "Security Code", _cvvBox, y);
        AddLabeled(contentHost, "Notes", _notesBox, y);

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 60, Tag = AppTheme.RoleShell };
        var bottomDivider = new Panel { Dock = DockStyle.Top, Height = 1, Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        bottomBar.Controls.Add(bottomDivider);

        var cancelButton = new Button { Text = "Cancel", Size = new Size(90, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var saveButton = new Button { Text = "Save", Size = new Size(90, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        StyleSecondary(cancelButton);
        StylePrimary(saveButton);
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        saveButton.Click += (_, _) => OnSave();

        void LayoutButtons()
        {
            saveButton.Location = new Point(bottomBar.Width - 24 - saveButton.Width, 14);
            cancelButton.Location = new Point(saveButton.Left - 8 - cancelButton.Width, 14);
        }
        bottomBar.Resize += (_, _) => LayoutButtons();

        bottomBar.Controls.Add(cancelButton);
        bottomBar.Controls.Add(saveButton);
        LayoutButtons();

        Controls.Add(content);
        Controls.Add(bottomBar);

        if (_existing is not null)
        {
            _cardholderBox.Text = _existing.CardholderName;
            _numberBox.Text = _existing.CardNumber;
            _expiryBox.Text = _existing.ExpirationDate;
            _cvvBox.Text = _existing.SecurityCode;
            _notesBox.Text = _existing.Notes;
        }

        AppTheme.Apply(this);
    }

    private static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = AppTheme.Gold;
        b.ForeColor = Color.Black;
        b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Tag = AppTheme.NoTheme;
        AppTheme.ThemeChanged += () => b.BackColor = AppTheme.Gold;
    }

    private static void StyleSecondary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = AppTheme.Border;
        b.BackColor = AppTheme.Shell;
        b.ForeColor = AppTheme.Text;
        b.Cursor = Cursors.Hand;
        b.Tag = AppTheme.NoTheme;
        AppTheme.ThemeChanged += () =>
        {
            b.BackColor = AppTheme.Shell;
            b.ForeColor = AppTheme.Text;
            b.FlatAppearance.BorderColor = AppTheme.Border;
        };
    }

    private static int AddLabeled(Panel parent, string label, Control control, int y)
    {
        var lbl = new Label { Text = label, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        parent.Controls.Add(lbl);
        y += 20;

        control.Location = new Point(0, y);
        control.Size = new Size(parent.Width, control.Height > 0 ? control.Height : 26);
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        if (control is TextBox { Multiline: false })
            ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
        parent.Controls.Add(control);

        return y + control.Height + 14;
    }

    private void OnSave()
    {
        if (string.IsNullOrWhiteSpace(_cardholderBox.Text) || string.IsNullOrWhiteSpace(_numberBox.Text))
        {
            MessageBox.Show(this, "Cardholder name and card number are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        Result = new PaymentCard
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString(),
            CardholderName = _cardholderBox.Text.Trim(),
            CardNumber = _numberBox.Text.Trim(),
            ExpirationDate = _expiryBox.Text.Trim(),
            SecurityCode = _cvvBox.Text.Trim(),
            Notes = _notesBox.Text.Trim(),
            CreatedAt = _existing?.CreatedAt ?? now,
            ModifiedAt = now
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
