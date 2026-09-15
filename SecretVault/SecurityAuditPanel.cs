using System.Drawing.Drawing2D;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Gui;

public class SecurityAuditPanel : Panel
{
    private enum ListFilter { All, Weak, Reused }

    private readonly VaultData _data;
    private readonly string _masterPassword;
    private readonly Action _viewAllRecords;

    private bool _showingList;
    private ListFilter _filter = ListFilter.All;
    private bool _sortByStrength = true;

    private readonly ListBox _recordListBox = new();

    public SecurityAuditPanel(VaultData data, string masterPassword, Action viewAllRecords)
    {
        _data = data;
        _masterPassword = masterPassword;
        _viewAllRecords = viewAllRecords;
        Tag = AppTheme.NoTheme;
        AutoScroll = true;
        Padding = new Padding(32);
    }

    public void RefreshData()
    {
        Controls.Clear();
        BackColor = AppTheme.Shell;

        if (_showingList)
            BuildDrillDownList();
        else
            BuildDashboard();

        AppTheme.Apply(this);
    }

    private void BuildDashboard()
    {
        var entries = _data.Entries.Where(e => e.DeletedAt is null).ToList();
        var total = Math.Max(1, entries.Count);

        var weakCount = entries.Count(e => PasswordStrength.Evaluate(e.Secret) <= StrengthLevel.Fair);
        var secretGroups = entries.Where(e => !string.IsNullOrEmpty(e.Secret)).GroupBy(e => e.Secret).ToList();
        var reusedCount = secretGroups.Where(g => g.Count() > 1).Sum(g => g.Count());
        var has2Fa = entries.Any(e => e.TotpSecret is not null);
        var masterStrong = PasswordStrength.Evaluate(_masterPassword) >= StrengthLevel.Strong;

        var weakPenalty = (double)weakCount / total * 40;
        var reusedPenalty = (double)reusedCount / total * 30;
        var baseScore = Math.Clamp(100 - weakPenalty - reusedPenalty, 0, 70);
        var bonus = (masterStrong ? 15 : 0) + (has2Fa ? 15 : 0);
        var score = (int)Math.Clamp(baseScore + bonus, 0, 100);

        var (scoreLabel, scoreColor) = score switch
        {
            >= 80 => ("Strong", AppTheme.StrengthColor(StrengthLevel.Strong)),
            >= 60 => ("Fair", AppTheme.StrengthColor(StrengthLevel.Fair)),
            _ => ("Weak", AppTheme.StrengthColor(StrengthLevel.Weak))
        };

        var title = new Label
        {
            Text = "Security Audit",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(0, 0)
        };
        Controls.Add(title);

        var scoreCard = MakeCard(new Point(0, 44), new Size(920, 220));
        var gauge = new Panel { Size = new Size(160, 160), Location = new Point(24, 30), Tag = AppTheme.NoTheme };
        gauge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(10, 10, 138, 138);
            using var trackPen = new Pen(AppTheme.Border, 14);
            e.Graphics.DrawArc(trackPen, rect, 0, 360);
            using var scorePen = new Pen(scoreColor, 14) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawArc(scorePen, rect, -90, 360f * score / 100f);

            TextRenderer.DrawText(e.Graphics, $"{score}%", new Font("Segoe UI", 22, FontStyle.Bold),
                new Rectangle(0, 48, 160, 40), AppTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, scoreLabel, new Font("Segoe UI", 10),
                new Rectangle(0, 92, 160, 24), AppTheme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        };
        scoreCard.Controls.Add(gauge);

        var scoreTitle = new Label
        {
            Text = "Overall Security Score",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(210, 30)
        };
        var scoreDesc = new Label
        {
            Text = "Your Security Score summarizes the strength of your SecretVault. Review the strength of your passwords and take action to minimize risks and vulnerabilities.",
            ForeColor = AppTheme.SubText,
            AutoSize = false,
            Size = new Size(650, 60),
            Location = new Point(210, 60)
        };
        var viewAllLink = MakeLinkButton("View All records", new Point(790, 170));
        viewAllLink.Click += (_, _) => ShowList(ListFilter.All);
        scoreCard.Controls.Add(scoreTitle);
        scoreCard.Controls.Add(scoreDesc);
        scoreCard.Controls.Add(viewAllLink);
        Controls.Add(scoreCard);

        var row1 = new Point(0, 280);
        var masterCard = MakeCard(row1, new Size(450, 60));
        AddStatusRow(masterCard, masterStrong, "Strong Master Password", masterStrong ? "Your master password is strong." : "Consider using a longer, more random master password.");
        var totpCard = MakeCard(new Point(470, 280), new Size(450, 60));
        AddStatusRow(totpCard, has2Fa, "Active 2FA Method", has2Fa ? "At least one entry has TOTP enabled." : "No entries have a TOTP code configured.");
        Controls.Add(masterCard);
        Controls.Add(totpCard);

        var weakCard = MakeCard(new Point(0, 356), new Size(450, 160));
        BuildCountCard(weakCard, IconKind.Key, weakCount, "Weak Passwords",
            "\"Weak\" and \"Fair\" passwords are easy to guess. Change passwords on the affected accounts as soon as possible.",
            "View Weak Passwords", ListFilter.Weak);
        var reusedCard = MakeCard(new Point(470, 356), new Size(450, 160));
        BuildCountCard(reusedCard, IconKind.Duplicate, reusedCount, "Reused Passwords",
            "If one account with a reused password is breached, all the other accounts become at risk as well.",
            "View Reused Passwords", ListFilter.Reused);
        Controls.Add(weakCard);
        Controls.Add(reusedCard);
    }

    private void ShowList(ListFilter filter)
    {
        _filter = filter;
        _showingList = true;
        RefreshData();
    }

    // ---------- Drill-down list ----------

    private void BuildDrillDownList()
    {
        var backBtn = new IconButton(IconKind.Back, 30) { Location = new Point(0, 0), IconColor = AppTheme.SubText };
        backBtn.Click += (_, _) => { _showingList = false; RefreshData(); };
        var title = new Label
        {
            Text = "Security Audit",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(38, 4)
        };
        Controls.Add(backBtn);
        Controls.Add(title);

        var sortLabel = new Label { Text = "Sort by:", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(0, 48) };
        var sortCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(60, 44), Width = 150 };
        sortCombo.Items.AddRange(new object[] { "Password Strength", "Name" });
        sortCombo.SelectedIndex = _sortByStrength ? 0 : 1;
        sortCombo.SelectedIndexChanged += (_, _) => { _sortByStrength = sortCombo.SelectedIndex == 0; RefreshData(); };

        var showLabel = new Label { Text = "Show:", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(230, 48) };
        var showCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(280, 44), Width = 150 };
        showCombo.Items.AddRange(new object[] { "All Records", "Weak Passwords", "Reused Passwords" });
        showCombo.SelectedIndex = _filter switch { ListFilter.Weak => 1, ListFilter.Reused => 2, _ => 0 };
        showCombo.SelectedIndexChanged += (_, _) =>
        {
            _filter = showCombo.SelectedIndex switch { 1 => ListFilter.Weak, 2 => ListFilter.Reused, _ => ListFilter.All };
            RefreshData();
        };

        Controls.Add(sortLabel);
        Controls.Add(sortCombo);
        Controls.Add(showLabel);
        Controls.Add(showCombo);

        var divider = new Panel { Location = new Point(0, 82), Size = new Size(1000, 1), Tag = AppTheme.NoTheme, BackColor = AppTheme.Border };
        Controls.Add(divider);

        var entries = _data.Entries.Where(e => e.DeletedAt is null).ToList();
        var secretGroups = entries.Where(e => !string.IsNullOrEmpty(e.Secret)).GroupBy(e => e.Secret)
            .Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();

        IEnumerable<VaultEntry> filtered = _filter switch
        {
            ListFilter.Weak => entries.Where(e => PasswordStrength.Evaluate(e.Secret) <= StrengthLevel.Fair),
            ListFilter.Reused => entries.Where(e => secretGroups.Contains(e)),
            _ => entries
        };

        var list = _sortByStrength
            ? filtered.OrderBy(e => PasswordStrength.Evaluate(e.Secret)).ToList()
            : filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _recordListBox.Items.Clear();
        _recordListBox.Location = new Point(0, 92);
        _recordListBox.Size = new Size(1000, 560);
        _recordListBox.BorderStyle = BorderStyle.None;
        _recordListBox.DrawMode = DrawMode.OwnerDrawFixed;
        _recordListBox.ItemHeight = 56;
        _recordListBox.IntegralHeight = false;
        _recordListBox.Tag = AppTheme.NoTheme;
        _recordListBox.BackColor = AppTheme.Shell;
        _recordListBox.DrawItem -= RecordListBox_DrawItem;
        _recordListBox.DrawItem += RecordListBox_DrawItem;
        foreach (var entry in list)
            _recordListBox.Items.Add(entry);

        Controls.Add(_recordListBox);
    }

    private void RecordListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var entry = (VaultEntry)_recordListBox.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(AppTheme.Shell))
            g.FillRectangle(bg, e.Bounds);

        var avatarRect = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + (e.Bounds.Height - 36) / 2, 36, 36);
        using (var avatarBrush = new SolidBrush(AppTheme.Accent))
            g.FillEllipse(avatarBrush, avatarRect);
        var initial = entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?";
        TextRenderer.DrawText(g, initial, new Font("Segoe UI", 12, FontStyle.Bold), avatarRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var textLeft = avatarRect.Right + 14;
        var nameRect = new Rectangle(textLeft, e.Bounds.Top + 8, e.Bounds.Width - textLeft - 140, 22);
        var subRect = new Rectangle(textLeft, e.Bounds.Top + 30, e.Bounds.Width - textLeft - 140, 20);
        TextRenderer.DrawText(g, entry.Name, new Font(_recordListBox.Font, FontStyle.Bold), nameRect, AppTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, entry.Username, _recordListBox.Font, subRect, AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var level = PasswordStrength.Evaluate(entry.Secret);
        var badgeColor = AppTheme.StrengthColor(level);
        var badgeText = level.ToString();
        var badgeIconRect = new Rectangle(e.Bounds.Right - 30, e.Bounds.Top + (e.Bounds.Height - 24) / 2, 24, 24);
        using (var badgeBg = new SolidBrush(Color.FromArgb(40, badgeColor)))
            g.FillEllipse(badgeBg, badgeIconRect);
        Icons.Draw(g, level <= StrengthLevel.Fair ? IconKind.Trash : IconKind.Shield, Rectangle.Inflate(badgeIconRect, -5, -5), badgeColor, 1.5f);

        var badgeTextRect = new Rectangle(badgeIconRect.Left - 90, e.Bounds.Top, 84, e.Bounds.Height);
        TextRenderer.DrawText(g, badgeText, _recordListBox.Font, badgeTextRect, badgeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        using var linePen = new Pen(AppTheme.Border);
        g.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void BuildCountCard(Panel card, IconKind icon, int count, string title, string description, string linkText, ListFilter filter)
    {
        var iconBadge = new Panel { Size = new Size(36, 36), Location = new Point(20, 20), Tag = AppTheme.NoTheme };
        iconBadge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(count > 0 ? Color.FromArgb(40, AppTheme.Danger) : Color.FromArgb(40, AppTheme.StrengthColor(StrengthLevel.Strong)));
            e.Graphics.FillEllipse(bg, 0, 0, 35, 35);
            Icons.Draw(e.Graphics, icon, new Rectangle(8, 8, 20, 20), count > 0 ? AppTheme.Danger : AppTheme.StrengthColor(StrengthLevel.Strong), 1.6f);
        };
        var countLabel = new Label
        {
            Text = count.ToString(),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(66, 18)
        };
        var titleLabel = new Label
        {
            Text = title,
            ForeColor = AppTheme.SubText,
            AutoSize = true,
            Location = new Point(66, 42)
        };
        var descLabel = new Label
        {
            Text = description,
            ForeColor = AppTheme.SubText,
            Size = new Size(400, 40),
            Location = new Point(20, 68)
        };
        var link = MakeLinkButton(linkText, new Point(300, 120));
        link.Click += (_, _) => ShowList(filter);

        card.Controls.Add(iconBadge);
        card.Controls.Add(countLabel);
        card.Controls.Add(titleLabel);
        card.Controls.Add(descLabel);
        card.Controls.Add(link);
    }

    private void AddStatusRow(Panel card, bool ok, string title, string description)
    {
        var badgeColor = ok ? AppTheme.StrengthColor(StrengthLevel.Strong) : AppTheme.SubText;
        var iconBadge = new Panel { Size = new Size(36, 36), Location = new Point(20, 12), Tag = AppTheme.NoTheme };
        iconBadge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(40, badgeColor));
            e.Graphics.FillEllipse(bg, 0, 0, 35, 35);
            Icons.Draw(e.Graphics, IconKind.Shield, new Rectangle(8, 8, 20, 20), badgeColor, 1.6f);
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(66, 22)
        };
        card.Controls.Add(iconBadge);
        card.Controls.Add(titleLabel);
    }

    private static Panel MakeCard(Point location, Size size)
    {
        var card = new Panel
        {
            Location = location,
            Size = size,
            Tag = AppTheme.RoleCard,
            BackColor = AppTheme.CardBg
        };
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(AppTheme.Border, 1.2f);
            using var path = RoundedRectPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
            e.Graphics.DrawPath(pen, path);
        };
        return card;
    }

    private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Button MakeLinkButton(string text, Point location)
    {
        var btn = new Button
        {
            Text = text,
            Location = location,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = AppTheme.Accent,
            BackColor = AppTheme.CardBg,
            Tag = AppTheme.NoTheme,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}
