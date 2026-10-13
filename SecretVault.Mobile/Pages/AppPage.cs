using Microsoft.Maui.Controls.Shapes;

namespace SecretVault.Mobile;

// Base for the app's pages: themed background, a Lock button, a toast layer, and a refresh when the vault
// changes underneath (e.g. a sync brought in records from the PC).
public abstract class AppPage : ContentPage
{
    private readonly Grid _root = new();
    private readonly Border _toast;
    private readonly Label _toastText = new() { TextColor = Colors.White, FontSize = 14 };
    private int _toastGeneration;

    protected AppPage(string title, bool lockButton = true)
    {
        Title = title;
        this.CanvasBackground();
        if (lockButton) ToolbarItems.Add(new ToolbarItem("Lock", null, () => App.LockNow()));
        _toast = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 20 }, StrokeThickness = 0, Padding = new Thickness(18, 11),
            BackgroundColor = Color.FromArgb("#E61E2026"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(24, 0, 24, 90), InputTransparent = true, IsVisible = false, Content = _toastText
        };
        _root.Children.Add(_toast);
        Content = _root;
    }

    // The page's own content (kept under the toast layer).
    protected View Body
    {
        set
        {
            foreach (var old in _root.Children.Where(c => c != _toast).ToList()) _root.Children.Remove(old);
            _root.Children.Insert(0, value);
        }
    }

    public async Task ToastAsync(string message)
    {
        var generation = ++_toastGeneration;
        _toastText.Text = message;
        _toast.Opacity = 0;
        _toast.IsVisible = true;
        await _toast.FadeToAsync(1, 150);
        await Task.Delay(2200);
        if (generation != _toastGeneration) return;
        await _toast.FadeToAsync(0, 250);
        if (generation == _toastGeneration) _toast.IsVisible = false;
    }

    // Rebuild from the current vault contents (override in pages that show vault data).
    protected virtual void Refresh() { }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        VaultSession.Changed += OnVaultChanged;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        VaultSession.Changed -= OnVaultChanged;
        base.OnDisappearing();
    }

    private void OnVaultChanged()
    {
        if (VaultSession.Data is not null) Refresh();
    }

    // Saves the vault, showing a message if that fails (e.g. storage full).
    protected async Task<bool> SaveAsync(string? toast = null)
    {
        try
        {
            await VaultSession.SaveAsync();
            if (toast is not null) _ = ToastAsync(toast);
            return true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't save", ex.Message, "OK");
            return false;
        }
    }

    protected Task<bool> ConfirmAsync(string title, string message, string accept, string cancel = "Cancel") =>
        DisplayAlertAsync(title, message, accept, cancel);
}
