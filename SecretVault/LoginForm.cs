using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

public class LoginForm : Form
{
    private readonly FileVaultStore _store = new();
    private readonly bool _isNewVault;

    private readonly Label _titleLabel = new();
    private readonly TextBox _passwordBox = new();
    private readonly TextBox _confirmBox = new();
    private readonly Label _confirmLabel = new();
    private readonly Label _errorLabel = new();
    private readonly Button _actionButton = new();
    private readonly IconButton _showPasswordButton = new(IconKind.Eye, 28) { Circular = false };

    public VaultData? UnlockedData { get; private set; }
    public string? MasterPassword { get; private set; }

    public LoginForm()
    {
        _isNewVault = !_store.Exists();

        Text = "SecretVault";
        ShowIcon = false;
        Size = new Size(460, _isNewVault ? 400 : 340);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.Shell;
        HandleCreated += (_, _) => DarkTitleBar.Apply(Handle, AppTheme.Mode == ThemeMode.Dark);

        var card = new Panel
        {
            Size = new Size(360, _isNewVault ? 320 : 260),
            Tag = AppTheme.RoleCard,
            Padding = new Padding(32)
        };
        card.Location = new Point((ClientSize.Width - card.Width) / 2, 24);
        card.Anchor = AnchorStyles.Top;

        var badge = new Panel { Size = new Size(48, 48), Location = new Point(0, 0) };
        badge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(AppTheme.Accent);
            e.Graphics.FillEllipse(brush, 0, 0, 47, 47);
            Icons.Draw(e.Graphics, IconKind.Shield, new Rectangle(13, 12, 22, 22), Color.White, 1.8f, AppTheme.Accent);
        };

        _titleLabel.Text = _isNewVault ? "Create Master Password" : "Unlock Vault";
        _titleLabel.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        _titleLabel.ForeColor = AppTheme.Text;
        _titleLabel.AutoSize = true;
        _titleLabel.Location = new Point(0, 60);

        var passwordLabel = new Label
        {
            Text = _isNewVault ? "New master password" : "Master password",
            ForeColor = AppTheme.SubText,
            AutoSize = true,
            Location = new Point(0, 100)
        };

        _passwordBox.Location = new Point(0, 122);
        _passwordBox.Size = new Size(258, 30);
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.BackColor = AppTheme.Field;
        _passwordBox.ForeColor = AppTheme.Text;
        _passwordBox.BorderStyle = BorderStyle.FixedSingle;
        _passwordBox.Font = new Font("Segoe UI", 10.5f);

        _showPasswordButton.Location = new Point(262, 121);
        _showPasswordButton.Click += (_, _) =>
        {
            _passwordBox.UseSystemPasswordChar = !_passwordBox.UseSystemPasswordChar;
            _showPasswordButton.Icon = _passwordBox.UseSystemPasswordChar ? IconKind.Eye : IconKind.EyeOff;
            _showPasswordButton.Invalidate();
        };

        _confirmLabel.Text = "Confirm password";
        _confirmLabel.ForeColor = AppTheme.SubText;
        _confirmLabel.AutoSize = true;
        _confirmLabel.Location = new Point(0, 164);
        _confirmLabel.Visible = _isNewVault;

        _confirmBox.Location = new Point(0, 186);
        _confirmBox.Size = new Size(296, 30);
        _confirmBox.UseSystemPasswordChar = true;
        _confirmBox.BackColor = AppTheme.Field;
        _confirmBox.ForeColor = AppTheme.Text;
        _confirmBox.BorderStyle = BorderStyle.FixedSingle;
        _confirmBox.Font = new Font("Segoe UI", 10.5f);
        _confirmBox.Visible = _isNewVault;

        _errorLabel.ForeColor = AppTheme.Danger;
        _errorLabel.AutoSize = true;
        _errorLabel.Location = new Point(0, _isNewVault ? 224 : 160);

        _actionButton.Text = _isNewVault ? "Create Vault" : "Unlock";
        _actionButton.Size = new Size(296, 40);
        _actionButton.Location = new Point(0, _isNewVault ? 252 : 190);
        _actionButton.BackColor = AppTheme.Gold;
        _actionButton.ForeColor = Color.Black;
        _actionButton.FlatStyle = FlatStyle.Flat;
        _actionButton.FlatAppearance.BorderSize = 0;
        _actionButton.FlatAppearance.MouseOverBackColor = AppTheme.GoldHover;
        _actionButton.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _actionButton.Cursor = Cursors.Hand;
        _actionButton.Tag = AppTheme.NoTheme;
        _actionButton.Click += async (_, _) => await OnActionAsync();

        AcceptButton = _actionButton;

        card.Controls.Add(badge);
        card.Controls.Add(_titleLabel);
        card.Controls.Add(passwordLabel);
        card.Controls.Add(_passwordBox);
        card.Controls.Add(_showPasswordButton);
        card.Controls.Add(_confirmLabel);
        card.Controls.Add(_confirmBox);
        card.Controls.Add(_errorLabel);
        card.Controls.Add(_actionButton);

        Controls.Add(card);
    }

    private async Task OnActionAsync()
    {
        _errorLabel.Text = string.Empty;
        var password = _passwordBox.Text;

        if (string.IsNullOrEmpty(password))
        {
            _errorLabel.Text = "Password cannot be empty.";
            return;
        }

        if (_isNewVault)
        {
            if (password != _confirmBox.Text)
            {
                _errorLabel.Text = "Passwords do not match.";
                return;
            }

            var data = new VaultData();
            await _store.SaveAsync(data, password);
            UnlockedData = data;
            MasterPassword = password;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        try
        {
            _actionButton.Enabled = false;
            UnlockedData = await _store.LoadAsync(password);
            MasterPassword = password;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (VaultAuthenticationException)
        {
            _errorLabel.Text = "Incorrect master password.";
            _actionButton.Enabled = true;
        }
    }
}
