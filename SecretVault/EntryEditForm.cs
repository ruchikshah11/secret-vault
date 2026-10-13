using System.Drawing.Drawing2D;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Gui;

public class EntryEditForm : Form
{
    private const int PagePad = 20;
    private const int CardPad = 20;
    private const int CardGap = 16;
    private const int FieldHeight = 38;

    private readonly VaultEntry? _existing;
    private readonly string[] _folders;
    private readonly List<CustomFieldRow> _fieldRows = new();
    private readonly List<Panel> _cards = new();
    private readonly Color _canvas = AppTheme.Canvas;

    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true, Tag = AppTheme.NoTheme };
    private readonly Panel _customCard;
    private readonly Panel _fieldsContainer = new() { Tag = AppTheme.NoTheme };
    private readonly Label _customEmptyLabel = new();

    private readonly TextBox _nameBox = new() { PlaceholderText = "e.g. GitHub" };
    private readonly TextBox _folderBox = new() { PlaceholderText = "General" };
    private readonly TextBox _usernameBox = new() { PlaceholderText = "Username or email" };
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true, PlaceholderText = "Password" };
    private readonly TextBox _totpBox = new() { PlaceholderText = "Base32 TOTP secret (optional)" };
    private readonly TextBox _urlBox = new() { PlaceholderText = "https://example.com" };
    private readonly TextBox _tagsBox = new() { PlaceholderText = "e.g. work, banking (comma separated)" };
    private readonly PillButton _typeButton = new("Auto-detect", primary: false, new Size(100, 38)) { Dropdown = true };
    private string? _chosenType; // null = detect from contents
    private readonly IconButton _favoriteButton = new(IconKind.Star, 38) { Circular = true };
    private bool _favorite;
    private CustomFieldRow? _draggingRow;
    private readonly TextBox _notesBox = new() { Multiline = true, AcceptsReturn = true, PlaceholderText = "Anything else worth remembering" };
    private readonly RoundedField _nameField;

    private readonly IconButton _showPasswordButton = new(IconKind.Eye, 30) { Circular = false };
    private readonly IconButton _generateButton = new(IconKind.Dice, 30) { Circular = false };
    private readonly Panel _strengthMeter = new() { Height = 6, Tag = AppTheme.NoTheme };
    private readonly Label _strengthLabel = new();
    private StrengthLevel? _strength;

    private readonly PillButton _saveButton = new("Save", primary: true, new Size(112, 38));
    private readonly PillButton _cancelButton = new("Cancel", primary: false, new Size(100, 38));

    public VaultEntry? Result { get; private set; }

    private readonly string? _defaultFolder;
    private readonly string? _defaultType;

    // defaultFolder / defaultType pre-fill a NEW record (e.g. the folder or record type selected in the main window).
    public EntryEditForm(IEnumerable<string> existingFolders, VaultEntry? existing = null, string? defaultFolder = null, string? defaultType = null)
    {
        _existing = existing;
        _defaultFolder = defaultFolder;
        _defaultType = defaultType;

        Text = existing is null ? "Add Entry" : "Edit Entry";
        Size = new Size(620, 820);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = _canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        _folders = existingFolders.Append("General")
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

        _nameField = new RoundedField(_nameBox, FieldHeight);
        _content.BackColor = _canvas;

        var cardWidth = ClientSize.Width - PagePad * 2 - SystemInformation.VerticalScrollBarWidth;
        BuildLoginCard(cardWidth);
        BuildSecurityCard(cardWidth);
        _customCard = BuildCustomFieldsCard(cardWidth);
        _content.AutoScrollMargin = new Size(0, PagePad);

        Controls.Add(_content);
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeader());

        LoadExisting();
        UpdateTypeButton();
        foreach (var box in new[] { _usernameBox, _passwordBox, _notesBox })
            box.TextChanged += (_, _) => UpdateTypeButton();
        LayoutFieldRows();
        AppTheme.Apply(this);
        ActiveControl = _nameBox;
    }

    // ---------- chrome ----------

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 84, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var badge = new Panel { Size = new Size(44, 44), Location = new Point(24, 20), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        badge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedField.RoundRect(new RectangleF(0, 0, 43, 43), 11);
            using var brush = new SolidBrush(AppTheme.Accent);
            g.FillPath(brush, path);
            Icons.Draw(g, _existing is null ? IconKind.Plus : IconKind.Key, new Rectangle(12, 12, 20, 20), Color.White, 2f, AppTheme.Accent);
        };

        var title = new Label
        {
            Text = _existing is null ? "Add Entry" : "Edit Entry",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(80, 16),
            Tag = AppTheme.NoTheme
        };
        var subtitle = new Label
        {
            Text = _existing is null ? "Save a new login securely in your vault." : "Update the details stored for this login.",
            ForeColor = AppTheme.SubText,
            AutoSize = true,
            Location = new Point(82, 46),
            Tag = AppTheme.NoTheme
        };

        _favoriteButton.Location = new Point(ClientSize.Width - 24 - _favoriteButton.Width, 23);
        _favoriteButton.Click += (_, _) => SetFavorite(!_favorite);
        new ToolTip().SetToolTip(_favoriteButton, "Add to favourites");

        header.Controls.Add(badge);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_favoriteButton);
        return header;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _saveButton.Click += (_, _) => OnSave();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        void LayoutButtons()
        {
            _saveButton.Location = new Point(footer.Width - 24 - _saveButton.Width, 15);
            _cancelButton.Location = new Point(_saveButton.Left - 10 - _cancelButton.Width, 15);
        }
        footer.Resize += (_, _) => LayoutButtons();

        footer.Controls.Add(_cancelButton);
        footer.Controls.Add(_saveButton);
        LayoutButtons();
        return footer;
    }

    // ---------- sections ----------

    private void BuildLoginCard(int cardWidth)
    {
        var card = NewCard("Login details", IconKind.Person, cardWidth, out var y);
        var inner = cardWidth - CardPad * 2;
        const int colGap = 12;

        var nameWidth = (inner - colGap) * 3 / 5;
        AddLabeled(card, "Name *", _nameField, CardPad, y, nameWidth);

        var folderField = new RoundedField(_folderBox, FieldHeight);
        _folderBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _folderBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _folderBox.AutoCompleteCustomSource.AddRange(_folders);
        var chevron = new IconButton(IconKind.ChevronDown, 28) { Circular = false };
        new ToolTip().SetToolTip(chevron, "Choose folder");
        chevron.Click += (_, _) =>
        {
            using var picker = new FolderPickerForm(_folders, _folderBox.Text, "Choose folder", "Select");
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedFolder is not null) _folderBox.Text = picker.SelectedFolder;
        };
        folderField.AddTrailing(chevron);
        y = AddLabeled(card, "Folder", folderField, CardPad + nameWidth + colGap, y, inner - nameWidth - colGap);

        y = AddLabeled(card, "Username", new RoundedField(_usernameBox, FieldHeight) { LeadingIcon = IconKind.Person }, CardPad, y, inner);
        y = AddLabeled(card, "Website", new RoundedField(_urlBox, FieldHeight) { LeadingIcon = IconKind.Globe }, CardPad, y, inner);

        var typeMenu = ModernMenu.Create(showIcons: false, disposeOnClose: false);
        typeMenu.Items.Add("Auto-detect from contents", null, (_, _) => { _chosenType = null; UpdateTypeButton(); });
        typeMenu.Items.Add(new ToolStripSeparator());
        foreach (var type in RecordTypes.All)
            typeMenu.Items.Add(type, null, (_, _) => { _chosenType = type; UpdateTypeButton(); });
        _typeButton.Click += (_, _) => typeMenu.Show(_typeButton, new Point(0, _typeButton.Height + 2));
        y = AddLabeled(card, "Record type", _typeButton, CardPad, y, inner);

        var passwordField = new RoundedField(_passwordBox, FieldHeight);
        _passwordBox.TextChanged += (_, _) => UpdateStrength();
        _showPasswordButton.Click += (_, _) => SetPasswordVisible(_passwordBox.UseSystemPasswordChar);
        _generateButton.Click += (_, _) =>
        {
            using var generator = new GeneratorForm();
            if (generator.ShowDialog(this) != DialogResult.OK || generator.Result is null) return;
            _passwordBox.Text = generator.Result;
            SetPasswordVisible(true);
        };
        passwordField.AddTrailing(_showPasswordButton);
        passwordField.AddTrailing(_generateButton);
        new ToolTip().SetToolTip(_showPasswordButton, "Show / hide password");
        new ToolTip().SetToolTip(_generateButton, "Password generator");
        y = AddLabeled(card, "Password", passwordField, CardPad, y, inner) - 4;

        const int labelWidth = 80;
        _strengthMeter.Location = new Point(CardPad, y + 6);
        _strengthMeter.Width = inner - labelWidth - 8;
        _strengthMeter.BackColor = AppTheme.CardBg;
        _strengthMeter.Paint += PaintStrengthMeter;
        _strengthLabel.Tag = AppTheme.NoTheme;
        _strengthLabel.AutoSize = false;
        _strengthLabel.TextAlign = ContentAlignment.MiddleRight;
        _strengthLabel.Font = new Font("Segoe UI Semibold", 8.5f);
        _strengthLabel.SetBounds(CardPad + inner - labelWidth, y, labelWidth, 18);
        card.Controls.Add(_strengthMeter);
        card.Controls.Add(_strengthLabel);
        y += 18;

        card.Height = y + CardPad;
    }

    private void BuildSecurityCard(int cardWidth)
    {
        var card = NewCard("Security, tags & notes", IconKind.Shield, cardWidth, out var y);
        var inner = cardWidth - CardPad * 2;

        y = AddLabeled(card, "Two-factor (TOTP) secret", new RoundedField(_totpBox, FieldHeight) { LeadingIcon = IconKind.Lock }, CardPad, y, inner);
        y = AddLabeled(card, "Tags", new RoundedField(_tagsBox, FieldHeight) { LeadingIcon = IconKind.Tag }, CardPad, y, inner);
        y = AddLabeled(card, "Notes", new RoundedField(_notesBox, 92), CardPad, y, inner);

        card.Height = y - 14 + CardPad;
    }

    private Panel BuildCustomFieldsCard(int cardWidth)
    {
        var card = NewCard("Custom fields", IconKind.Edit, cardWidth, out var y);
        var inner = cardWidth - CardPad * 2;

        var addButton = new PillButton("Add field", primary: false, new Size(104, 30)) { LeadingIcon = IconKind.Plus };
        addButton.Location = new Point(cardWidth - CardPad - addButton.Width, CardPad - 6);
        addButton.Click += (_, _) => AddFieldRow(new CustomField { Type = CustomFieldType.Text });
        card.Controls.Add(addButton);

        _customEmptyLabel.Text = "Add extra details such as PINs, security questions or account numbers.";
        _customEmptyLabel.Tag = AppTheme.NoTheme;
        _customEmptyLabel.ForeColor = AppTheme.SubText;
        _customEmptyLabel.AutoSize = true;
        _customEmptyLabel.Location = new Point(CardPad, y);
        card.Controls.Add(_customEmptyLabel);

        _fieldsContainer.BackColor = AppTheme.CardBg;
        _fieldsContainer.Location = new Point(CardPad, y);
        _fieldsContainer.Size = new Size(inner, 0);
        card.Controls.Add(_fieldsContainer);
        return card;
    }

    // ---------- building blocks ----------

    // A rounded section card with an icon + title row; `y` returns the first free row inside it.
    private Panel NewCard(string title, IconKind icon, int width, out int y)
    {
        var card = new Panel { Width = width, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_canvas);
            using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, card.Width - 1.5f, card.Height - 1.5f), 12);
            using var fill = new SolidBrush(AppTheme.CardBg);
            using var pen = new Pen(AppTheme.Border);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            Icons.Draw(g, icon, new Rectangle(CardPad, CardPad + 2, 16, 16), AppTheme.Accent, 1.7f, AppTheme.CardBg);
        };
        card.Resize += (_, _) => card.Invalidate();

        card.Controls.Add(new Label
        {
            Text = title,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(CardPad + 24, CardPad - 1),
            Tag = AppTheme.NoTheme
        });

        _cards.Add(card);
        _content.Controls.Add(card);
        y = CardPad + 40;
        return card;
    }

    private static int AddLabeled(Panel card, string label, Control field, int x, int y, int width)
    {
        card.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI Semibold", 9f),
            ForeColor = AppTheme.SubText,
            AutoSize = true,
            Location = new Point(x - 1, y),
            Tag = AppTheme.NoTheme
        });

        field.Location = new Point(x, y + 22);
        field.Width = width;
        card.Controls.Add(field);
        return field.Bottom + 16;
    }

    private void LayoutCards()
    {
        var scroll = _content.AutoScrollPosition;
        var y = PagePad;
        foreach (var card in _cards)
        {
            card.Location = new Point(PagePad + scroll.X, y + scroll.Y);
            y += card.Height + CardGap;
        }
    }

    private void SetPasswordVisible(bool visible)
    {
        _passwordBox.UseSystemPasswordChar = !visible;
        _showPasswordButton.Icon = visible ? IconKind.EyeOff : IconKind.Eye;
        _showPasswordButton.Invalidate();
    }

    // ---------- password strength ----------

    private void UpdateStrength()
    {
        _strength = _passwordBox.Text.Length == 0 ? null : PasswordStrength.Evaluate(_passwordBox.Text);
        _strengthLabel.Text = _strength switch
        {
            null => string.Empty,
            StrengthLevel.VeryWeak => "Very weak",
            StrengthLevel.VeryStrong => "Very strong",
            var level => level.ToString()
        };
        _strengthLabel.ForeColor = _strength is { } l ? AppTheme.StrengthColor(l) : AppTheme.SubText;
        _strengthMeter.Invalidate();
    }

    private void PaintStrengthMeter(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.CardBg);

        const int segments = 5;
        const int gap = 4;
        var segWidth = (_strengthMeter.Width - gap * (segments - 1)) / (float)segments;
        var filled = _strength is { } level ? (int)level + 1 : 0;
        using var on = new SolidBrush(_strength is { } l ? AppTheme.StrengthColor(l) : AppTheme.Border);
        using var off = new SolidBrush(AppTheme.Border);

        for (var i = 0; i < segments; i++)
        {
            using var path = RoundedField.RoundRect(new RectangleF(i * (segWidth + gap), 0, segWidth, _strengthMeter.Height - 1), 2.5f);
            g.FillPath(i < filled ? on : off, path);
        }
    }

    // ---------- custom fields ----------

    private void AddFieldRow(CustomField field)
    {
        var row = new CustomFieldRow(field.Label, field.Value, field.Type) { Width = _fieldsContainer.Width };
        row.RemoveRequested += (_, _) => RemoveFieldRow(row);
        row.SizeAdjusted += (_, _) => LayoutFieldRows();
        row.DragHandle.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _draggingRow = row; };
        row.DragHandle.MouseUp += (_, _) => _draggingRow = null;
        row.DragHandle.MouseMove += (_, _) => DragRowToCursor();
        _fieldRows.Add(row);
        _fieldsContainer.Controls.Add(row);
        AppTheme.Apply(_fieldsContainer);
        LayoutFieldRows();
    }

    // Moves the dragged row to the slot under the cursor (rows' midpoints decide the order).
    private void DragRowToCursor()
    {
        if (_draggingRow is null || Control.MouseButtons != MouseButtons.Left) { _draggingRow = null; return; }
        var y = _fieldsContainer.PointToClient(Cursor.Position).Y;
        var others = _fieldRows.Where(r => r != _draggingRow).ToList();
        var target = others.Count(r => r.Top + r.Height / 2 < y);
        var current = _fieldRows.IndexOf(_draggingRow);
        if (target == current) return;
        _fieldRows.RemoveAt(current);
        _fieldRows.Insert(target, _draggingRow);
        LayoutFieldRows();
    }

    // Shows the chosen type, or "Auto-detect · <what it would be>" based on the current field values.
    private void UpdateTypeButton()
    {
        if (_chosenType is not null)
        {
            _typeButton.Text = _chosenType;
            return;
        }
        var probe = new VaultEntry
        {
            Username = _usernameBox.Text, Secret = _passwordBox.Text, Notes = _notesBox.Text,
            CustomFields = _fieldRows.Select(r => r.ToCustomField()).ToList()
        };
        _typeButton.Text = $"Auto-detect  ·  {RecordTypes.Detect(probe)}";
    }

    private void SetFavorite(bool favorite)
    {
        _favorite = favorite;
        _favoriteButton.Icon = favorite ? IconKind.StarFilled : IconKind.Star;
        _favoriteButton.IconColor = favorite ? Color.FromArgb(245, 166, 35) : AppTheme.SubText;
        _favoriteButton.Invalidate();
    }

    private void RemoveFieldRow(CustomFieldRow row)
    {
        _fieldsContainer.Controls.Remove(row);
        _fieldRows.Remove(row);
        row.Dispose();
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

        _fieldsContainer.Height = y;
        UpdateTypeButton();
        _customEmptyLabel.Visible = _fieldRows.Count == 0;
        var bottom = _fieldRows.Count == 0 ? _customEmptyLabel.Bottom : _fieldsContainer.Bottom - 8;
        _customCard.Height = bottom + CardPad;
        LayoutCards();
    }

    // ---------- load / save ----------

    private void LoadExisting()
    {
        if (_existing is null)
        {
            _folderBox.Text = string.IsNullOrWhiteSpace(_defaultFolder) ? "General" : _defaultFolder;
            _chosenType = _defaultType;
            return;
        }

        _nameBox.Text = _existing.Name;
        _folderBox.Text = _existing.Folder;
        _usernameBox.Text = _existing.Username;
        _passwordBox.Text = _existing.Secret;
        _notesBox.Text = _existing.Notes;
        _totpBox.Text = _existing.TotpSecret ?? string.Empty;
        _urlBox.Text = _existing.Url;
        _chosenType = string.IsNullOrWhiteSpace(_existing.Type) ? null : _existing.Type;
        _tagsBox.Text = string.Join(", ", _existing.Tags);
        SetFavorite(_existing.Favorite);

        foreach (var field in _existing.CustomFields)
            AddFieldRow(field);

        UpdateStrength();
    }

    private void OnSave()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _nameField.HasError = true;
            _content.ScrollControlIntoView(_nameField);
            _nameBox.Focus();
            return;
        }

        // Dates, password history and defaults are applied by SecretVault.Core, shared with the Android app.
        Result = VaultOperations.Stamp(new VaultEntry
        {
            Name = _nameBox.Text,
            Folder = _folderBox.Text,
            Username = _usernameBox.Text.Trim(),
            Secret = _passwordBox.Text,
            Notes = _notesBox.Text.Trim(),
            TotpSecret = _totpBox.Text,
            Url = _urlBox.Text.Trim(),
            Type = _chosenType,
            Favorite = _favorite,
            Tags = VaultOperations.SplitTags(_tagsBox.Text),
            CustomFields = _fieldRows.Select(r => r.ToCustomField()).ToList()
        }, _existing);
        DialogResult = DialogResult.OK;
        Close();
    }
}
