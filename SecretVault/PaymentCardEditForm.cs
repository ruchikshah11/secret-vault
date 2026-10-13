using System.Drawing.Drawing2D;
using SecretVault.Models;

namespace SecretVault.Gui;

public class PaymentCardEditForm : Form
{
    private const int PagePad = 20;
    private const int CardPad = 20;
    private const int FieldHeight = 38;

    private readonly PaymentCard? _existing;

    private readonly TextBox _cardholderBox = new() { PlaceholderText = "Name as shown on card" };
    private readonly TextBox _numberBox = new() { PlaceholderText = "1234 5678 9012 3456" };
    private readonly TextBox _expiryBox = new() { PlaceholderText = "MM/YY", MaxLength = 7 };
    private readonly TextBox _cvvBox = new() { UseSystemPasswordChar = true, PlaceholderText = "CVV", MaxLength = 4 };
    private readonly TextBox _notesBox = new() { Multiline = true, AcceptsReturn = true, PlaceholderText = "Optional" };
    private readonly RoundedField _cardholderField;
    private readonly RoundedField _numberField;
    private readonly PillButton _brandButton;
    private readonly Panel _brandBadge = new() { Size = new Size(46, 28), Tag = AppTheme.NoTheme };
    private CardBrand _chosenBrand = CardBrand.Unknown; // Unknown = auto-detect

    public PaymentCard? Result { get; private set; }

    public PaymentCardEditForm(PaymentCard? existing = null)
    {
        _existing = existing;

        Text = existing is null ? "Add Payment Card" : "Edit Payment Card";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        const int formWidth = 520;
        var cardWidth = formWidth - PagePad * 2;
        var inner = cardWidth - CardPad * 2;

        // Header
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
            Icons.Draw(g, IconKind.Card, new Rectangle(11, 11, 22, 22), Color.White, 1.8f, AppTheme.Accent);
        };
        header.Controls.Add(badge);
        header.Controls.Add(new Label { Text = Text, Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(80, 16), Tag = AppTheme.NoTheme });
        header.Controls.Add(new Label { Text = "Card details are encrypted and only visible after unlocking.", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(82, 46), Tag = AppTheme.NoTheme });

        // Body card
        var card = UiKit.NewCard();
        card.Location = new Point(PagePad, PagePad);
        card.Width = cardWidth;
        var y = CardPad;

        _cardholderField = new RoundedField(_cardholderBox, FieldHeight) { LeadingIcon = IconKind.Person };
        y = AddLabeled(card, "Cardholder name", _cardholderField, CardPad, y, inner, required: true);

        _numberField = new RoundedField(_numberBox, FieldHeight) { LeadingIcon = IconKind.Card };
        _numberBox.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != ' ') e.Handled = true; };
        _numberBox.Leave += (_, _) => _numberBox.Text = FormatCardNumber(_numberBox.Text);
        _numberBox.TextChanged += (_, _) => UpdateBrand();
        _brandBadge.BackColor = AppTheme.Field;
        _brandBadge.Paint += (_, e) => PaintBrandBadge(e.Graphics);
        _numberField.AddTrailing(_brandBadge);
        y = AddLabeled(card, "Card number", _numberField, CardPad, y, inner, required: true);

        // Card type: auto-detect by default, or pick the network explicitly.
        _brandButton = new PillButton("Auto-detect", primary: false, new Size(inner, FieldHeight)) { Dropdown = true };
        var brandMenu = ModernMenu.Create(showIcons: false, disposeOnClose: false);
        brandMenu.Items.Add("Auto-detect from number", null, (_, _) => { _chosenBrand = CardBrand.Unknown; UpdateBrand(); });
        brandMenu.Items.Add(new ToolStripSeparator());
        foreach (var b in CardBrands.Selectable)
            brandMenu.Items.Add(CardBrands.DisplayName(b), null, (_, _) => { _chosenBrand = b; UpdateBrand(); });
        _brandButton.Click += (_, _) => brandMenu.Show(_brandButton, new Point(0, _brandButton.Height + 2));
        y = AddLabeled(card, "Card type", _brandButton, CardPad, y, inner);

        var half = (inner - 12) / 2;
        AddLabeled(card, "Expiration date", new RoundedField(_expiryBox, FieldHeight), CardPad, y, half);
        var cvvField = new RoundedField(_cvvBox, FieldHeight);
        var showCvv = new IconButton(IconKind.Eye, 30) { Circular = false };
        showCvv.Click += (_, _) =>
        {
            _cvvBox.UseSystemPasswordChar = !_cvvBox.UseSystemPasswordChar;
            showCvv.Icon = _cvvBox.UseSystemPasswordChar ? IconKind.Eye : IconKind.EyeOff;
            showCvv.Invalidate();
        };
        cvvField.AddTrailing(showCvv);
        y = AddLabeled(card, "Security code", cvvField, CardPad + half + 12, y, inner - half - 12);

        y = AddLabeled(card, "Notes", new RoundedField(_notesBox, 76), CardPad, y, inner);
        card.Height = y - 16 + CardPad;

        var body = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        body.Controls.Add(card);

        // Footer
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var saveButton = new PillButton("Save card", primary: true, new Size(120, 38));
        var cancelButton = new PillButton("Cancel", primary: false, new Size(100, 38));
        saveButton.Location = new Point(formWidth - 24 - saveButton.Width - 16, 15);
        cancelButton.Location = new Point(saveButton.Left - 10 - cancelButton.Width, 15);
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        saveButton.Click += (_, _) => OnSave();
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(saveButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        ClientSize = new Size(formWidth - 16, header.Height + PagePad + card.Height + PagePad + footer.Height);
        saveButton.Location = new Point(ClientSize.Width - 24 - saveButton.Width, 15);
        cancelButton.Location = new Point(saveButton.Left - 10 - cancelButton.Width, 15);
        card.Width = ClientSize.Width - PagePad * 2;

        if (_existing is not null)
        {
            _cardholderBox.Text = _existing.CardholderName;
            _numberBox.Text = FormatCardNumber(_existing.CardNumber);
            _expiryBox.Text = _existing.ExpirationDate;
            _cvvBox.Text = _existing.SecurityCode;
            _notesBox.Text = _existing.Notes;
            Enum.TryParse(_existing.Brand, ignoreCase: true, out _chosenBrand);
        }
        UpdateBrand();

        AppTheme.Apply(this);
        ActiveControl = _cardholderBox;
    }

    private CardBrand EffectiveBrand => _chosenBrand != CardBrand.Unknown ? _chosenBrand : CardBrands.Detect(_numberBox.Text);

    private void UpdateBrand()
    {
        var detected = CardBrands.Detect(_numberBox.Text);
        _brandButton.Text = _chosenBrand != CardBrand.Unknown
            ? CardBrands.DisplayName(_chosenBrand)
            : detected == CardBrand.Unknown ? "Auto-detect" : $"Auto-detect  ·  {CardBrands.DisplayName(detected)}";
        _brandBadge.Visible = EffectiveBrand != CardBrand.Unknown;
        _brandBadge.Invalidate();
    }

    // Mini card in the brand colours with its mark, shown inside the number field.
    private void PaintBrandBadge(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.Field);
        var brand = EffectiveBrand;
        var rect = new RectangleF(0, 0, _brandBadge.Width - 1, _brandBadge.Height - 1);
        var (c1, c2) = CardBrands.Colors(brand);
        using (var path = RoundedField.RoundRect(rect, 5))
        using (var brush = new LinearGradientBrush(rect, c1, c2, 35f))
            g.FillPath(brush, path);
        CardBrands.DrawLogo(g, new RectangleF(5, 7, rect.Width - 10, rect.Height - 14), brand);
    }

    private static int AddLabeled(Panel card, string label, Control field, int x, int y, int width, bool required = false)
    {
        card.Controls.Add(UiKit.Caption(label, new Point(x - 1, y), required));
        field.Location = new Point(x, y + 22);
        field.Width = width;
        field.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(field);
        return field.Bottom + 16;
    }

    private static string FormatCardNumber(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? number.Trim() : string.Join(" ", digits.Chunk(4).Select(c => new string(c)));
    }

    private void OnSave()
    {
        var missingName = string.IsNullOrWhiteSpace(_cardholderBox.Text);
        var missingNumber = string.IsNullOrWhiteSpace(_numberBox.Text);
        if (missingName || missingNumber)
        {
            _cardholderField.HasError = missingName;
            _numberField.HasError = missingNumber;
            (missingName ? _cardholderBox : _numberBox).Focus();
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
            Brand = _chosenBrand == CardBrand.Unknown ? string.Empty : _chosenBrand.ToString(),
            Notes = _notesBox.Text.Trim(),
            CreatedAt = _existing?.CreatedAt ?? now,
            ModifiedAt = now
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
