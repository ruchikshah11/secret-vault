using Microsoft.Maui.Controls.Shapes;

namespace SecretVault.Mobile;

// Shows a new recovery key once (same flow as the desktop app): copy or write it down, confirm, done.
public sealed class RecoveryKeyPage : ContentPage
{
    private readonly CheckBox _saved = new() { Color = Ui.Primary };

    private RecoveryKeyPage(string key)
    {
        this.CanvasBackground();
        var keyLabel = new Label
        {
            Text = key, FontFamily = "monospace", FontSize = 20, FontAttributes = FontAttributes.Bold, CharacterSpacing = 1,
            HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap
        }.Themed(Label.TextColorProperty, Ui.TextLight, Ui.TextDark);
        var copy = Ui.SecondaryButton("Copy key");
        copy.Clicked += (_, _) => SecureClipboard.Copy(key, "Recovery key");
        var done = Ui.PrimaryButton("Done");
        done.IsEnabled = false;
        _saved.CheckedChanged += (_, e) => done.IsEnabled = e.Value;
        done.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var savedRow = new HorizontalStackLayout { Spacing = 6, Children = { _saved, Ui.Text("I've saved my recovery key somewhere safe", 15) } };
        ((Label)savedRow.Children[1]).VerticalOptions = LayoutOptions.Center;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 40, 24, 24), Spacing = 16,
                Children =
                {
                    Ui.Text("Your recovery key", 26, bold: true),
                    Ui.Text("If you forget your master password, this key lets you set a new one — on this phone or any PC with your vault. It's shown only once.", 15, sub: true),
                    new Border
                    {
                        StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1, Padding = new Thickness(16, 22), Content = keyLabel
                    }.Themed(VisualElement.BackgroundColorProperty, Ui.TintLight, Ui.TintDark).Themed(Border.StrokeProperty, Ui.Primary, Ui.PrimaryDark),
                    copy,
                    Ui.Text("Store it away from this phone: on paper, or in another password manager. Anyone with this key and your vault file can open the vault.", 13, sub: true),
                    savedRow,
                    done
                }
            }
        };
    }

    protected override bool OnBackButtonPressed()
    {
        if (_saved.IsChecked) return base.OnBackButtonPressed();
        Dispatcher.Dispatch(async () =>
        {
            if (await DisplayAlertAsync("Close without saving the key?", "You haven't confirmed that you saved the recovery key. It won't be shown again.", "Close anyway", "Go back"))
                await Navigation.PopModalAsync();
        });
        return true;
    }

    // Creates (or replaces) the vault's recovery key and shows it.
    public static async Task CreateAndShowAsync(Page owner)
    {
        var key = await Task.Run(() => VaultSession.Store.CreateRecoveryKeyAsync(VaultSession.Vault, VaultSession.Password));
        MobileSync.SyncSoon();
        await owner.Navigation.PushModalAsync(new RecoveryKeyPage(key));
    }
}
