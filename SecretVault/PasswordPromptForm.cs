namespace SecretVault.Gui;

// Small dialog asking for a single password (e.g. the one a backup was saved with).
public class PasswordPromptForm : Form
{
    private readonly TextBox _box = new() { UseSystemPasswordChar = true };

    public string Password => _box.Text;

    public PasswordPromptForm(string title, string message)
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
        card.SetBounds(20, 20, 380, 200);
        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(22, 20), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label { Text = message, ForeColor = AppTheme.SubText, Location = new Point(24, 50), Size = new Size(332, 36), Tag = AppTheme.NoTheme });
        var field = new RoundedField(_box, 40) { LeadingIcon = IconKind.Lock };
        field.SetBounds(24, 92, 332, 40);
        card.Controls.Add(field);

        var ok = new PillButton("Continue", primary: true, new Size(110, 38)) { Location = new Point(246, 146) };
        var cancel = new PillButton("Cancel", primary: false, new Size(90, 38)) { Location = new Point(146, 146) };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        card.Controls.Add(ok);
        card.Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(card);
        ClientSize = new Size(420, 240);
        ActiveControl = _box;
    }
}
