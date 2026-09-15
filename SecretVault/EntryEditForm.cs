using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Gui;

public class EntryEditForm : Form
{
    private readonly VaultEntry? _existing;
    private readonly List<CustomFieldRow> _fieldRows = new();

    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 20, 28, 20), Tag = AppTheme.RoleShell };
    private readonly Panel _fieldsHost = new() { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };
    private readonly Panel _fieldsContainer = new() { Tag = AppTheme.RoleShell };

    private readonly TextBox _nameBox = new();
    private readonly ComboBox _folderCombo = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly IconButton _showPasswordButton = new(IconKind.Eye, 30) { Circular = false };
    private readonly IconButton _generateButton = new(IconKind.Dice, 30) { Circular = false };
    private readonly Label _strengthLabel = new();
    private readonly Panel _strengthBarTrack = new() { Height = 4, BackColor = AppTheme.Border };
    private readonly Panel _strengthBarFill = new() { Height = 4 };
    private readonly TextBox _notesBox = new() { Multiline = true, Height = 60 };
    private readonly TextBox _totpBox = new() { PlaceholderText = "Base32 TOTP secret (optional)" };
    private readonly Button _addFieldButton = new() { Text = "+ Add Custom Field" };

    private readonly Button _saveButton = new() { Text = "Save", AutoSize = false, Size = new Size(96, 36) };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = false, Size = new Size(96, 36) };

    public VaultEntry? Result { get; private set; }

    public EntryEditForm(IEnumerable<string> existingFolders, VaultEntry? existing = null)
    {
        _existing = existing;

        Text = existing is null ? "Add Entry" : "Edit Entry";
        Size = new Size(600, 800);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.Shell;
        Font = new Font("Segoe UI", 9.5f);

        var folders = existingFolders.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        _folderCombo.Items.AddRange(folders);

        _content.Controls.Add(_fieldsHost);
        BuildContent();

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 64, Tag = AppTheme.RoleShell };
        var bottomDivider = new Panel { Dock = DockStyle.Top, Height = 1, Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        bottomBar.Controls.Add(bottomDivider);

        StyleSecondaryButton(_cancelButton);
        StylePrimaryButton(_saveButton);
        _cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _saveButton.Click += (_, _) => OnSave();

        void LayoutButtons()
        {
            _saveButton.Location = new Point(bottomBar.Width - 24 - _saveButton.Width, 14);
            _cancelButton.Location = new Point(_saveButton.Left - 8 - _cancelButton.Width, 14);
        }
        bottomBar.Resize += (_, _) => LayoutButtons();

        bottomBar.Controls.Add(_cancelButton);
        bottomBar.Controls.Add(_saveButton);
        LayoutButtons();

        Controls.Add(_content);
        Controls.Add(bottomBar);

        LoadExisting();
        AppTheme.Apply(this);
        _strengthBarTrack.BackColor = AppTheme.Border;
    }

    private static void StylePrimaryButton(Button b)
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

    private static void StyleSecondaryButton(Button b)
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

    private void BuildContent()
    {
        var y = 0;
        var width = _fieldsHost.Width;

        y = AddLabeledControl("Name", _nameBox, width, y);
        y = AddLabeledControl("Folder", _folderCombo, width, y);
        y = AddLabeledControl("Username", _usernameBox, width, y);

        var pwLabel = new Label { Text = "Password", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        _fieldsHost.Controls.Add(pwLabel);
        y += 20;

        _passwordBox.Location = new Point(0, y);
        _passwordBox.Size = new Size(width - 76, 26);
        _passwordBox.BorderStyle = BorderStyle.FixedSingle;
        _passwordBox.TextChanged += (_, _) => UpdateStrength();
        _showPasswordButton.Location = new Point(width - 68, y - 2);
        _showPasswordButton.Click += (_, _) =>
        {
            _passwordBox.UseSystemPasswordChar = !_passwordBox.UseSystemPasswordChar;
            _showPasswordButton.Icon = _passwordBox.UseSystemPasswordChar ? IconKind.Eye : IconKind.EyeOff;
            _showPasswordButton.Invalidate();
        };
        _generateButton.Location = new Point(width - 34, y - 2);
        _generateButton.Click += (_, _) =>
        {
            _passwordBox.Text = PasswordGenerator.Generate(20);
            _passwordBox.UseSystemPasswordChar = false;
            _showPasswordButton.Icon = IconKind.EyeOff;
            _showPasswordButton.Invalidate();
        };
        _fieldsHost.Controls.Add(_passwordBox);
        _fieldsHost.Controls.Add(_showPasswordButton);
        _fieldsHost.Controls.Add(_generateButton);
        y += 30;

        _strengthBarTrack.Location = new Point(0, y);
        _strengthBarTrack.Width = width;
        _strengthBarFill.Location = new Point(0, y);
        _strengthBarFill.Height = 4;
        _fieldsHost.Controls.Add(_strengthBarFill);
        _fieldsHost.Controls.Add(_strengthBarTrack);
        _strengthBarFill.BringToFront();
        y += 10;

        _strengthLabel.ForeColor = AppTheme.SubText;
        _strengthLabel.AutoSize = true;
        _strengthLabel.Location = new Point(0, y);
        _fieldsHost.Controls.Add(_strengthLabel);
        y += 28;

        y = AddDivider(width, y);
        y = AddLabeledControl("Notes", _notesBox, width, y);
        y = AddLabeledControl("TOTP Secret", _totpBox, width, y);

        y = AddDivider(width, y);
        var fieldsLabel = new Label { Text = "Custom Fields", ForeColor = AppTheme.Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) };
        _fieldsHost.Controls.Add(fieldsLabel);
        y += 30;

        _addFieldButton.Location = new Point(0, y);
        _addFieldButton.AutoSize = true;
        _addFieldButton.FlatStyle = FlatStyle.Flat;
        _addFieldButton.FlatAppearance.BorderColor = AppTheme.Border;
        _addFieldButton.ForeColor = AppTheme.Accent;
        _addFieldButton.Click += (_, _) => AddFieldRow(new CustomField { Type = CustomFieldType.Text });
        _fieldsHost.Controls.Add(_addFieldButton);
        y += 40;

        _fieldsContainer.Location = new Point(0, y);
        _fieldsContainer.Size = new Size(width, 300);
        _fieldsHost.Controls.Add(_fieldsContainer);
    }

    private int AddDivider(int width, int y)
    {
        y += 4;
        var line = new Panel { Location = new Point(0, y), Size = new Size(width, 1), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        _fieldsHost.Controls.Add(line);
        return y + 16;
    }

    private int AddLabeledControl(string label, Control control, int width, int y)
    {
        var lbl = new Label { Text = label, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        _fieldsHost.Controls.Add(lbl);
        y += 20;

        control.Location = new Point(0, y);
        control.Size = new Size(width, control.Height > 0 ? control.Height : 26);
        if (control is TextBox { Multiline: false })
            ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
        _fieldsHost.Controls.Add(control);

        return y + control.Height + 14;
    }

    private void UpdateStrength()
    {
        var level = PasswordStrength.Evaluate(_passwordBox.Text);
        _strengthLabel.Text = _passwordBox.Text.Length == 0 ? string.Empty : level.ToString();
        _strengthBarFill.BackColor = AppTheme.StrengthColor(level);

        var fraction = (int)level + 1;
        _strengthBarFill.Width = string.IsNullOrEmpty(_passwordBox.Text)
            ? 0
            : _strengthBarTrack.Width * fraction / 5;
    }

    private void AddFieldRow(CustomField field)
    {
        var row = new CustomFieldRow(field.Label, field.Value, field.Type) { Width = _fieldsContainer.Width };
        row.RemoveRequested += (_, _) => RemoveFieldRow(row);
        row.SizeAdjusted += (_, _) => LayoutFieldRows();
        _fieldRows.Add(row);
        _fieldsContainer.Controls.Add(row);
        LayoutFieldRows();
    }

    private void RemoveFieldRow(CustomFieldRow row)
    {
        _fieldsContainer.Controls.Remove(row);
        _fieldRows.Remove(row);
        LayoutFieldRows();
    }

    private void LayoutFieldRows()
    {
        var y = 0;
        foreach (var row in _fieldRows)
        {
            row.Location = new Point(0, y);
            y += row.Height + row.Margin.Vertical;
        }
    }

    private void LoadExisting()
    {
        if (_existing is null)
        {
            _folderCombo.Text = "General";
            return;
        }

        _nameBox.Text = _existing.Name;
        _folderCombo.Text = _existing.Folder;
        _usernameBox.Text = _existing.Username;
        _passwordBox.Text = _existing.Secret;
        _notesBox.Text = _existing.Notes;
        _totpBox.Text = _existing.TotpSecret ?? string.Empty;

        foreach (var field in _existing.CustomFields)
            AddFieldRow(field);

        UpdateStrength();
    }

    private void OnSave()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new VaultEntry
        {
            Name = _nameBox.Text.Trim(),
            Folder = string.IsNullOrWhiteSpace(_folderCombo.Text) ? "General" : _folderCombo.Text.Trim(),
            Username = _usernameBox.Text.Trim(),
            Secret = _passwordBox.Text,
            Notes = _notesBox.Text.Trim(),
            TotpSecret = string.IsNullOrWhiteSpace(_totpBox.Text) ? null : _totpBox.Text.Trim(),
            CustomFields = _fieldRows.Select(r => r.ToCustomField()).ToList(),
            CreatedAt = _existing?.CreatedAt ?? now,
            ModifiedAt = now
        };

        Result = entry;
        DialogResult = DialogResult.OK;
        Close();
    }
}
