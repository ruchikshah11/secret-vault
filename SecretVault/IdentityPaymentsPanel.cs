using SecretVault.Models;

namespace SecretVault.Gui;

public class IdentityPaymentsPanel : Panel
{
    private readonly VaultData _data;
    private readonly Func<Task> _save;

    private readonly Panel _tabContent = new() { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };
    private readonly Button _personalTab = new() { Text = "Personal Info", AutoSize = true, Tag = AppTheme.NoTheme };
    private readonly Button _cardsTab = new() { Text = "Payment Cards", AutoSize = true, Tag = AppTheme.NoTheme };
    private bool _showingCards;

    private readonly ListBox _cardListBox = new();
    private PaymentCard? _selectedCard;
    private Panel _cardDetailPanel = null!;
    private Panel _cardDetailContent = null!;

    public IdentityPaymentsPanel(VaultData data, Func<Task> save)
    {
        _data = data;
        _save = save;
        Tag = AppTheme.NoTheme;
        BuildLayout();
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 56, Tag = AppTheme.NoTheme, Padding = new Padding(24, 18, 24, 0) };
        var titleLabel = new Label
        {
            Text = "Identity & Payments",
            UseMnemonic = false,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        header.Controls.Add(titleLabel);

        var tabBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Tag = AppTheme.NoTheme, Padding = new Padding(20, 0, 0, 0) };
        StyleTabButton(_personalTab);
        StyleTabButton(_cardsTab);
        _personalTab.Click += (_, _) => { _showingCards = false; RefreshView(); };
        _cardsTab.Click += (_, _) => { _showingCards = true; RefreshView(); };
        tabBar.Controls.Add(_personalTab);
        tabBar.Controls.Add(_cardsTab);
        var tabDivider = new Panel { Dock = DockStyle.Top, Height = 1, Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };

        Controls.Add(_tabContent);
        Controls.Add(tabDivider);
        Controls.Add(tabBar);
        Controls.Add(header);
    }

    private static void StyleTabButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.Font = new Font("Segoe UI", 9.5f);
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(0, 6, 24, 0);
    }

    public void RefreshView()
    {
        BackColor = AppTheme.Shell;
        _tabContent.Controls.Clear();

        _personalTab.ForeColor = !_showingCards ? AppTheme.Accent : AppTheme.SubText;
        _personalTab.Font = new Font("Segoe UI", 9.5f, !_showingCards ? FontStyle.Bold : FontStyle.Regular);
        _cardsTab.ForeColor = _showingCards ? AppTheme.Accent : AppTheme.SubText;
        _cardsTab.Font = new Font("Segoe UI", 9.5f, _showingCards ? FontStyle.Bold : FontStyle.Regular);

        if (_showingCards) BuildCardsView();
        else BuildPersonalInfoView();

        AppTheme.Apply(this);
    }

    // ---------- Personal Info ----------

    private void BuildPersonalInfoView()
    {
        var info = _data.PersonalInfoRecords.FirstOrDefault();
        var content = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };

        if (info is null)
        {
            var placeholder = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };
            var icon = new Panel { Size = new Size(64, 64), Location = new Point(0, 60), Anchor = AnchorStyles.Top, Tag = AppTheme.NoTheme };
            icon.Paint += (_, e) =>
            {
                using var bg = new SolidBrush(AppTheme.Field);
                e.Graphics.FillRectangle(bg, icon.ClientRectangle);
                Icons.Draw(e.Graphics, IconKind.Person, new Rectangle(16, 16, 32, 32), AppTheme.SubText, 1.6f);
            };
            var desc = new Label
            {
                Text = "Securely store Personal Information such as your name, phone numbers and addresses to quickly fill online forms.",
                ForeColor = AppTheme.SubText,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 140),
                Size = new Size(420, 50)
            };
            var addBtn = new Button { Text = "+ Add Personal Info", AutoSize = true, Location = new Point(0, 200) };
            addBtn.Click += (_, _) =>
            {
                _data.PersonalInfoRecords.Add(new PersonalInfo { CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow });
                RefreshView();
            };

            var wrap = new Panel { Size = new Size(420, 260), Anchor = AnchorStyles.None };
            wrap.Controls.Add(icon);
            wrap.Controls.Add(desc);
            wrap.Controls.Add(addBtn);
            placeholder.Controls.Add(wrap);
            placeholder.Resize += (_, _) =>
                wrap.Location = new Point((placeholder.Width - wrap.Width) / 2, Math.Max(20, (placeholder.Height - wrap.Height) / 3));
            content.Controls.Add(placeholder);
        }
        else
        {
            var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), Tag = AppTheme.NoTheme };

            var card = new Panel { Location = new Point(0, 0), Size = new Size(480, 460), Padding = new Padding(28, 24, 28, 24), Tag = AppTheme.RoleCard };
            card.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(AppTheme.Border, 1.2f);
                using var path = RoundedRectPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
                e.Graphics.DrawPath(pen, path);
            };
            var cardHost = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };
            card.Controls.Add(cardHost);

            var y = 0;
            var width = cardHost.Width;

            var sectionLabel = new Label { Text = "Personal Details", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(0, y) };
            cardHost.Controls.Add(sectionLabel);

            var saveBtn = new Button { Text = "Save", AutoSize = false, Size = new Size(90, 34) };
            var cancelBtn = new Button { Text = "Cancel", AutoSize = false, Size = new Size(90, 34) };
            StylePillButton(saveBtn, primary: true);
            StylePillButton(cancelBtn, primary: false);
            cancelBtn.Click += (_, _) => RefreshView();
            cardHost.Controls.Add(saveBtn);
            cardHost.Controls.Add(cancelBtn);
            saveBtn.Location = new Point(width - saveBtn.Width, y);
            cancelBtn.Location = new Point(saveBtn.Left - 8 - cancelBtn.Width, y);
            y += 40;

            y = AddCardDivider(cardHost, width, y);

            var usernameLbl = new Label { Text = "Username (Required)", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
            cardHost.Controls.Add(usernameLbl);
            y += 20;
            var usernameBox = new TextBox { Text = info.Username, Size = new Size(width, 26), Location = new Point(0, y), BorderStyle = BorderStyle.FixedSingle };
            cardHost.Controls.Add(usernameBox);
            y += 44;

            var phoneLbl = new Label { Text = "Phone Numbers (one per line)", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
            cardHost.Controls.Add(phoneLbl);
            y += 20;
            var phoneBox = new TextBox
            {
                Text = string.Join(Environment.NewLine, info.PhoneNumbers),
                Multiline = true,
                Size = new Size(width, 66),
                Location = new Point(0, y),
                BorderStyle = BorderStyle.FixedSingle
            };
            cardHost.Controls.Add(phoneBox);
            y += 82;

            var addrLbl = new Label { Text = "Addresses (one per line)", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
            cardHost.Controls.Add(addrLbl);
            y += 20;
            var addrBox = new TextBox
            {
                Text = string.Join(Environment.NewLine, info.Addresses),
                Multiline = true,
                Size = new Size(width, 80),
                Location = new Point(0, y),
                BorderStyle = BorderStyle.FixedSingle
            };
            cardHost.Controls.Add(addrBox);

            saveBtn.Click += async (_, _) =>
            {
                info.Username = usernameBox.Text.Trim();
                info.PhoneNumbers = SplitLines(phoneBox.Text);
                info.Addresses = SplitLines(addrBox.Text);
                info.ModifiedAt = DateTimeOffset.UtcNow;
                await _save();
                RefreshView();
            };

            content.Controls.Add(outer);
            outer.Controls.Add(card);
        }

        _tabContent.Controls.Add(content);
    }

    private static int AddCardDivider(Panel host, int width, int y)
    {
        y += 6;
        var line = new Panel { Location = new Point(0, y), Size = new Size(width, 1), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        host.Controls.Add(line);
        return y + 16;
    }

    private static void StylePillButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Tag = AppTheme.NoTheme;
        if (primary)
        {
            b.BackColor = AppTheme.Gold;
            b.ForeColor = Color.Black;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            AppTheme.ThemeChanged += () => b.BackColor = AppTheme.Gold;
        }
        else
        {
            b.BackColor = AppTheme.CardBg;
            b.ForeColor = AppTheme.Text;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = AppTheme.Border;
            AppTheme.ThemeChanged += () =>
            {
                b.BackColor = AppTheme.CardBg;
                b.ForeColor = AppTheme.Text;
                b.FlatAppearance.BorderColor = AppTheme.Border;
            };
        }
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---------- Payment Cards ----------

    private void BuildCardsView()
    {
        var content = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.NoTheme };

        var listColumn = new Panel { Dock = DockStyle.Left, Width = 300, Tag = AppTheme.NoTheme, Padding = new Padding(20, 12, 12, 12) };
        var addBtn = new Button { Text = "+ Add Card", Dock = DockStyle.Top, Height = 36, BackColor = AppTheme.Gold, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Tag = AppTheme.NoTheme, Cursor = Cursors.Hand };
        addBtn.FlatAppearance.BorderSize = 0;
        addBtn.Click += async (_, _) =>
        {
            using var editForm = new PaymentCardEditForm();
            if (editForm.ShowDialog(FindForm()) != DialogResult.OK || editForm.Result is null) return;
            _data.PaymentCards.Add(editForm.Result);
            await _save();
            RefreshView();
        };

        _cardListBox.Dock = DockStyle.Fill;
        _cardListBox.BorderStyle = BorderStyle.None;
        _cardListBox.DrawMode = DrawMode.OwnerDrawFixed;
        _cardListBox.ItemHeight = 50;
        _cardListBox.IntegralHeight = false;
        _cardListBox.Tag = AppTheme.NoTheme;
        _cardListBox.BackColor = AppTheme.Shell;
        _cardListBox.DrawItem -= CardListBox_DrawItem;
        _cardListBox.DrawItem += CardListBox_DrawItem;
        _cardListBox.SelectedIndexChanged -= CardListBox_SelectedIndexChanged;
        _cardListBox.SelectedIndexChanged += CardListBox_SelectedIndexChanged;

        _cardListBox.Items.Clear();
        foreach (var card in _data.PaymentCards)
            _cardListBox.Items.Add(card);

        var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0), Tag = AppTheme.NoTheme };
        listWrap.Controls.Add(_cardListBox);
        listColumn.Controls.Add(listWrap);
        listColumn.Controls.Add(addBtn);
        var divider = new Panel { Dock = DockStyle.Left, Width = 1, Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };

        _cardDetailPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 24), Tag = AppTheme.RoleCard };
        _cardDetailContent = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Tag = AppTheme.NoTheme };
        _cardDetailPanel.Controls.Add(_cardDetailContent);

        content.Controls.Add(_cardDetailPanel);
        content.Controls.Add(divider);
        content.Controls.Add(listColumn);
        _tabContent.Controls.Add(content);

        _selectedCard = _data.PaymentCards.FirstOrDefault();
        if (_selectedCard is not null) _cardListBox.SelectedItem = _selectedCard;
        RenderCardDetail(_selectedCard);
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var bg = new SolidBrush(selected ? AppTheme.Selection : AppTheme.Shell))
            g.FillRectangle(bg, e.Bounds);

        var last4 = card.CardNumber.Length >= 4 ? card.CardNumber[^4..] : card.CardNumber;
        var masked = $"•••• {last4}";

        TextRenderer.DrawText(g, card.CardholderName, new Font(_cardListBox.Font, FontStyle.Bold),
            new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 8, e.Bounds.Width - 24, 20), AppTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, masked, _cardListBox.Font,
            new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 28, e.Bounds.Width - 24, 18), AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

        using var linePen = new Pen(AppTheme.Border);
        g.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void RenderCardDetail(PaymentCard? card)
    {
        _cardDetailContent.Controls.Clear();

        if (card is null)
        {
            _cardDetailContent.Controls.Add(new Label { Text = "Select a card, or add a new one.", ForeColor = AppTheme.SubText, AutoSize = true });
            return;
        }

        var width = _cardDetailContent.Width;
        var y = 0;

        var toolbar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Location = new Point(0, y), Size = new Size(width, 30), Tag = AppTheme.RoleCard };
        var deleteBtn = new IconButton(IconKind.Trash, 28) { IconColor = AppTheme.Danger };
        deleteBtn.Click += async (_, _) =>
        {
            var confirm = MessageBox.Show(FindForm(), $"Delete card '{card.CardholderName}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            _data.PaymentCards.Remove(card);
            await _save();
            RefreshView();
        };
        var editBtn = new IconButton(IconKind.Edit, 28) { IconColor = AppTheme.SubText };
        editBtn.Click += async (_, _) =>
        {
            using var editForm = new PaymentCardEditForm(card);
            if (editForm.ShowDialog(FindForm()) != DialogResult.OK || editForm.Result is null) return;
            var index = _data.PaymentCards.IndexOf(card);
            _data.PaymentCards[index] = editForm.Result;
            await _save();
            RefreshView();
        };
        toolbar.Controls.Add(deleteBtn);
        toolbar.Controls.Add(editBtn);
        _cardDetailContent.Controls.Add(toolbar);
        y += 44;

        var title = new Label { Text = card.CardholderName, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(0, y) };
        _cardDetailContent.Controls.Add(title);
        y += 40;

        y = AddDetailField("Card Number", card.CardNumber, width, y, masked: true);
        y = AddDetailField("Expiration Date", card.ExpirationDate, width, y, masked: false);
        y = AddDetailField("Security Code", card.SecurityCode, width, y, masked: true);
        if (!string.IsNullOrEmpty(card.Notes))
            AddDetailField("Notes", card.Notes, width, y, masked: false);

        AppTheme.Apply(_cardDetailPanel);
    }

    private int AddDetailField(string label, string value, int width, int y, bool masked)
    {
        var lbl = new Label { Text = label, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, y) };
        _cardDetailContent.Controls.Add(lbl);
        y += 20;

        var box = new TextBox
        {
            Text = value,
            ReadOnly = true,
            UseSystemPasswordChar = masked,
            BorderStyle = BorderStyle.None,
            BackColor = AppTheme.CardBg,
            ForeColor = AppTheme.Text,
            Tag = AppTheme.NoTheme,
            Location = new Point(0, y),
            Size = new Size(masked ? width - 40 : width, 22)
        };
        _cardDetailContent.Controls.Add(box);

        if (masked)
        {
            var showBtn = new IconButton(IconKind.Eye, 28) { Location = new Point(width - 32, y - 3), Circular = false };
            showBtn.Click += (_, _) =>
            {
                box.UseSystemPasswordChar = !box.UseSystemPasswordChar;
                showBtn.Icon = box.UseSystemPasswordChar ? IconKind.Eye : IconKind.EyeOff;
                showBtn.Invalidate();
            };
            _cardDetailContent.Controls.Add(showBtn);
        }

        return y + 36;
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
