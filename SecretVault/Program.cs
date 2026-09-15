namespace SecretVault.Gui;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppTheme.SetMode(ThemeSettings.Load());

        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK || login.UnlockedData is null || login.MasterPassword is null)
            return;

        Application.Run(new MainForm(login.UnlockedData, login.MasterPassword));
    }
}
