using Microsoft.Maui.Controls.Shapes;
using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Mobile;

// One record: tap-to-copy fields, live one-time code, history, and Edit / Move / Favourite / Delete.
public sealed class EntryPage : AppPage
{
    private readonly string _id;
    private VaultEntry? _entry;
    private Label? _totpCode;
    private ProgressBar? _totpProgress;
    private Label? _totpSeconds;
    private bool _visible;
    private int _timerGeneration;

    public EntryPage(VaultEntry entry) : base(entry.Name)
    {
        _id = entry.Id;
        _entry = entry;
        ToolbarItems.Insert(0, new ToolbarItem("Edit", null, async () => await EditAsync()));
        ToolbarItems.Insert(1, new ToolbarItem("More", null, async () => await ShowActionsAsync()));
    }

    private VaultEntry? Find() =>
        VaultSession.Data?.Entries.FirstOrDefault(e => e.Id == _id && _id.Length > 0) ?? (_entry is not null && VaultSession.Data?.Entries.Contains(_entry) == true ? _entry : null);

    protected override void Refresh()
    {
        _entry = Find();
        if (_entry is null)
        {
            // Deleted forever (here or by a sync): leave the page.
            Dispatcher.Dispatch(async () => { if (Navigation.NavigationStack.LastOrDefault() == this) await Navigation.PopAsync(); });
            return;
        }
        Title = _entry.Name;
        Body = new ScrollView { Content = Build(_entry) };
        StartTotpTimer();
    }

    private View Build(VaultEntry entry)
    {
        var type = RecordTypes.Of(entry);
        var color = Ui.TypeColor(type);
        var stack = new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 32), Spacing = 14 };
        _totpCode = null;

        if (entry.DeletedAt is { } deletedAt)
        {
            var restore = Ui.PillButton("Restore");
            restore.Clicked += async (_, _) => await RestoreAsync();
            stack.Children.Add(new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 0, Padding = new Thickness(14, 10),
                Content = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    Children = { Ui.Text($"In Deleted Items since {deletedAt.LocalDateTime:d MMM yyyy}", 14), restore.Column(1) }
                }
            }.Themed(VisualElement.BackgroundColorProperty, Color.FromArgb("#FDECEC"), Color.FromArgb("#3A1D1D")));
        }

        // Header
        var meta = $"{type} · {entry.Folder.Replace("\\", " › ")}";
        var star = new Button { Text = entry.Favorite ? "★" : "☆", FontSize = 24, TextColor = entry.Favorite ? Ui.Gold : Colors.Gray, BackgroundColor = Colors.Transparent, Padding = 0, WidthRequest = 44 };
        star.Clicked += async (_, _) => await ToggleFavoriteAsync();
        stack.Children.Add(Ui.Card(new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 14,
            Children =
            {
                Ui.Avatar(entry.Name, color, 56),
                new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { Ui.Text(entry.Name, 20, bold: true), Ui.Text(meta, 13, sub: true) } }.Column(1),
                star.Column(2)
            }
        }));

        // Login
        var login = new List<View>();
        if (entry.Username.Length > 0) login.Add(FieldRow.Create("Username", entry.Username));
        if (entry.Secret.Length > 0)
        {
            login.Add(FieldRow.Create("Password", entry.Secret, secret: true));
            var level = PasswordStrength.Evaluate(entry.Secret);
            login.Add(new HorizontalStackLayout
            {
                Spacing = 8, Padding = new Thickness(0, 0, 0, 8),
                Children = { new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Forms.StrengthColor(level), VerticalOptions = LayoutOptions.Center },
                             Ui.Text("Strength: " + Forms.StrengthName(level), 12, sub: true) }
            });
        }
        if (!string.IsNullOrWhiteSpace(entry.TotpSecret)) login.Add(BuildTotpRow(entry.TotpSecret!));
        if (entry.Url.Length > 0)
        {
            var open = Ui.PillButton("Open");
            open.Clicked += async (_, _) => await OpenWebsiteAsync(entry.Url);
            login.Add(FieldRow.Create("Website", entry.Url, extraButton: CsvTransfer.HostOf(entry.Url) is null ? null : open));
        }
        if (login.Count > 0) stack.Children.Add(FieldRow.Section(null, login));

        // Custom fields
        var fields = entry.CustomFields.Where(f => f.Value.Length > 0)
            .Select(f => FieldRow.Create(f.Label, f.Value, secret: f.IsSecret, multiLine: f.IsMultiLine)).ToList();
        if (fields.Count > 0) stack.Children.Add(FieldRow.Section("Details", fields));

        // Notes
        if (!string.IsNullOrWhiteSpace(entry.Notes))
        {
            var copy = Ui.PillButton("Copy");
            copy.HorizontalOptions = LayoutOptions.Start;
            copy.Clicked += (_, _) => SecureClipboard.Copy(entry.Notes, "Notes");
            stack.Children.Add(Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Caption("Notes"), Ui.Text(entry.Notes, 15), copy } }));
        }

        // Tags
        if (entry.Tags.Count > 0)
        {
            var tags = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var tag in entry.Tags)
            {
                tags.Children.Add(new Border
                {
                    StrokeThickness = 0, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 5),
                    StrokeShape = new RoundRectangle { CornerRadius = 14 },
                    Content = Ui.Text(tag, 13).Themed(Label.TextColorProperty, Ui.Primary, Ui.PrimaryDark)
                }.Themed(VisualElement.BackgroundColorProperty, Ui.TintLight, Ui.TintDark));
            }
            stack.Children.Add(Ui.Card(new VerticalStackLayout { Spacing = 10, Children = { Ui.Caption("Tags"), tags } }));
        }

        // Password history
        if (entry.PasswordHistory.Count > 0)
        {
            var rows = entry.PasswordHistory.Select(h => FieldRow.Create($"Used until {h.ChangedAt.LocalDateTime:d MMM yyyy, HH:mm}", h.Password, secret: true, copyName: "Old password"));
            stack.Children.Add(FieldRow.Section($"Password history ({entry.PasswordHistory.Count})", rows));
        }

        stack.Children.Add(Ui.Text($"Created {entry.CreatedAt.LocalDateTime:d MMM yyyy} · Modified {entry.ModifiedAt.LocalDateTime:d MMM yyyy, HH:mm}", 12, sub: true).Center());
        return stack;
    }

    // ---------- actions ----------

    private async Task EditAsync()
    {
        if (_entry is null) return;
        if (_entry.DeletedAt is not null) { await DisplayAlertAsync("In Deleted Items", "Restore this record to edit it.", "OK"); return; }
        await Navigation.PushAsync(new EntryEditPage(_entry));
    }

    private async Task ShowActionsAsync()
    {
        if (_entry is not { } entry) return;
        if (entry.DeletedAt is not null)
        {
            var choice = await DisplayActionSheetAsync(entry.Name, "Cancel", "Delete forever", "Restore");
            if (choice == "Restore") await RestoreAsync();
            else if (choice == "Delete forever") await PurgeAsync();
            return;
        }
        var action = await DisplayActionSheetAsync(entry.Name, "Cancel", "Delete", "Edit", "Move to folder…",
            entry.Favorite ? "Remove from favourites" : "Add to favourites", "Copy username", "Copy password");
        switch (action)
        {
            case "Edit": await EditAsync(); break;
            case "Move to folder…": await MoveAsync(); break;
            case "Add to favourites" or "Remove from favourites": await ToggleFavoriteAsync(); break;
            case "Copy username" when entry.Username.Length > 0: SecureClipboard.Copy(entry.Username, "Username"); break;
            case "Copy password" when entry.Secret.Length > 0: SecureClipboard.Copy(entry.Secret, "Password"); break;
            case "Delete": await DeleteAsync(); break;
        }
    }

    private async Task MoveAsync()
    {
        if (_entry is not { } entry) return;
        await Navigation.PushAsync(new FoldersPage(FoldersPage.Mode.Pick, entry.Folder, async folder =>
        {
            if (folder is null || !VaultOperations.MoveEntry(entry, folder)) return;
            if (await SaveAsync($"Moved to {VaultOperations.LeafFolderName(folder)}")) Refresh();
        }));
    }

    private async Task ToggleFavoriteAsync()
    {
        if (_entry is not { } entry) return;
        VaultOperations.ToggleFavorite(entry);
        if (await SaveAsync(entry.Favorite ? "Added to favourites" : "Removed from favourites")) Refresh();
    }

    private async Task DeleteAsync()
    {
        if (_entry is not { } entry) return;
        if (!await ConfirmAsync("Delete this record?", $"\"{entry.Name}\" will move to Deleted Items. You can restore it from there.", "Move to Deleted Items")) return;
        VaultOperations.MoveToDeletedItems(entry);
        if (await SaveAsync()) await Navigation.PopAsync();
    }

    private async Task RestoreAsync()
    {
        if (_entry is not { } entry) return;
        VaultOperations.Restore(entry);
        if (await SaveAsync("Restored")) Refresh();
    }

    private async Task PurgeAsync()
    {
        if (_entry is not { } entry) return;
        if (!await ConfirmAsync("Delete forever?", $"\"{entry.Name}\" will be permanently deleted. This can't be undone.", "Delete forever")) return;
        VaultOperations.DeleteForever(VaultSession.Vault, entry);
        _entry = null;
        if (await SaveAsync()) await Navigation.PopAsync();
    }

    // ---------- one-time code ----------

    private View BuildTotpRow(string secret)
    {
        _totpCode = new Label { FontSize = 26, FontAttributes = FontAttributes.Bold, FontFamily = "monospace", CharacterSpacing = 2 }
            .Themed(Label.TextColorProperty, Ui.Primary, Ui.PrimaryDark);
        _totpSeconds = Ui.Text("", 12, sub: true);
        _totpProgress = new ProgressBar { HeightRequest = 4, ProgressColor = Ui.Primary };
        var copy = Ui.PillButton("Copy");
        copy.Clicked += (_, _) => CopyTotp(secret);
        UpdateTotp();

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 10),
            Children =
            {
                new VerticalStackLayout { Spacing = 3, Children = { Ui.Text("One-time code", 12, sub: true), _totpCode, _totpProgress, _totpSeconds } },
                copy.Column(1)
            }
        };
        grid.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => CopyTotp(secret)) });
        return grid;
    }

    private static void CopyTotp(string secret)
    {
        try { SecureClipboard.Copy(TotpGenerator.GenerateCode(secret), "One-time code"); }
        catch { /* invalid secret: the row already says so */ }
    }

    private void UpdateTotp()
    {
        if (_totpCode is null || _entry?.TotpSecret is not { } secret) return;
        try
        {
            var code = TotpGenerator.GenerateCode(secret);
            _totpCode.Text = code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
            var left = TotpGenerator.SecondsRemaining();
            _totpProgress!.Progress = left / 30.0;
            _totpSeconds!.Text = $"Changes in {left} s";
        }
        catch
        {
            _totpCode.Text = "Invalid secret";
            _totpCode.FontSize = 16;
        }
    }

    private void StartTotpTimer()
    {
        if (_totpCode is null || !_visible) return;
        var generation = ++_timerGeneration; // a rebuild or returning to the page must not start a second timer
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!_visible || generation != _timerGeneration) return false;
            UpdateTotp();
            return true;
        });
    }

    protected override void OnAppearing()
    {
        _visible = true;
        base.OnAppearing(); // calls Refresh, which starts the timer
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        base.OnDisappearing();
    }

    private async Task OpenWebsiteAsync(string url)
    {
        var target = url.Contains("://") ? url : "https://" + url;
        try { await Launcher.Default.OpenAsync(new Uri(target)); }
        catch { await DisplayAlertAsync("Open website", "No app on this phone can open this address.", "OK"); }
    }
}
