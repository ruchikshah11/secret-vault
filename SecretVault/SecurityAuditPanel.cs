using System.Drawing.Drawing2D;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Gui;

public class SecurityAuditPanel : Panel
{
    private enum ListFilter { All, Weak, Reused }

    private const int Pad = 28;
    private const int Gap = 16;
    private const int MaxWidth = 1040;

    private readonly VaultData _data;
    private readonly Func<string> _masterPassword;
    private readonly Action _viewAllRecords;

    private bool _showingList;
    private ListFilter _filter = ListFilter.All;
    private bool _sortByStrength = true;
    private int _builtWidth = -1;

    private readonly NavListView _recordListBox = new() { ShowScrollbar = true };
    private HashSet<VaultEntry> _reused = new();
    private readonly Font _rowTitleFont = new("Segoe UI Semibold", 10f);
    private readonly Font _rowSubFont = new("Segoe UI", 9f);
    private readonly Font _chipFont = new("Segoe UI Semibold", 8.25f);

    public SecurityAuditPanel(VaultData data, Func<string> masterPassword, Action viewAllRecords)
    {
        _data = data;
        _masterPassword = masterPassword;
        _viewAllRecords = viewAllRecords;
        Tag = AppTheme.NoTheme;
        AutoScroll = true;

        _recordListBox.ItemHeight = 70;
        _recordListBox.Tag = AppTheme.NoTheme;
        _recordListBox.DrawItem += RecordListBox_DrawItem;

        Resize += (_, _) =>
        {
            if (Visible && ClientSize.Width != _builtWidth) RefreshData();
        };
    }

    public void RefreshData()
    {
        SuspendLayout();
        _recordListBox.Parent?.Controls.Remove(_recordListBox); // reused; keep it out of the disposal below
        foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
        AutoScrollPosition = Point.Empty;
        BackColor = AppTheme.Canvas;
        _builtWidth = ClientSize.Width;

        if (_showingList)
            BuildDrillDownList();
        else
            BuildDashboard();

        ResumeLayout();
    }

    private int ContentWidth => Math.Clamp(ClientSize.Width - Pad * 2, 560, MaxWidth);

    // ---------- Dashboard ----------

    private void BuildDashboard()
    {
        // Score formula lives in SecretVault.Core (SecurityReport) so the Android app shows the same numbers.
        var report = SecurityReport.Create(_data, _masterPassword());
        var entries = report.Entries;
        var weakCount = report.Weak.Count;
        var reusedCount = report.Reused.Count;
        var has2Fa = report.Has2Fa;
        var masterStrong = report.MasterStrong;
        var score = report.Score;
        var scoreLabel = report.ScoreLabel;
        var scoreColor = AppTheme.StrengthColor(score switch { >= 80 => StrengthLevel.Strong, >= 60 => StrengthLevel.Fair, _ => StrengthLevel.Weak });

        var width = ContentWidth;
        var half = (width - Gap) / 2;
        var y = Pad - 4;

        y = AddPageHeader("Security Audit", "See how well your vault is protected and fix weak spots.", y);

        // Score card
        var scoreCard = Card(new Rectangle(Pad, y, width, 200));
        var gauge = new Panel { Size = new Size(150, 150), Location = new Point(28, 25), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        var scoreFont = new Font("Segoe UI", 24f, FontStyle.Bold);
        gauge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            var rect = new Rectangle(9, 9, 131, 131);
            using var trackPen = new Pen(AppTheme.Border, 12);
            g.DrawArc(trackPen, rect, 0, 360);
            using var scorePen = new Pen(scoreColor, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (score > 0) g.DrawArc(scorePen, rect, -90, 360f * score / 100f);
            TextRenderer.DrawText(g, $"{score}%", scoreFont, new Rectangle(0, 46, 150, 44), AppTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "score", _rowSubFont, new Rectangle(0, 88, 150, 20), AppTheme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        };
        scoreCard.Controls.Add(gauge);

        var textLeft = 206;
        scoreCard.Controls.Add(new Label { Text = "Overall security score", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(textLeft, 34), Tag = AppTheme.NoTheme });
        var verdict = new Panel { Location = new Point(textLeft, 66), Size = new Size(200, 24), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        verdict.Paint += (_, e) =>
        {
            e.Graphics.Clear(AppTheme.CardBg);
            UiKit.DrawChip(e.Graphics, Point.Empty, scoreLabel, _chipFont, scoreColor, UiKit.Tint(scoreColor, AppTheme.CardBg), IconKind.Shield);
        };
        scoreCard.Controls.Add(verdict);
        scoreCard.Controls.Add(new Label
        {
            Text = "Your score combines password strength, password reuse, your master password and two-factor usage. Fix the items below to raise it.",
            ForeColor = AppTheme.SubText,
            Location = new Point(textLeft, 100),
            Size = new Size(width - textLeft - 28, 44),
            Tag = AppTheme.NoTheme
        });
        var viewAll = new PillButton("View all records", primary: false, new Size(160, 36)) { LeadingIcon = IconKind.ChevronRight, Location = new Point(textLeft, 146) };
        viewAll.Click += (_, _) => ShowList(ListFilter.All);
        scoreCard.Controls.Add(viewAll);
        y = scoreCard.Bottom + Gap;

        // Status cards
        StatusCard(new Rectangle(Pad, y, half, 76), masterStrong, "Master password",
            masterStrong ? "Your master password is strong." : "Use a longer, more random master password.");
        StatusCard(new Rectangle(Pad + half + Gap, y, width - half - Gap, 76), has2Fa, "Two-factor codes",
            has2Fa ? "At least one record has a TOTP code." : "No records have a TOTP code yet.");
        y += 76 + Gap;

        // Issue cards
        IssueCard(new Rectangle(Pad, y, half, 176), IconKind.Key, weakCount, "Weak passwords",
            "\"Weak\" and \"Fair\" passwords are easy to guess. Change them on the affected accounts.",
            "Review weak", ListFilter.Weak);
        IssueCard(new Rectangle(Pad + half + Gap, y, width - half - Gap, 176), IconKind.Duplicate, reusedCount, "Reused passwords",
            "If one account with a reused password is breached, the others are at risk too.",
            "Review reused", ListFilter.Reused);
        y += 176;

        Controls.Add(new Panel { Location = new Point(Pad, y), Size = new Size(1, Pad), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas });
    }

    private int AddPageHeader(string title, string subtitle, int y, int x = Pad)
    {
        Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 17f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(x - 3, y), Tag = AppTheme.NoTheme });
        Controls.Add(new Label { Text = subtitle, ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(x - 1, y + 36), Tag = AppTheme.NoTheme });
        return y + 74;
    }

    private Panel Card(Rectangle bounds)
    {
        var card = UiKit.NewCard();
        card.Bounds = bounds;
        Controls.Add(card);
        return card;
    }

    private Panel IconBadge(IconKind icon, Color color, Point location, int size = 40)
    {
        var badge = new Panel { Size = new Size(size, size), Location = location, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        badge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            var tint = UiKit.Tint(color, AppTheme.CardBg);
            using var bg = new SolidBrush(tint);
            using var path = RoundedField.RoundRect(new RectangleF(0, 0, size - 1, size - 1), size / 4f);
            g.FillPath(bg, path);
            var inset = size / 4;
            Icons.Draw(g, icon, new Rectangle(inset, inset, size - inset * 2, size - inset * 2), color, 1.7f, tint);
        };
        return badge;
    }

    private void StatusCard(Rectangle bounds, bool ok, string title, string description)
    {
        var color = ok ? AppTheme.StrengthColor(StrengthLevel.Strong) : AppTheme.StrengthColor(StrengthLevel.Weak);
        var card = Card(bounds);
        card.Controls.Add(IconBadge(ok ? IconKind.Shield : IconKind.Key, color, new Point(20, 18)));
        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(74, 15), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label { Text = description, ForeColor = AppTheme.SubText, AutoEllipsis = true, Location = new Point(74, 40), Size = new Size(bounds.Width - 74 - 100, 20), Tag = AppTheme.NoTheme });

        var status = new Panel { Size = new Size(90, 24), Location = new Point(bounds.Width - 20 - 90, 26), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        status.Paint += (_, e) =>
        {
            e.Graphics.Clear(AppTheme.CardBg);
            var text = ok ? "Good" : "Improve";
            var w = TextRenderer.MeasureText(text, _chipFont, Size.Empty, TextFormatFlags.NoPadding).Width + 20;
            UiKit.DrawChip(e.Graphics, new Point(90 - w, 0), text, _chipFont, color, UiKit.Tint(color, AppTheme.CardBg));
        };
        card.Controls.Add(status);
    }

    private void IssueCard(Rectangle bounds, IconKind icon, int count, string title, string description, string linkText, ListFilter filter)
    {
        var color = count > 0 ? AppTheme.Danger : AppTheme.StrengthColor(StrengthLevel.Strong);
        var card = Card(bounds);
        card.Controls.Add(IconBadge(icon, color, new Point(22, 22), 44));
        card.Controls.Add(new Label { Text = count.ToString(), Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = count > 0 ? color : AppTheme.Text, AutoSize = true, Location = new Point(78, 14), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 10f), ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(81, 48), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label { Text = description, ForeColor = AppTheme.SubText, Location = new Point(22, 82), Size = new Size(bounds.Width - 44, 40), Tag = AppTheme.NoTheme });

        var link = new PillButton(count > 0 ? linkText : "All good", primary: false, new Size(150, 34))
        {
            LeadingIcon = count > 0 ? IconKind.ChevronRight : IconKind.Shield,
            AccentColor = count > 0 ? AppTheme.Accent : color,
            Location = new Point(22, bounds.Height - 34 - 18),
            Enabled = count > 0
        };
        link.Click += (_, _) => ShowList(filter);
        card.Controls.Add(link);
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
        var width = ContentWidth;
        var y = Pad - 4;

        var backBtn = new IconButton(IconKind.Back, 36) { Location = new Point(Pad - 6, y + 2), IconColor = AppTheme.SubText };
        new ToolTip().SetToolTip(backBtn, "Back to overview");
        backBtn.Click += (_, _) => { _showingList = false; RefreshData(); };
        Controls.Add(backBtn);
        var title = _filter switch { ListFilter.Weak => "Weak passwords", ListFilter.Reused => "Reused passwords", _ => "All records" };
        y = AddPageHeader(title, "Records are ordered so the riskiest ones come first.", y, Pad + 40);

        // Filter pills + sort toggle
        var x = Pad;
        foreach (var (text, filter) in new[] { ("All", ListFilter.All), ("Weak", ListFilter.Weak), ("Reused", ListFilter.Reused) })
        {
            var pill = new PillButton(text, primary: false, new Size(84, 34)) { Selected = _filter == filter, Location = new Point(x, y) };
            pill.Click += (_, _) => { _filter = filter; RefreshData(); };
            Controls.Add(pill);
            x += 92;
        }
        var sortBtn = new PillButton(_sortByStrength ? "Sort: Strength" : "Sort: Name", primary: false, new Size(150, 34))
        {
            Dropdown = true,
            Location = new Point(Pad + width - 150, y)
        };
        sortBtn.Click += (_, _) => { _sortByStrength = !_sortByStrength; RefreshData(); };
        Controls.Add(sortBtn);
        y += 34 + Gap;

        var report = SecurityReport.Create(_data, _masterPassword());
        var reused = report.Reused;

        IEnumerable<VaultEntry> filtered = _filter switch
        {
            ListFilter.Weak => report.Weak,
            ListFilter.Reused => report.Entries.Where(reused.Contains),
            _ => report.Entries
        };

        var list = _sortByStrength
            ? filtered.OrderBy(e => PasswordStrength.Evaluate(e.Secret)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (list.Count == 0)
        {
            Controls.Add(new Label
            {
                Text = _filter == ListFilter.All ? "No records yet." : "Nothing to fix here. 🎉",
                ForeColor = AppTheme.SubText,
                Font = new Font("Segoe UI", 10.5f),
                AutoSize = true,
                Location = new Point(Pad, y + 12),
                Tag = AppTheme.NoTheme
            });
            return;
        }

        _recordListBox.Items.Clear();
        _reused = reused;
        _recordListBox.BackColor = AppTheme.Canvas;
        _recordListBox.Location = new Point(Pad, y);
        _recordListBox.Size = new Size(width, Math.Max(200, ClientSize.Height - y - Pad));
        _recordListBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
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

        using (var bg = new SolidBrush(AppTheme.Canvas))
            g.FillRectangle(bg, e.Bounds);
        var tile = new RectangleF(e.Bounds.Left, e.Bounds.Top + 4, e.Bounds.Width - 1, e.Bounds.Height - 8);
        using (var path = RoundedField.RoundRect(tile, 12))
        {
            using var fill = new SolidBrush(AppTheme.CardBg);
            using var pen = new Pen(AppTheme.Border);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        var avatarRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + (e.Bounds.Height - 40) / 2, 40, 40);
        UiKit.DrawAvatar(g, avatarRect, entry.Name);

        // Right side chips: strength (always) and "Reused" when applicable.
        var level = PasswordStrength.Evaluate(entry.Secret);
        var color = AppTheme.StrengthColor(level);
        var chipY = e.Bounds.Top + (e.Bounds.Height - 22) / 2;
        var strengthText = string.IsNullOrEmpty(entry.Secret) ? "No password" : UiKit.StrengthText(level);
        var right = e.Bounds.Right - 16;
        right -= TextRenderer.MeasureText(g, strengthText, _chipFont, Size.Empty, TextFormatFlags.NoPadding).Width + 20;
        UiKit.DrawChip(g, new Point(right, chipY), strengthText, _chipFont, color, UiKit.Tint(color, AppTheme.CardBg));
        if (_reused.Contains(entry))
        {
            right -= 8 + TextRenderer.MeasureText(g, "Reused", _chipFont, Size.Empty, TextFormatFlags.NoPadding).Width + 20 + 16;
            UiKit.DrawChip(g, new Point(right, chipY), "Reused", _chipFont, AppTheme.Danger, UiKit.Tint(AppTheme.Danger, AppTheme.CardBg), IconKind.Duplicate);
        }

        var textLeft = avatarRect.Right + 14;
        TextRenderer.DrawText(g, entry.Name, _rowTitleFont, new Rectangle(textLeft, e.Bounds.Top + 14, right - textLeft - 10, 22), AppTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, string.IsNullOrEmpty(entry.Username) ? entry.Folder : $"{entry.Username}  ·  {entry.Folder}", _rowSubFont,
            new Rectangle(textLeft, e.Bounds.Top + 36, right - textLeft - 10, 20), AppTheme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
