using System.Drawing.Drawing2D;
using SecretVault.Models;

namespace SecretVault.Gui;

public class IdentityPaymentsPanel : Panel
{
    private const int PagePadX = 28;
    private const int CardPad = 24;
    private const int MaxCardWidth = 760;
    private const int FieldHeight = 38;

    private readonly VaultData _data;
    private readonly Func<Task> _save;

    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 84, Tag = AppTheme.NoTheme };
    private readonly Panel _tabContent = new() { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };
    private readonly Panel _tabBar = new() { Dock = DockStyle.Top, Height = 44, Tag = AppTheme.NoTheme };
    private readonly Label _personalTab = new() { Text = "Personal Info", AutoSize = true, Tag = AppTheme.NoTheme, Cursor = Cursors.Hand };
    private readonly Label _cardsTab = new() { Text = "Payment Cards", AutoSize = true, Tag = AppTheme.NoTheme, Cursor = Cursors.Hand };
    private bool _showingCards;
    private bool _justSaved;

    private readonly NavListView _cardListBox = new() { ShowScrollbar = true };
    private PaymentCard? _selectedCard;
    private Panel _cardDetailContent = null!;
    private int _renderedDetailWidth;

    private readonly Font _listTitleFont = new("Segoe UI Semibold", 9.5f);
    private readonly Font _listSubFont = new("Segoe UI", 8.5f);
    private readonly Font _avatarFont = new("Segoe UI", 14f, FontStyle.Bold);

    public IdentityPaymentsPanel(VaultData data, Func<Task> save)
    {
        _data = data;
        _save = save;
        Tag = AppTheme.NoTheme;
        BuildLayout();
    }

    private void BuildLayout()
    {
        _header.Controls.Add(new Label
        {
            Text = "Identity & Payments",
            UseMnemonic = false,
            Font = new Font("Segoe UI", 17f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(PagePadX - 3, 18),
            Tag = AppTheme.NoTheme
        });
        _header.Controls.Add(new Label
        {
            Text = "Your personal details and payment cards, stored encrypted and ready to autofill.",
            AutoSize = true,
            Location = new Point(PagePadX - 1, 54),
            Tag = AppTheme.NoTheme
        });

        foreach (var tab in new[] { _personalTab, _cardsTab })
        {
            tab.Font = new Font("Segoe UI Semibold", 10f);
            _tabBar.Controls.Add(tab);
        }
        _personalTab.Click += (_, _) => { _showingCards = false; RefreshView(); };
        _cardsTab.Click += (_, _) => { _showingCards = true; RefreshView(); };
        _tabBar.Paint += PaintTabBar;
        _tabBar.Resize += (_, _) => _tabBar.Invalidate();

        Controls.Add(_tabContent);
        Controls.Add(_tabBar);
        Controls.Add(_header);
    }

    private void PaintTabBar(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        using var line = new Pen(AppTheme.Border);
        g.DrawLine(line, 0, _tabBar.Height - 1, _tabBar.Width, _tabBar.Height - 1);

        var active = _showingCards ? _cardsTab : _personalTab;
        using var accent = new SolidBrush(AppTheme.Accent);
        g.FillRectangle(accent, active.Left - 4, _tabBar.Height - 3, active.Width + 8, 3);
    }

    public void RefreshView()
    {
        var canvas = AppTheme.Canvas;
        BackColor = canvas;
        foreach (Control c in Controls) c.BackColor = canvas;
        foreach (Control c in _header.Controls)
            c.ForeColor = c.Font.Size > 12 ? AppTheme.Text : AppTheme.SubText;

        _personalTab.Location = new Point(PagePadX, 12);
        _cardsTab.Location = new Point(_personalTab.Right + 28, 12);
        _personalTab.ForeColor = !_showingCards ? AppTheme.Accent : AppTheme.SubText;
        _cardsTab.ForeColor = _showingCards ? AppTheme.Accent : AppTheme.SubText;
        _tabBar.Invalidate();

        _tabContent.SuspendLayout();
        _cardListBox.Parent?.Controls.Remove(_cardListBox); // reused across refreshes; keep it out of the disposal below
        foreach (Control c in _tabContent.Controls.Cast<Control>().ToList()) c.Dispose();
        if (_showingCards) BuildCardsView();
        else BuildPersonalInfoView();
        _tabContent.ResumeLayout();

        AppTheme.Apply(this);
    }

    // ---------- Personal Info ----------

    private void BuildPersonalInfoView()
    {
        var info = _data.PersonalInfoRecords.FirstOrDefault();
        if (info is null)
        {
            BuildEmptyState(_tabContent, IconKind.Person, "No personal info yet",
                "Securely store your name, phone numbers and addresses to fill online forms in seconds.",
                "Add Personal Info", () =>
                {
                    _data.PersonalInfoRecords.Add(new PersonalInfo { CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow });
                    RefreshView();
                });
            return;
        }

        var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        var card = NewCard();
        outer.Controls.Add(card);

        var usernameBox = new TextBox { Text = info.Username, PlaceholderText = "e.g. jane.doe" };
        var phoneBox = new TextBox { Text = string.Join(Environment.NewLine, info.PhoneNumbers), Multiline = true, AcceptsReturn = true, PlaceholderText = "+1 555 0100" };
        var addrBox = new TextBox { Text = string.Join(Environment.NewLine, info.Addresses), Multiline = true, AcceptsReturn = true, PlaceholderText = "221B Baker Street, London" };
        var usernameField = new RoundedField(usernameBox, FieldHeight);
        var phoneField = new RoundedField(phoneBox, 120);
        var addrField = new RoundedField(addrBox, 120);

        // Profile header: initials avatar + title.
        var avatar = new Panel { Size = new Size(52, 52), Location = new Point(CardPad, CardPad), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        avatar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new LinearGradientBrush(avatar.ClientRectangle, AppTheme.Accent, AppTheme.RailBg, 45f);
            g.FillEllipse(brush, 0, 0, 51, 51);
            var initials = Initials(usernameBox.Text);
            if (initials.Length == 0)
                Icons.Draw(g, IconKind.Person, new Rectangle(15, 14, 22, 22), Color.White, 2f, AppTheme.Accent);
            else
                TextRenderer.DrawText(g, initials, _avatarFont, new Rectangle(0, 0, 52, 52), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        usernameBox.TextChanged += (_, _) => avatar.Invalidate();
        card.Controls.Add(avatar);
        card.Controls.Add(new Label { Text = "Personal details", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(CardPad + 66, CardPad + 3), Tag = AppTheme.NoTheme });
        var updated = info.ModifiedAt == default ? "Not saved yet" : $"Last updated {info.ModifiedAt.ToLocalTime():MMM d, yyyy 'at' h:mm tt}";
        card.Controls.Add(new Label { Text = updated, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(CardPad + 67, CardPad + 30), Tag = AppTheme.NoTheme });

        var divider = new Panel { Height = 1, Location = new Point(0, CardPad + 52 + 20), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        card.Controls.Add(divider);

        var top = divider.Bottom + 22;
        var usernameLabel = FieldLabel("Username", required: true);
        var phoneLabel = FieldLabel("Phone numbers", hint: "one per line");
        var addrLabel = FieldLabel("Addresses", hint: "one per line");
        card.Controls.AddRange(new Control[] { usernameLabel, usernameField, phoneLabel, phoneField, addrLabel, addrField });

        var saveBtn = new PillButton("Save changes", primary: true, new Size(132, 38));
        var cancelBtn = new PillButton("Discard", primary: false, new Size(96, 38));
        var savedLabel = new Label { Text = "✓  Changes saved", ForeColor = AppTheme.Current.StrengthVeryStrong, AutoSize = true, Visible = _justSaved, Tag = AppTheme.NoTheme, Font = new Font("Segoe UI Semibold", 9f) };
        card.Controls.AddRange(new Control[] { saveBtn, cancelBtn, savedLabel });
        if (_justSaved)
        {
            _justSaved = false;
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, _) => { timer.Dispose(); if (!savedLabel.IsDisposed) savedLabel.Visible = false; };
            timer.Start();
        }

        cancelBtn.Click += (_, _) => RefreshView();
        saveBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(usernameBox.Text))
            {
                usernameField.HasError = true;
                usernameBox.Focus();
                return;
            }
            info.Username = usernameBox.Text.Trim();
            info.PhoneNumbers = SplitLines(phoneBox.Text);
            info.Addresses = SplitLines(addrBox.Text);
            info.ModifiedAt = DateTimeOffset.UtcNow;
            await _save();
            _justSaved = true;
            RefreshView();
        };

        void LayoutCard()
        {
            var width = Math.Min(MaxCardWidth, outer.ClientSize.Width - PagePadX * 2);
            width = Math.Max(width, 360);
            card.Location = new Point(PagePadX + outer.AutoScrollPosition.X, 24 + outer.AutoScrollPosition.Y);
            card.Width = width;
            var inner = width - CardPad * 2;
            divider.Width = width;

            var y = top;
            usernameLabel.Location = new Point(CardPad - 1, y);
            usernameField.SetBounds(CardPad, y + 22, inner, FieldHeight);
            y = usernameField.Bottom + 20;

            // Phone numbers and addresses side by side when there is room, stacked otherwise.
            if (inner >= 520)
            {
                var colWidth = (inner - 16) / 2;
                phoneLabel.Location = new Point(CardPad - 1, y);
                phoneField.SetBounds(CardPad, y + 22, colWidth, 120);
                addrLabel.Location = new Point(CardPad + colWidth + 16 - 1, y);
                addrField.SetBounds(CardPad + colWidth + 16, y + 22, colWidth, 120);
                y = phoneField.Bottom + 28;
            }
            else
            {
                phoneLabel.Location = new Point(CardPad - 1, y);
                phoneField.SetBounds(CardPad, y + 22, inner, 120);
                y = phoneField.Bottom + 20;
                addrLabel.Location = new Point(CardPad - 1, y);
                addrField.SetBounds(CardPad, y + 22, inner, 120);
                y = addrField.Bottom + 28;
            }

            saveBtn.Location = new Point(width - CardPad - saveBtn.Width, y);
            cancelBtn.Location = new Point(saveBtn.Left - 10 - cancelBtn.Width, y);
            savedLabel.Location = new Point(CardPad, y + (saveBtn.Height - savedLabel.Height) / 2);
            card.Height = saveBtn.Bottom + CardPad;
            card.Invalidate();
        }

        outer.Resize += (_, _) => LayoutCard();
        _tabContent.Controls.Add(outer);
        LayoutCard();
    }

    private static string Initials(string name)
    {
        var parts = name.Split(new[] { ' ', '.', '_', '-', '@' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant()
        };
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---------- Payment Cards ----------

    private void BuildCardsView()
    {
        var content = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };

        if (_data.PaymentCards.Count == 0)
        {
            BuildEmptyState(_tabContent, IconKind.Card, "No payment cards yet",
                "Add your credit and debit cards so checkout forms can be filled automatically.",
                "Add Card", async () => await AddCardAsync());
            return;
        }

        var listColumn = new Panel { Dock = DockStyle.Left, Width = 300, Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas, Padding = new Padding(PagePadX, 20, 12, 12) };
        var addBtn = new PillButton("Add Card", primary: true, new Size(260, 40)) { LeadingIcon = IconKind.Plus, Dock = DockStyle.Top };
        addBtn.Click += async (_, _) => await AddCardAsync();

        _cardListBox.Dock = DockStyle.Fill;
        _cardListBox.ItemHeight = 64;
        _cardListBox.Tag = AppTheme.NoTheme;
        _cardListBox.BackColor = AppTheme.Canvas;
        _cardListBox.DrawItem -= CardListBox_DrawItem;
        _cardListBox.DrawItem += CardListBox_DrawItem;
        _cardListBox.SelectedIndexChanged -= CardListBox_SelectedIndexChanged;
        _cardListBox.SelectedIndexChanged += CardListBox_SelectedIndexChanged;

        _cardListBox.Items.Clear();
        foreach (var card in _data.PaymentCards)
            _cardListBox.Items.Add(card);

        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        listWrap.Controls.Add(_cardListBox);
        listColumn.Controls.Add(listWrap);
        listColumn.Controls.Add(addBtn);

        _cardDetailContent = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        _cardDetailContent.Resize += (_, _) =>
        {
            if (_cardDetailContent.ClientSize.Width != _renderedDetailWidth) RenderCardDetail(_selectedCard);
        };

        content.Controls.Add(_cardDetailContent);
        content.Controls.Add(listColumn);
        _tabContent.Controls.Add(content);

        _selectedCard = _selectedCard is not null && _data.PaymentCards.Contains(_selectedCard) ? _selectedCard : _data.PaymentCards.FirstOrDefault();
        if (_selectedCard is not null) _cardListBox.SelectedItem = _selectedCard;
        RenderCardDetail(_selectedCard);
    }

    private async Task AddCardAsync()
    {
        using var editForm = new PaymentCardEditForm();
        if (editForm.ShowDialog(FindForm()) != DialogResult.OK || editForm.Result is null) return;
        _data.PaymentCards.Add(editForm.Result);
        _selectedCard = editForm.Result;
        await _save();
        RefreshView();
    }

    private void CardListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _selectedCard = _cardListBox.SelectedItem as PaymentCard;
        RenderCardDetail(_selectedCard);
    }

    private void CardListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var card = (PaymentCard)_cardListBox.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(AppTheme.Canvas))
            g.FillRectangle(bg, e.Bounds);

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var tile = new RectangleF(e.Bounds.Left, e.Bounds.Top + 3, e.Bounds.Width - 1, e.Bounds.Height - 7);
        using (var path = RoundedField.RoundRect(tile, 10))
        {
            using var fill = new SolidBrush(selected ? AppTheme.Selection : AppTheme.CardBg);
            var hovered = (e.State & DrawItemState.HotLight) == DrawItemState.HotLight;
            using var pen = new Pen(selected ? AppTheme.Accent : hovered ? UiKit.Blend(AppTheme.Accent, AppTheme.Border, 0.45f) : AppTheme.Border, selected ? 1.4f : 1f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        // Mini card thumbnail in the brand's colours.
        var thumb = new RectangleF(e.Bounds.Left + 12, e.Bounds.Top + 18, 40, 27);
        var brand = CardBrands.Resolve(card);
        var (c1, c2) = CardBrands.Colors(brand);
        using (var thumbPath = RoundedField.RoundRect(thumb, 4))
        using (var brush = new LinearGradientBrush(thumb, c1, c2, 35f))
            g.FillPath(brush, thumbPath);
        if (brand == CardBrand.Unknown)
        {
            using var chip = new SolidBrush(Color.FromArgb(230, 200, 120));
            g.FillRectangle(chip, thumb.Left + 6, thumb.Top + 9, 8, 6);
        }
        else
        {
            CardBrands.DrawLogo(g, new RectangleF(thumb.Left + 4, thumb.Top + 7, thumb.Width - 8, thumb.Height - 14), brand);
        }

        var textLeft = e.Bounds.Left + 64;
        var name = string.IsNullOrWhiteSpace(card.CardholderName) ? "Unnamed card" : card.CardholderName;
        TextRenderer.DrawText(g, name, _listTitleFont,
            new Rectangle(textLeft, e.Bounds.Top + 12, e.Bounds.Width - textLeft - 10, 20), AppTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, $"{CardBrands.ShortName(brand)} •••• {Last4(card.CardNumber)}", _listSubFont,
            new Rectangle(textLeft, e.Bounds.Top + 33, e.Bounds.Width - textLeft - 10, 18), AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    private void RenderCardDetail(PaymentCard? card)
    {
        _cardDetailContent.SuspendLayout();
        foreach (Control c in _cardDetailContent.Controls.Cast<Control>().ToList()) c.Dispose();
        _renderedDetailWidth = _cardDetailContent.ClientSize.Width;

        if (card is null)
        {
            _cardDetailContent.Controls.Add(new Label { Text = "Select a card to see its details.", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(24, 24), Tag = AppTheme.NoTheme });
            _cardDetailContent.ResumeLayout();
            return;
        }

        var x = 20;
        var width = Math.Min(560, Math.Max(340, _renderedDetailWidth - x - PagePadX));
        var y = 20;

        // Title row with actions.
        var title = new Label
        {
            Text = string.IsNullOrWhiteSpace(card.CardholderName) ? "Unnamed card" : card.CardholderName,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoEllipsis = true,
            Location = new Point(x - 2, y),
            Size = new Size(width - 150, 32),
            Tag = AppTheme.NoTheme
        };
        var editBtn = new PillButton("Edit", primary: false, new Size(84, 34)) { LeadingIcon = IconKind.Edit, Location = new Point(x + width - 84 - 44, y) };
        var deleteBtn = new IconButton(IconKind.Trash, 34) { IconColor = AppTheme.Danger, Location = new Point(x + width - 34, y) };
        new ToolTip().SetToolTip(deleteBtn, "Delete card");
        editBtn.Click += async (_, _) =>
        {
            using var editForm = new PaymentCardEditForm(card);
            if (editForm.ShowDialog(FindForm()) != DialogResult.OK || editForm.Result is null) return;
            var index = _data.PaymentCards.IndexOf(card);
            _data.PaymentCards[index] = editForm.Result;
            _selectedCard = editForm.Result;
            await _save();
            RefreshView();
        };
        deleteBtn.Click += async (_, _) =>
        {
            if (!AppDialog.Confirm(FindForm(), "Delete this card?", $"\"{card.CardholderName}\" will be permanently removed from your vault.",
                    "Delete card", danger: true)) return;
            VaultOperations.DeleteCard(_data, card);
            _selectedCard = null;
            await _save();
            RefreshView();
        };
        _cardDetailContent.Controls.AddRange(new Control[] { title, editBtn, deleteBtn });
        y += 52;

        // Visual card.
        var visual = new Panel { Location = new Point(x, y), Size = new Size(340, 204), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        visual.Paint += (_, e) => PaintCardVisual(e.Graphics, visual.ClientRectangle, card);
        _cardDetailContent.Controls.Add(visual);
        y = visual.Bottom + 24;

        // Details card.
        var details = new Panel { Location = new Point(x, y), Width = width, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        details.Paint += (_, e) => PaintCardChrome(e.Graphics, details);
        var rowY = 8;
        rowY = AddDetailRow(details, "Card number", FormatCardNumber(card.CardNumber), rowY, masked: true, maskedText: $"•••• •••• •••• {Last4(card.CardNumber)}");
        rowY = AddDetailRow(details, "Expiration date", card.ExpirationDate, rowY, masked: false);
        rowY = AddDetailRow(details, "Security code", card.SecurityCode, rowY, masked: true, maskedText: "•••", last: string.IsNullOrEmpty(card.Notes));
        if (!string.IsNullOrEmpty(card.Notes))
            rowY = AddDetailRow(details, "Notes", card.Notes, rowY, masked: false, last: true);
        details.Height = rowY + 8;
        _cardDetailContent.Controls.Add(details);
        _cardDetailContent.Controls.Add(new Panel { Location = new Point(x, details.Bottom), Size = new Size(1, 24), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas });

        _cardDetailContent.ResumeLayout();
    }

    private int AddDetailRow(Panel host, string label, string value, int y, bool masked, string? maskedText = null, bool last = false)
    {
        const int rowHeight = 62;
        var width = host.Width;

        host.Controls.Add(new Label { Text = label, ForeColor = AppTheme.SubText, Font = new Font("Segoe UI Semibold", 8.5f), AutoSize = true, Location = new Point(CardPad - 1, y + 10), Tag = AppTheme.NoTheme });
        var valueLabel = new Label
        {
            Text = string.IsNullOrEmpty(value) ? "—" : masked ? maskedText : value,
            UseMnemonic = false,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 11f),
            AutoEllipsis = true,
            Location = new Point(CardPad - 2, y + 28),
            Size = new Size(width - CardPad * 2 - 80, 24),
            Tag = AppTheme.NoTheme
        };
        host.Controls.Add(valueLabel);

        var copyBtn = new IconButton(IconKind.Copy, 32) { Circular = false, Location = new Point(width - CardPad - 32 + 6, y + 15) };
        new ToolTip().SetToolTip(copyBtn, $"Copy {label.ToLowerInvariant()}");
        copyBtn.Click += (_, _) => { if (!string.IsNullOrEmpty(value)) Clipboard.SetText(value); };
        copyBtn.Enabled = !string.IsNullOrEmpty(value);
        host.Controls.Add(copyBtn);

        if (masked && !string.IsNullOrEmpty(value))
        {
            var revealed = false;
            var showBtn = new IconButton(IconKind.Eye, 32) { Circular = false, Location = new Point(copyBtn.Left - 34, y + 15) };
            new ToolTip().SetToolTip(showBtn, "Show / hide");
            showBtn.Click += (_, _) =>
            {
                revealed = !revealed;
                valueLabel.Text = revealed ? value : maskedText;
                showBtn.Icon = revealed ? IconKind.EyeOff : IconKind.Eye;
                showBtn.Invalidate();
            };
            host.Controls.Add(showBtn);
        }

        if (!last)
            host.Controls.Add(new Panel { Location = new Point(CardPad, y + rowHeight - 1), Size = new Size(width - CardPad * 2, 1), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border });

        return y + rowHeight;
    }

    private static void PaintCardVisual(Graphics g, Rectangle bounds, PaymentCard card)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(AppTheme.Canvas);

        var rect = new RectangleF(0, 0, bounds.Width - 1, bounds.Height - 1);
        var brand = CardBrands.Resolve(card);
        var (c1, c2) = CardBrands.Colors(brand);
        using var path = RoundedField.RoundRect(rect, 16);
        using (var brush = new LinearGradientBrush(rect, c1, c2, 35f))
            g.FillPath(brush, path);

        // Soft decorative circles for depth.
        g.SetClip(path);
        using (var glow = new SolidBrush(Color.FromArgb(28, Color.White)))
        {
            g.FillEllipse(glow, rect.Width * 0.55f, -rect.Height * 0.45f, rect.Width * 0.8f, rect.Width * 0.8f);
            g.FillEllipse(glow, -rect.Width * 0.3f, rect.Height * 0.55f, rect.Width * 0.7f, rect.Width * 0.7f);
        }
        g.ResetClip();

        // Chip.
        using (var chipPath = RoundedField.RoundRect(new RectangleF(24, 62, 42, 32), 6))
        using (var chip = new LinearGradientBrush(new RectangleF(24, 62, 42, 32), Color.FromArgb(240, 214, 140), Color.FromArgb(200, 160, 70), 45f))
            g.FillPath(chip, chipPath);

        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(190, Color.White));
        using var numberFont = new Font("Consolas", 16f, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI Semibold", 10.5f);

        CardBrands.DrawLogo(g, new RectangleF(rect.Width - 22 - 120, 16, 120, 30), brand);

        g.DrawString($"••••  ••••  ••••  {Last4(card.CardNumber)}", numberFont, white, 22, 110);

        g.DrawString("CARDHOLDER", smallFont, soft, 24, 152);
        var holder = string.IsNullOrWhiteSpace(card.CardholderName) ? "—" : card.CardholderName.ToUpperInvariant();
        g.DrawString(holder, valueFont, white, new RectangleF(22, 166, rect.Width - 130, 22), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

        g.DrawString("EXPIRES", smallFont, soft, rect.Width - 92, 152);
        g.DrawString(string.IsNullOrWhiteSpace(card.ExpirationDate) ? "—" : card.ExpirationDate, valueFont, white, rect.Width - 94, 166);
    }

    private static string Last4(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static string FormatCardNumber(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? number : string.Join(" ", digits.Chunk(4).Select(c => new string(c)));
    }

    // ---------- shared building blocks ----------

    private static Panel NewCard()
    {
        var card = new Panel { Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        card.Paint += (_, e) => PaintCardChrome(e.Graphics, card);
        card.Resize += (_, _) => card.Invalidate();
        return card;
    }

    private static void PaintCardChrome(Graphics g, Control card)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.Canvas);
        using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, card.Width - 1.5f, card.Height - 1.5f), 14);
        using var fill = new SolidBrush(AppTheme.CardBg);
        using var pen = new Pen(AppTheme.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    private static Label FieldLabel(string text, bool required = false, string? hint = null)
    {
        var label = new Label { AutoSize = true, Tag = AppTheme.NoTheme, Font = new Font("Segoe UI Semibold", 9f), ForeColor = AppTheme.SubText, UseMnemonic = false };
        label.Text = text + (required ? " *" : "") + (hint is null ? "" : $"  ·  {hint}");
        return label;
    }

    private static void BuildEmptyState(Panel host, IconKind icon, string title, string description, string actionText, Action onAction)
    {
        var placeholder = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        var wrap = new Panel { Size = new Size(440, 270), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };

        var badge = new Panel { Size = new Size(76, 76), Location = new Point((440 - 76) / 2, 0), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        badge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(AppTheme.Selection);
            g.FillEllipse(bg, 0, 0, 75, 75);
            Icons.Draw(g, icon, new Rectangle(23, 23, 30, 30), AppTheme.Accent, 2f, AppTheme.Selection);
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 92),
            Size = new Size(440, 32),
            Tag = AppTheme.NoTheme
        };
        var desc = new Label
        {
            Text = description,
            ForeColor = AppTheme.SubText,
            TextAlign = ContentAlignment.TopCenter,
            Location = new Point(20, 130),
            Size = new Size(400, 44),
            Tag = AppTheme.NoTheme
        };
        var addBtn = new PillButton(actionText, primary: true, new Size(200, 42)) { LeadingIcon = IconKind.Plus, Location = new Point(120, 192) };
        addBtn.Click += (_, _) => onAction();

        wrap.Controls.AddRange(new Control[] { badge, titleLabel, desc, addBtn });
        placeholder.Controls.Add(wrap);
        placeholder.Resize += (_, _) =>
            wrap.Location = new Point(Math.Max(0, (placeholder.Width - wrap.Width) / 2), Math.Max(24, (placeholder.Height - wrap.Height) / 3));
        host.Controls.Add(placeholder);
    }
}
