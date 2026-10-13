using System.Drawing.Drawing2D;
using SecretVault.Crypto;

namespace SecretVault.Gui;

// Password / passphrase generator with live preview. Options persist in AppSettings.
public class GeneratorForm : Form
{
    private const int Pad = 24;

    private readonly PasswordOptions _options;
    private readonly Label _preview = new();
    private readonly Panel _meter = new() { Height = 6, Tag = AppTheme.NoTheme };
    private readonly Label _meterLabel = new();
    private readonly Panel _passwordOptions = new() { Tag = AppTheme.NoTheme };
    private readonly Panel _passphraseOptions = new() { Tag = AppTheme.NoTheme };
    private readonly PillButton _modePassword = new("Password", primary: false, new Size(120, 34));
    private readonly PillButton _modePassphrase = new("Passphrase", primary: false, new Size(120, 34));
    private double _bits;

    public string? Result { get; private set; }

    public GeneratorForm()
    {
        var saved = AppSettings.Current.Generator;
        _options = new PasswordOptions
        {
            Passphrase = saved.Passphrase, Length = saved.Length, Uppercase = saved.Uppercase, Lowercase = saved.Lowercase,
            Digits = saved.Digits, Symbols = saved.Symbols, Words = saved.Words, Separator = saved.Separator,
            Capitalize = saved.Capitalize, IncludeNumber = saved.IncludeNumber
        };

        Text = "Password Generator";
        ClientSize = new Size(480, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.Canvas;
        Font = new Font("Segoe UI", 9.5f);
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var card = UiKit.NewCard();
        card.SetBounds(Pad - 4, Pad - 4, ClientSize.Width - (Pad - 4) * 2, ClientSize.Height - 68 - (Pad - 4) * 2 + 8);
        var inner = card.Width - Pad * 2;
        Controls.Add(card);

        // Mode switch
        _modePassword.Location = new Point(Pad, Pad - 4);
        _modePassphrase.Location = new Point(Pad + 128, Pad - 4);
        _modePassword.Click += (_, _) => { _options.Passphrase = false; Regenerate(); };
        _modePassphrase.Click += (_, _) => { _options.Passphrase = true; Regenerate(); };
        card.Controls.Add(_modePassword);
        card.Controls.Add(_modePassphrase);

        // Preview box
        var previewBox = new Panel { Location = new Point(Pad, 70), Size = new Size(inner, 74), Tag = AppTheme.NoTheme, BackColor = AppTheme.Canvas };
        previewBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(AppTheme.CardBg);
            using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, previewBox.Width - 1.5f, previewBox.Height - 1.5f), 10);
            using var fill = new SolidBrush(previewBox.BackColor);
            e.Graphics.FillPath(fill, path);
        };
        _preview.Font = new Font("Consolas", 13f, FontStyle.Bold);
        _preview.ForeColor = AppTheme.Text;
        _preview.UseMnemonic = false;
        _preview.AutoEllipsis = true;
        _preview.TextAlign = ContentAlignment.MiddleLeft;
        _preview.SetBounds(14, 8, inner - 14 - 80, 58);
        _preview.Tag = AppTheme.NoTheme;
        var regen = new IconButton(IconKind.Dice, 34) { Circular = false, Location = new Point(inner - 76, 20) };
        new ToolTip().SetToolTip(regen, "Generate another");
        regen.Click += (_, _) => Regenerate();
        var copy = new IconButton(IconKind.Copy, 34) { Circular = false, Location = new Point(inner - 40, 20) };
        new ToolTip().SetToolTip(copy, "Copy");
        copy.Click += (_, _) => { if (!string.IsNullOrEmpty(_preview.Text)) Clipboard.SetText(_preview.Text); };
        previewBox.Controls.AddRange(new Control[] { _preview, regen, copy });
        card.Controls.Add(previewBox);

        _meter.SetBounds(Pad, 156, inner - 170, 6);
        _meter.BackColor = AppTheme.CardBg;
        _meter.Paint += PaintMeter;
        _meterLabel.SetBounds(Pad + inner - 160, 148, 160, 20);
        _meterLabel.TextAlign = ContentAlignment.MiddleRight;
        _meterLabel.Font = new Font("Segoe UI Semibold", 8.5f);
        _meterLabel.Tag = AppTheme.NoTheme;
        card.Controls.Add(_meter);
        card.Controls.Add(_meterLabel);

        // Options panels share the same area; only one is visible.
        foreach (var panel in new[] { _passwordOptions, _passphraseOptions })
        {
            panel.SetBounds(Pad, 184, inner, card.Height - 184 - 12);
            panel.BackColor = AppTheme.CardBg;
            card.Controls.Add(panel);
        }

        var y = 0;
        y = AddSlider(_passwordOptions, "Length", 8, 64, _options.Length, v => _options.Length = v, y);
        y = AddToggle(_passwordOptions, "Uppercase (A–Z)", _options.Uppercase, v => _options.Uppercase = v, y);
        y = AddToggle(_passwordOptions, "Lowercase (a–z)", _options.Lowercase, v => _options.Lowercase = v, y);
        y = AddToggle(_passwordOptions, "Numbers (0–9)", _options.Digits, v => _options.Digits = v, y);
        AddToggle(_passwordOptions, "Symbols (!@#$…)", _options.Symbols, v => _options.Symbols = v, y);

        y = 0;
        y = AddSlider(_passphraseOptions, "Words", 3, 10, _options.Words, v => _options.Words = v, y);
        y = AddToggle(_passphraseOptions, "Capitalize words", _options.Capitalize, v => _options.Capitalize = v, y);
        y = AddToggle(_passphraseOptions, "Include a number", _options.IncludeNumber, v => _options.IncludeNumber = v, y);
        _passphraseOptions.Controls.Add(new Label { Text = "Separator", ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(0, y + 8), Tag = AppTheme.NoTheme });
        var sx = inner - 4 * 52;
        foreach (var (label, sep) in new[] { ("-", "-"), (".", "."), ("_", "_"), ("space", " ") })
        {
            var b = new PillButton(label, primary: false, new Size(46, 30)) { Location = new Point(sx, y + 2) };
            b.Click += (_, _) => { _options.Separator = sep; Regenerate(); };
            b.Tag = sep;
            _passphraseOptions.Controls.Add(b);
            sx += 52;
        }

        // Footer
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg };
        footer.Paint += (_, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        var use = new PillButton("Use password", primary: true, new Size(136, 38)) { Location = new Point(ClientSize.Width - 24 - 136, 15) };
        var cancel = new PillButton("Cancel", primary: false, new Size(96, 38)) { Location = new Point(use.Left - 10 - 96, 15) };
        use.Click += (_, _) =>
        {
            Result = _preview.Text;
            AppSettings.Current.Generator = _options;
            AppSettings.Current.Save();
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.Add(use);
        footer.Controls.Add(cancel);
        Controls.Add(footer);
        AcceptButton = use;
        CancelButton = cancel;

        Regenerate();
    }

    private int AddToggle(Panel host, string label, bool value, Action<bool> set, int y)
    {
        host.Controls.Add(new Label { Text = label, ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(0, y + 10), Tag = AppTheme.NoTheme });
        var toggle = new ToggleSwitch { Checked = value, Location = new Point(host.Width - 44, y + 8) };
        toggle.CheckedChanged += (_, _) => { set(toggle.Checked); Regenerate(); };
        host.Controls.Add(toggle);
        return y + 40;
    }

    private int AddSlider(Panel host, string label, int min, int max, int value, Action<int> set, int y)
    {
        var valueLabel = new Label { Text = value.ToString(), Font = new Font("Segoe UI Semibold", 10f), ForeColor = AppTheme.Accent, AutoSize = true, Tag = AppTheme.NoTheme };
        host.Controls.Add(new Label { Text = label, ForeColor = AppTheme.Text, AutoSize = true, Location = new Point(0, y + 10), Tag = AppTheme.NoTheme });
        var slider = new TrackBar
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickStyle = TickStyle.None,
            Location = new Point(80, y + 4), Width = host.Width - 80 - 44, Tag = AppTheme.NoTheme, BackColor = AppTheme.CardBg, AutoSize = false, Height = 30
        };
        valueLabel.Location = new Point(host.Width - 32, y + 9);
        slider.ValueChanged += (_, _) => { set(slider.Value); valueLabel.Text = slider.Value.ToString(); Regenerate(); };
        host.Controls.Add(slider);
        host.Controls.Add(valueLabel);
        return y + 44;
    }

    private void Regenerate()
    {
        _modePassword.Selected = !_options.Passphrase;
        _modePassphrase.Selected = _options.Passphrase;
        _modePassword.Invalidate();
        _modePassphrase.Invalidate();
        _passwordOptions.Visible = !_options.Passphrase;
        _passphraseOptions.Visible = _options.Passphrase;
        foreach (var b in _passphraseOptions.Controls.OfType<PillButton>())
        {
            b.Selected = (string?)b.Tag == _options.Separator;
            b.Invalidate();
        }

        _preview.Text = PasswordGenerator.Generate(_options);
        _bits = PasswordGenerator.EstimateEntropyBits(_options);
        var level = PasswordStrength.Evaluate(_preview.Text);
        _meterLabel.Text = $"{UiKit.StrengthText(level)} · ~{_bits:0} bits";
        _meterLabel.ForeColor = AppTheme.StrengthColor(level);
        _meter.Invalidate();
    }

    private void PaintMeter(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.CardBg);
        var track = new RectangleF(0, 0, _meter.Width - 1, _meter.Height - 1);
        using (var path = RoundedField.RoundRect(track, 2.5f))
        using (var b = new SolidBrush(AppTheme.Border))
            g.FillPath(b, path);
        var fraction = (float)Math.Clamp(_bits / 128.0, 0.05, 1.0);
        var fill = new RectangleF(0, 0, track.Width * fraction, track.Height);
        using (var path = RoundedField.RoundRect(fill, 2.5f))
        using (var b = new SolidBrush(AppTheme.StrengthColor(PasswordStrength.Evaluate(_preview.Text))))
            g.FillPath(b, path);
    }
}
