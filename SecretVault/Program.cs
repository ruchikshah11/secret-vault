using SecretVault.Storage;

namespace SecretVault.Gui;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppTheme.SetMode(AppSettings.Current.ThemeMode);

        // Older debug builds wrote an unencrypted copy of the vault next to it; make sure it is gone.
        try { new FileVaultStore().DeleteLegacyPlaintextDump(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Unlock -> main window -> (lock) -> unlock again, until the user closes the app.
        string? notice = null;
        while (true)
        {
            using var login = new LoginForm(notice);
            if (login.ShowDialog() != DialogResult.OK || login.UnlockedData is null || login.MasterPassword is null)
                return;

            using var main = new MainForm(login.UnlockedData, login.MasterPassword);
            Application.Run(main);
            if (!main.LockRequested) return;
            notice = "Vault locked. Enter your master password to continue.";
        }
    }
}
