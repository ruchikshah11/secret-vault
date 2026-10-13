using System.Drawing.Drawing2D;

namespace SecretVault.Gui;

// Shows a newly created recovery key once. The user must confirm they've saved it before closing.
public class RecoveryKeyForm : Form
{
    private const int Pad = 26;

    public RecoveryKeyForm(string recoveryKey)
    {
        Text = "Your recovery key";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var card = UiKit.NewCard();
        card.SetBounds(20, 20, 500, 100);
        var inner = card.Width - Pad * 2;
        var y = Pad;

        card.Controls.Add(new Label { Text = "Save your recovery key", Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad - 2, y), Tag = AppTheme.NoTheme });
        y += 38;
        card.Controls.Add(new Label
        {
            Text = "If you forget your master password, this key lets you set a new one — on this PC or any PC with your vault file. " +
                   "It is shown only now. Store it somewhere safe, away from this computer (printed, or in another password manager).",
            ForeColor = AppTheme.SubText, Location = new Point(Pad, y), Size = new Size(inner, 80), Tag = AppTheme.NoTheme
        });
        y += 88;

        // The key, large and monospaced.
        var keyBox = new Panel { Location = new Point(Pad, y), Size = new Size(inner, 64), Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        var keyFont = new Font("Consolas", 15f, FontStyle.Bold);
        keyBox.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.CardBg);
            var back = UiKit.Tint(AppTheme.Accent, AppTheme.CardBg);
            using (var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, keyBox.Width - 1.5f, keyBox.Height - 1.5f), 12))
            using (var brush = new SolidBrush(back))
            using (var pen = new Pen(UiKit.Blend(AppTheme.Accent, back, 0.4f)) { DashStyle = DashStyle.Dash })
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, recoveryKey, keyFont, keyBox.ClientRectangle, AppTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        card.Controls.Add(keyBox);
        y = keyBox.Bottom + 14;

        var copy = new PillButton("Copy", primary: false, new Size(110, 36)) { LeadingIcon = IconKind.Copy, Location = new Point(Pad, y) };
        copy.Click += (_, _) => { Clipboard.SetText(recoveryKey); copy.Text = "Copied"; };
        var save = new PillButton("Save as file…", primary: false, new Size(150, 36)) { LeadingIcon = IconKind.Download, Location = new Point(Pad + 120, y) };
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Title = "Save recovery key", Filter = "Text file (*.txt)|*.txt", FileName = "SecretVault recovery key.txt" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dialog.FileName,
                "SecretVault recovery key\r\n\r\n" + recoveryKey + "\r\n\r\n" +
                $"Created {DateTime.Now:MMMM d, yyyy}. Use it on the login screen (\"Forgot password?\") to set a new master password.\r\n" +
                "Keep this file somewhere safe and NOT on the same computer as your vault.\r\n");
            save.Text = "Saved";
        };
        card.Controls.Add(copy);
        card.Controls.Add(save);
        y += 54;

        var confirm = new ToggleSwitch { Location = new Point(Pad, y) };
        card.Controls.Add(confirm);
        card.Controls.Add(new Label { Text = "I have saved my recovery key somewhere safe", ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(Pad + 54, y + 3), Tag = AppTheme.NoTheme });
        y += 46;

        var done = new PillButton("Done", primary: true, new Size(120, 40)) { Location = new Point(card.Width - Pad - 120, y), Enabled = false };
        confirm.CheckedChanged += (_, _) => done.Enabled = confirm.Checked;
        done.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        card.Controls.Add(done);
        card.Height = done.Bottom + Pad;

        // Closing with the X before confirming asks once, so the key isn't lost by accident.
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK || confirm.Checked) return;
            if (!AppDialog.Confirm(this, "Close without saving the key?", "You haven't confirmed that you saved the recovery key. It won't be shown again.",
                    "Close anyway", "Go back", danger: true))
                e.Cancel = true;
        };

        Controls.Add(card);
        ClientSize = new Size(card.Width + 40, card.Height + 40);
    }
}
