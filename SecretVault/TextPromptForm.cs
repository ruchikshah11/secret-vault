namespace SecretVault.Gui;

// Small dialog asking for one line of text (e.g. a folder name).
public class TextPromptForm : Form
{
    private readonly TextBox _box = new();
    private readonly RoundedField _field;
    private readonly Label _error = new();
    private readonly Func<string, string?>? _validate;

    public string Value => _box.Text.Trim();

    // validate returns an error message, or null when the value is fine.
    public TextPromptForm(string title, string message, string initial = "", string actionText = "OK", Func<string, string?>? validate = null)
    {
        _validate = validate;
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
        card.SetBounds(20, 20, 400, 210);
        card.Controls.Add(new Label { Text = title, UseMnemonic = false, Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(22, 20), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label { Text = message, UseMnemonic = false, ForeColor = AppTheme.SubText, Location = new Point(24, 50), Size = new Size(352, 36), Tag = AppTheme.NoTheme });

        _box.Text = initial;
        _field = new RoundedField(_box, 40) { LeadingIcon = IconKind.Folder };
        _field.SetBounds(24, 90, 352, 40);
        card.Controls.Add(_field);

        _error.SetBounds(24, 134, 352, 18);
        _error.ForeColor = AppTheme.Danger;
        _error.Font = new Font("Segoe UI Semibold", 8.75f);
        _error.Tag = AppTheme.NoTheme;
        card.Controls.Add(_error);

        var ok = new PillButton(actionText, primary: true, new Size(110, 38)) { Location = new Point(266, 156) };
        var cancel = new PillButton("Cancel", primary: false, new Size(90, 38)) { Location = new Point(166, 156) };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        card.Controls.Add(ok);
        card.Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(card);
        ClientSize = new Size(440, 250);
        ActiveControl = _box;
        Shown += (_, _) => _box.SelectAll();
    }

    private void Accept()
    {
        var problem = Value.Length == 0 ? "Please enter a name." : _validate?.Invoke(Value);
        if (problem is not null)
        {
            _error.Text = problem;
            _field.HasError = true;
            _box.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
