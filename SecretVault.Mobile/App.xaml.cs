using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using Application = Microsoft.Maui.Controls.Application;
using TabbedPage = Microsoft.Maui.Controls.TabbedPage;

namespace SecretVault.Mobile;

public partial class App : Application
{
    private Window? _window;
    private DateTimeOffset? _leftAt;
    private static TabbedPage? _tabs;

    public App()
    {
        InitializeComponent();
        AppPrefs.ApplyTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _window = new Window(StartPage()) { Title = "SecretVault" };
        _window.Stopped += (_, _) => _leftAt = DateTimeOffset.UtcNow;
        _window.Resumed += async (_, _) =>
        {
            var minutes = AppPrefs.AutoLockMinutes;
            if (VaultSession.Data is not null && minutes > 0 && _leftAt is { } left && DateTimeOffset.UtcNow - left >= TimeSpan.FromMinutes(minutes))
                LockNow();
            else if (VaultSession.Data is not null)
                await MobileSync.SyncAsync(null); // pick up changes made on the PC meanwhile
            _leftAt = null;
        };
        return _window;
    }

    // Always opens on the login page (which offers to bring the vault over when the phone has none yet).
    private static Page StartPage() => new UnlockPage();

    public static void ShowRoot(Page page)
    {
        if (Current?.Windows.FirstOrDefault() is { } window) window.Page = page;
    }

    public static void LockNow(string? message = null)
    {
        VaultSession.Lock();
        _tabs = null;
        ShowRoot(new UnlockPage(message));
    }

    // Records · Cards & ID · Security · Settings, as bottom tabs. Syncs with the PC right after unlocking.
    public static void ShowVault()
    {
        var tabs = new TabbedPage()
            .Themed(TabbedPage.BarBackgroundColorProperty, Ui.CardLight, Ui.CardDark)
            .Themed(TabbedPage.UnselectedTabColorProperty, Ui.SubLight, Ui.SubDark)
            .Themed(TabbedPage.SelectedTabColorProperty, Ui.Primary, Ui.PrimaryDark);
        tabs.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
            .SetToolbarPlacement(ToolbarPlacement.Bottom)
            .SetIsSwipePagingEnabled(false);
        tabs.Children.Add(Nav(new VaultPage(), "Records", "tab_records.png"));
        tabs.Children.Add(Nav(new CardsPage(), "Cards & ID", "tab_cards.png"));
        tabs.Children.Add(Nav(new SecurityPage(), "Security", "tab_security.png"));
        tabs.Children.Add(Nav(new SettingsPage(), "Settings", "tab_settings.png"));
        _tabs = tabs;
        ShowRoot(tabs);
        if (MobileSync.Enabled) _ = MobileSync.SyncAsync(null);
    }

    public static void SelectTab(int index)
    {
        if (_tabs is not null && index < _tabs.Children.Count) _tabs.CurrentPage = _tabs.Children[index];
    }

    public static NavigationPage Nav(Page root, string? title = null, string? icon = null)
    {
        var nav = new NavigationPage(root)
            .Themed(NavigationPage.BarBackgroundColorProperty, Ui.CardLight, Ui.CardDark)
            .Themed(NavigationPage.BarTextColorProperty, Ui.TextLight, Ui.TextDark);
        if (title is not null) nav.Title = title;
        if (icon is not null) nav.IconImageSource = icon;
        return nav;
    }
}
