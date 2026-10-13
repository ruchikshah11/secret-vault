using System.Drawing.Drawing2D;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Gui;

// Checks every password against Have I Been Pwned (k-anonymity range API) and lists the exposed ones.
public class BreachWatchPanel : Panel
{
    private const int Pad = 28;
    private const int MaxWidth = 1040;

    private readonly VaultData _data;
    private readonly Action<VaultEntry> _editEntry;
    private readonly Font _chipFont = new("Segoe UI Semibold", 8.25f);
    private int _builtWidth = -1;

    private CancellationTokenSource? _scan;
    private string _status = "Not scanned yet.";
    private int _progress;
    private int _progressTotal;
    private DateTime? _lastScan;
    private string? _error;

    // Entry -> number of times its password appears in breaches. Null until the first scan completes.
    public Dictionary<VaultEntry, int>? Results { get; private set; }

    public BreachWatchPanel(VaultData data, Action<VaultEntry> editEntry)
    {
        _data = data;
        _editEntry = editEntry;
        Tag = AppTheme.NoTheme;
        AutoScroll = true;
        Resize += (_, _) => { if (Visible && ClientSize.Width != _builtWidth) RefreshView(); };
        VisibleChanged += (_, _) => { if (!Visible) _scan?.Cancel(); };
    }

    public void RefreshView()
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
        AutoScrollPosition = Point.Empty;
        BackColor = AppTheme.Canvas;
        _builtWidth = ClientSize.Width;

        var width = Math.Clamp(ClientSize.Width - Pad * 2, 560, MaxWidth);
        var y = Pad - 4;
        Controls.Add(new Label { Text = "BreachWatch", Font = new Font("Segoe UI", 17f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad - 3, y), Tag = AppTheme.NoTheme });
        Controls.Add(new Label { Text = "Find passwords that have appeared in public data breaches.", ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(Pad - 1, y + 36), Tag = AppTheme.NoTheme });
        y += 76;

        y = BuildScanCard(width, y) + 20;
        BuildResults(width, y);
        ResumeLayout();
    }

    private int BuildScanCard(int width, int y)
    {
        var card = UiKit.NewCard();
        card.SetBounds(Pad, y, width, 170);
        Controls.Add(card);

        var exposed = Results?.Count(r => r.Value > 0 && r.Key.DeletedAt is null && _data.Entries.Contains(r.Key)) ?? 0;
        var color = Results is null ? AppTheme.Accent : exposed > 0 ? AppTheme.Danger : AppTheme.StrengthColor(StrengthLevel.Strong);
        var badge = new Panel { Size = new Size(56, 56), Location = new Point(24, 24), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        badge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            var tint = UiKit.Tint(color, AppTheme.CardBg);
            using var b = new SolidBrush(tint);
            g.FillEllipse(b, 0, 0, 55, 55);
            Icons.Draw(g, IconKind.Shield, new Rectangle(16, 15, 24, 26), color, 2f, tint);
        };
        card.Controls.Add(badge);

        var headline = _scan is not null ? "Scanning your passwords…"
            : Results is null ? "Check your passwords against known breaches"
            : exposed == 0 ? "No exposed passwords found"
            : $"{exposed} password{(exposed == 1 ? " has" : "s have")} been exposed";
        card.Controls.Add(new Label { Text = headline, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(96, 26), Tag = AppTheme.NoTheme });
        card.Controls.Add(new Label
        {
            Text = "Your passwords never leave this PC. Only the first 5 characters of each password's SHA-1 hash are sent to " +
                   "haveibeenpwned.com, which returns hundreds of possible matches that are compared locally.",
            ForeColor = AppTheme.SubText, Location = new Point(96, 58), Size = new Size(width - 96 - 24, 40), Tag = AppTheme.NoTheme
        });

        var statusText = _error ?? (_scan is not null ? $"Checked {_progress} of {_progressTotal}…" : _lastScan is { } t ? $"Last scan: {t:h:mm tt}" : _status);
        card.Controls.Add(new Label { Text = statusText, ForeColor = _error is null ? AppTheme.SubText : AppTheme.Danger, Font = new Font("Segoe UI", 9f), AutoSize = true, Location = new Point(96, 124), Tag = AppTheme.NoTheme });

        var button = new PillButton(_scan is not null ? "Cancel" : Results is null ? "Scan now" : "Scan again", primary: _scan is null, new Size(130, 38))
        {
            LeadingIcon = _scan is not null ? IconKind.Close : IconKind.Search,
            Location = new Point(width - 24 - 130, 114)
        };
        button.Click += async (_, _) =>
        {
            if (_scan is not null) { _scan.Cancel(); return; }
            await ScanAsync();
        };
        card.Controls.Add(button);

        if (_scan is not null && _progressTotal > 0)
        {
            var bar = new Panel { Location = new Point(96, 150), Size = new Size(width - 96 - 24 - 140, 5), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
            bar.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(AppTheme.CardBg);
                using (var track = RoundedField.RoundRect(new RectangleF(0, 0, bar.Width - 1, 4), 2))
                using (var b = new SolidBrush(AppTheme.Border)) g.FillPath(b, track);
                var w = Math.Max(4, (bar.Width - 1) * _progress / (float)_progressTotal);
                using var fill = RoundedField.RoundRect(new RectangleF(0, 0, w, 4), 2);
                AppTheme.FillPrimary(g, fill, new RectangleF(0, 0, bar.Width, 4), false, true, AppTheme.CardBg);
            };
            card.Controls.Add(bar);
        }

        return card.Bottom;
    }

    private void BuildResults(int width, int y)
    {
        if (Results is null) return;

        // Edited records are replaced by new objects, so drop results for records no longer in the vault.
        var exposed = Results.Where(r => r.Value > 0 && r.Key.DeletedAt is null && _data.Entries.Contains(r.Key))
            .OrderByDescending(r => r.Value).ToList();
        if (exposed.Count == 0)
        {
            Controls.Add(new Label { Text = "🎉  None of your passwords appear in known breaches.", ForeColor = AppTheme.SubText, Font = new Font("Segoe UI", 10.5f), AutoSize = true, Location = new Point(Pad, y), Tag = AppTheme.NoTheme });
            return;
        }

        Controls.Add(new Label { Text = "EXPOSED PASSWORDS — CHANGE THESE FIRST", Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = AppTheme.SubText, AutoSize = true, Location = new Point(Pad, y), Tag = AppTheme.NoTheme });
        y += 26;

        foreach (var (entry, count) in exposed)
        {
            var tile = new Panel { Location = new Point(Pad, y), Size = new Size(width, 66), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
            tile.Paint += (_, e) =>
            {
                var g = e.Graphics;
                UiKit.PaintCard(g, tile, 12);
                UiKit.DrawAvatar(g, new Rectangle(14, 13, 40, 40), entry.Name);
                var chipText = $"Seen {count:N0} times";
                var chipW = TextRenderer.MeasureText(g, chipText, _chipFont, Size.Empty, TextFormatFlags.NoPadding).Width + 20;
                UiKit.DrawChip(g, new Point(tile.Width - 130 - 16 - chipW, 22), chipText, _chipFont, AppTheme.Danger, UiKit.Tint(AppTheme.Danger, AppTheme.CardBg));
            };
            tile.Controls.Add(new Label { Text = entry.Name, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 10f), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(66, 13), BackColor = AppTheme.CardBg, Tag = AppTheme.NoTheme });
            tile.Controls.Add(new Label { Text = string.IsNullOrEmpty(entry.Username) ? entry.Folder : $"{entry.Username}  ·  {entry.Folder}", UseMnemonic = false, ForeColor = AppTheme.SubText, Font = new Font("Segoe UI", 9f), AutoSize = true, Location = new Point(66, 35), BackColor = AppTheme.CardBg, Tag = AppTheme.NoTheme });
            var change = new PillButton("Change", primary: false, new Size(110, 32)) { LeadingIcon = IconKind.Edit, Location = new Point(width - 16 - 110, 17) };
            change.Click += (_, _) => _editEntry(entry);
            tile.Controls.Add(change);
            Controls.Add(tile);
            y += 74;
        }
        Controls.Add(new Panel { Location = new Point(Pad, y), Size = new Size(1, Pad), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas });
    }

    private async Task ScanAsync()
    {
        _scan = new CancellationTokenSource();
        _error = null;
        _progress = 0;
        _progressTotal = 0;
        RefreshView();

        try
        {
            using var checker = new BreachChecker();
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                (_progress, _progressTotal) = p;
                if (!IsDisposed) RefreshView();
            });
            Results = await checker.ScanAsync(VaultOperations.Live(_data), progress, _scan.Token);
            _lastScan = DateTime.Now;
        }
        catch (OperationCanceledException) when (_scan.IsCancellationRequested)
        {
            _status = "Scan cancelled.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _error = "Couldn't reach haveibeenpwned.com. Check your internet connection and try again.";
        }
        finally
        {
            _scan.Dispose();
            _scan = null;
        }

        if (!IsDisposed) RefreshView();
    }
}
