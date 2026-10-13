using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// App-styled replacement for MessageBox: icon badge, bold title, message, rounded buttons.
// Destructive confirmations get a red button with a specific label ("Delete forever", not "Yes").
public sealed class AppDialog : Form
{
    public enum Kind { Info, Success, Question, Warning, Danger }

    private const int Pad = 24;
    private const int Width_ = 440;

    private AppDialog(string title, string message, Kind kind, string primaryText, string? secondaryText, bool dangerPrimary)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.CardBg;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var (color, icon) = kind switch
        {
            Kind.Success => (AppTheme.StrengthColor(Crypto.StrengthLevel.Strong), IconKind.Shield),
            Kind.Question => (AppTheme.Accent, IconKind.Key),
            Kind.Warning => (Color.FromArgb(217, 119, 6), IconKind.Lock),
            Kind.Danger => (AppTheme.Danger, IconKind.Trash),
            _ => (AppTheme.Accent, IconKind.Shield)
        };

        var badge = new Panel { Size = new Size(48, 48), Location = new Point(Pad, Pad), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        badge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            var tint = UiKit.Tint(color, AppTheme.CardBg);
            using (var b = new SolidBrush(tint)) g.FillEllipse(b, 0, 0, 47, 47);
            Icons.Draw(g, icon, new Rectangle(14, 14, 20, 20), color, 1.9f, tint);
        };
        Controls.Add(badge);

        var textLeft = Pad + 48 + 16;
        var textWidth = Width_ - textLeft - Pad;
        var titleLabel = new Label
        {
            Text = title, UseMnemonic = false, Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), ForeColor = AppTheme.Text,
            Location = new Point(textLeft - 2, Pad + 2), Size = new Size(textWidth, 26), AutoEllipsis = true, Tag = AppTheme.NoTheme
        };
        Controls.Add(titleLabel);

        var messageHeight = TextRenderer.MeasureText(message, Font, new Size(textWidth, 0), TextFormatFlags.WordBreak).Height;
        var messageLabel = new Label
        {
            Text = message, UseMnemonic = false, ForeColor = AppTheme.SubText,
            Location = new Point(textLeft - 1, Pad + 34), Size = new Size(textWidth, messageHeight), Tag = AppTheme.NoTheme
        };
        Controls.Add(messageLabel);

        var footerTop = Math.Max(badge.Bottom, messageLabel.Bottom) + 22;
        var footer = new Panel { Location = new Point(0, footerTop), Size = new Size(Width_, 66), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        footer.Paint += (_, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };

        Control? safeChoice = null;
        var primaryWidth = Math.Max(110, TextRenderer.MeasureText(primaryText, new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width + 36);
        var primary = new PillButton(primaryText, primary: true, new Size(primaryWidth, 38)) { Danger = dangerPrimary, Location = new Point(Width_ - Pad - primaryWidth, 14) };
        primary.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        footer.Controls.Add(primary);
        AcceptButton = primary;

        if (secondaryText is not null)
        {
            var secondaryWidth = Math.Max(96, TextRenderer.MeasureText(secondaryText, Font).Width + 32);
            var secondary = new PillButton(secondaryText, primary: false, new Size(secondaryWidth, 38)) { Location = new Point(primary.Left - 10 - secondaryWidth, 14) };
            secondary.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(secondary);
            CancelButton = secondary;
            if (dangerPrimary) safeChoice = secondary;
        }
        else
        {
            CancelButton = primary;
        }

        Controls.Add(footer);
        ClientSize = new Size(Width_, footer.Bottom);
        // Destructive actions default to the safe choice (set after the buttons are on the form, or WinForms throws).
        if (safeChoice is not null) ActiveControl = safeChoice;
    }

    // Yes/no style question; returns true for the primary (confirm) button.
    public static bool Confirm(IWin32Window? owner, string title, string message, string confirmText, string cancelText = "Cancel",
        Kind kind = Kind.Question, bool danger = false)
    {
        using var dialog = new AppDialog(title, message, danger ? Kind.Danger : kind, confirmText, cancelText, danger);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    // Information / warning with a single button.
    public static void Show(IWin32Window? owner, string title, string message, Kind kind = Kind.Info, string buttonText = "OK")
    {
        using var dialog = new AppDialog(title, message, kind, buttonText, null, false);
        dialog.ShowDialog(owner);
    }
}
