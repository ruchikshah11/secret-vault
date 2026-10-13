using Microsoft.Maui.Controls.Shapes;
using SecretVault.Models;

namespace SecretVault.Mobile;

public sealed record EntryRow(VaultEntry Entry, string Name, string Subtitle, string Folder, string Initial, Color Color, Color Tint, bool Favorite);

// Records: search, filter chips (All · Favorites · record types · Deleted), folder filter, sort, new record.
public sealed class VaultPage : AppPage
{
    private const string All = "All", Favorites = "Favorites", Deleted = "Deleted";

    private readonly SearchBar _search;
    private readonly HorizontalStackLayout _chips = new() { Spacing = 8, Padding = new Thickness(16, 0) };
    private readonly CollectionView _list;
    private readonly Label _count = Ui.Text("", 13, sub: true);
    private readonly Button _folderButton;
    private readonly Button _sortButton;
    private readonly View _recoveryBanner;
    private string _filter = All;
    private string? _folder;

    public VaultPage() : base("Records")
    {
        ToolbarItems.Insert(0, new ToolbarItem("New", null, async () => await NewRecordAsync()));
        ToolbarItems.Add(new ToolbarItem("Folders", null, async () => await ChooseFolderAsync()) { Order = ToolbarItemOrder.Secondary });
        ToolbarItems.Add(new ToolbarItem("Password generator", null, async () => await Navigation.PushAsync(new GeneratorPage())) { Order = ToolbarItemOrder.Secondary });
        ToolbarItems.Add(new ToolbarItem("Sync now", null, async () => await SyncNowAsync()) { Order = ToolbarItemOrder.Secondary });

        _search = new SearchBar { Placeholder = "Search records, usernames, websites…", FontSize = 15 }
            .Themed(SearchBar.TextColorProperty, Ui.TextLight, Ui.TextDark)
            .Themed(SearchBar.PlaceholderColorProperty, Ui.SubLight, Ui.SubDark)
            .Themed(SearchBar.CancelButtonColorProperty, Ui.Primary, Ui.PrimaryDark);
        _search.TextChanged += (_, _) => Refresh();

        _folderButton = Ui.PillButton("All folders ▾");
        _folderButton.Clicked += async (_, _) => await ChooseFolderAsync();
        _sortButton = Ui.PillButton("");
        _sortButton.Clicked += (_, _) => { AppPrefs.SortByRecent = !AppPrefs.SortByRecent; Refresh(); };

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(BuildRow),
            EmptyView = new VerticalStackLayout
            {
                Padding = 40, Spacing = 6,
                Children = { Ui.Text("No records here", 17, bold: true).Center(), Ui.Text("Try a different search or filter, or tap + to add one.", 14, sub: true).Center() }
            },
            Footer = new BoxView { HeightRequest = 90, Color = Colors.Transparent }
        };
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not EntryRow row) return;
            _list.SelectedItem = null;
            await Navigation.PushAsync(new EntryPage(row.Entry));
        };

        _recoveryBanner = BuildRecoveryBanner();

        var add = new Button
        {
            Text = "+", FontSize = 30, TextColor = Colors.White, WidthRequest = 60, HeightRequest = 60, CornerRadius = 30, Padding = 0,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 20, 20),
            Shadow = new Shadow { Radius = 10, Opacity = 0.3f, Offset = new Point(0, 4) }
        }.Themed(VisualElement.BackgroundColorProperty, Ui.Primary, Ui.PrimaryDark);
        add.Clicked += async (_, _) => await NewRecordAsync();

        var page = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)
            },
            Children =
            {
                new Border { Margin = new Thickness(12, 10, 12, 6), StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1, Content = _search }
                    .Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark)
                    .Themed(Border.StrokeProperty, Ui.BorderLight, Ui.BorderDark),
                _recoveryBanner.Row(1),
                new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _chips, Margin = new Thickness(0, 4) }.Row(2),
                new Grid
                {
                    Padding = new Thickness(16, 4, 16, 2), ColumnSpacing = 8,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                    Children = { _count.Column(0), _folderButton.Column(1), _sortButton.Column(2) }
                }.Row(3),
                _list.Row(4),
                add.Row(4)
            }
        };
        _count.VerticalOptions = LayoutOptions.Center;
        Body = page;
    }

    protected override void Refresh()
    {
        if (VaultSession.Data is not { } data) return;
        var live = VaultOperations.Live(data).ToList();
        var deleted = VaultOperations.Deleted(data).ToList();
        BuildChips(live, deleted.Count);

        if (_folder is not null && !VaultOperations.FolderTree(data).Contains(_folder, StringComparer.OrdinalIgnoreCase)) _folder = null;
        _folderButton.Text = _folder is null ? "All folders ▾" : $"{VaultOperations.LeafFolderName(_folder)} ▾";
        _sortButton.Text = AppPrefs.SortByRecent ? "Recent ▾" : "A–Z ▾";
        _folderButton.IsVisible = _filter != Deleted;

        IEnumerable<VaultEntry> shown = _filter switch
        {
            All => live,
            Favorites => live.Where(e => e.Favorite),
            Deleted => deleted,
            _ => live.Where(e => RecordTypes.Of(e) == _filter)
        };
        if (_folder is not null && _filter != Deleted) shown = shown.Where(e => VaultOperations.IsInFolder(e, _folder));
        var term = _search.Text?.Trim();
        if (!string.IsNullOrEmpty(term)) shown = shown.Where(e => Matches(e, term));

        shown = AppPrefs.SortByRecent
            ? shown.OrderByDescending(e => e.DeletedAt ?? e.ModifiedAt)
            : shown.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
        var rows = shown.Select(ToRow).ToList();
        _list.ItemsSource = rows;
        _count.Text = (rows.Count == 1 ? "1 record" : $"{rows.Count} records") + (_filter == Deleted ? " in Deleted Items" : "");
        UpdateRecoveryBanner();
    }

    private static bool Matches(VaultEntry e, string term)
    {
        bool Has(string? s) => s?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;
        return Has(e.Name) || Has(e.Username) || Has(e.Url) || Has(e.Folder) || Has(e.Notes)
            || e.Tags.Any(Has) || e.CustomFields.Any(f => !f.IsSecret && Has(f.Value));
    }

    private static EntryRow ToRow(VaultEntry e)
    {
        var type = RecordTypes.Of(e);
        var color = Ui.TypeColor(type);
        var subtitle = e.DeletedAt is { } d ? $"Deleted {d.LocalDateTime:d MMM yyyy}"
            : !string.IsNullOrEmpty(e.Username) ? e.Username : type;
        var initial = e.Name.FirstOrDefault(char.IsLetterOrDigit);
        return new EntryRow(e, e.Name, subtitle, VaultOperations.LeafFolderName(e.Folder),
            initial == default ? "•" : char.ToUpperInvariant(initial).ToString(), color, color.WithAlpha(0.16f), e.Favorite);
    }

    private void BuildChips(List<VaultEntry> live, int deletedCount)
    {
        _chips.Children.Clear();
        void Chip(string name, int count, bool always = false)
        {
            if (count == 0 && !always && _filter != name) return;
            var selected = _filter == name;
            var label = new Label { Text = $"{name}  {count}", FontSize = 14, FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None };
            var chip = new Border { StrokeShape = new RoundRectangle { CornerRadius = 18 }, StrokeThickness = 1, Padding = new Thickness(14, 7), Content = label };
            if (selected)
            {
                chip.Themed(VisualElement.BackgroundColorProperty, Ui.Primary, Ui.PrimaryDark).Themed(Border.StrokeProperty, Ui.Primary, Ui.PrimaryDark);
                label.TextColor = Colors.White;
            }
            else
            {
                chip.Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark).Themed(Border.StrokeProperty, Ui.BorderLight, Ui.BorderDark);
                label.Themed(Label.TextColorProperty, Ui.TextLight, Ui.TextDark);
            }
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _filter = name; Refresh(); }) });
            _chips.Children.Add(chip);
        }

        Chip(All, live.Count, always: true);
        Chip(Favorites, live.Count(e => e.Favorite));
        foreach (var type in RecordTypes.All)
            Chip(type, live.Count(e => RecordTypes.Of(e) == type));
        Chip(Deleted, deletedCount);
    }

    private async Task ChooseFolderAsync()
    {
        await Navigation.PushAsync(new FoldersPage(FoldersPage.Mode.Filter, _folder, chosen =>
        {
            _folder = chosen;
            if (_filter == Deleted) _filter = All;
            Refresh();
        }));
    }

    private async Task NewRecordAsync()
    {
        var type = RecordTypes.All.Contains(_filter) ? _filter : null;
        await Navigation.PushAsync(new EntryEditPage(null, _folder, type));
    }

    private async Task SyncNowAsync()
    {
        if (!MobileSync.Enabled)
        {
            if (await DisplayAlertAsync("Sync isn't set up", "Set up sync with your PC in Settings › Sync.", "Open Settings", "Cancel"))
                App.SelectTab(3);
            return;
        }
        var summary = await MobileSync.SyncAsync(this);
        if (summary is not null) { Refresh(); await ToastAsync("Synced · " + summary); }
    }

    // ---------- recovery key reminder (like the desktop banner) ----------

    private bool _recoveryMissing;

    private View BuildRecoveryBanner()
    {
        var create = Ui.PillButton("Create");
        create.Clicked += async (_, _) =>
        {
            await RecoveryKeyPage.CreateAndShowAsync(this);
            _recoveryMissing = false;
            UpdateRecoveryBanner();
        };
        var later = new Button { Text = "Later", FontSize = 13, BackgroundColor = Colors.Transparent, Padding = new Thickness(8, 0), HeightRequest = 36 }
            .Themed(Button.TextColorProperty, Ui.SubLight, Ui.SubDark);
        later.Clicked += (_, _) =>
        {
            AppPrefs.RecoveryReminderSnoozedUntil = DateTimeOffset.UtcNow.AddDays(7);
            UpdateRecoveryBanner();
        };
        var banner = new Border
        {
            Margin = new Thickness(12, 2, 12, 4), Padding = new Thickness(14, 10), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 14 },
            IsVisible = false,
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 6,
                Children =
                {
                    new VerticalStackLayout { Spacing = 2, Children = { Ui.Text("No recovery key yet", 14, bold: true), Ui.Text("Without it a forgotten master password can't be reset.", 12, sub: true) } },
                    later.Column(1), create.Column(2)
                }
            }
        }.Themed(VisualElement.BackgroundColorProperty, Color.FromArgb("#FFF7E6"), Color.FromArgb("#33280F")).Themed(Border.StrokeProperty, Color.FromArgb("#F5C46B"), Color.FromArgb("#6B5320"));
        _ = CheckRecoveryKeyAsync();
        return banner;
    }

    private async Task CheckRecoveryKeyAsync()
    {
        try { _recoveryMissing = !await VaultSession.Store.HasRecoveryKeyAsync(); }
        catch (Exception) { _recoveryMissing = false; }
        UpdateRecoveryBanner();
    }

    private void UpdateRecoveryBanner()
    {
        var snoozed = AppPrefs.RecoveryReminderSnoozedUntil is { } until && until > DateTimeOffset.UtcNow;
        if (_recoveryBanner is not null) _recoveryBanner.IsVisible = _recoveryMissing && !snoozed;
    }

    private static View BuildRow()
    {
        var initial = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        initial.SetBinding(Label.TextProperty, static (EntryRow r) => r.Initial);
        initial.SetBinding(Label.TextColorProperty, static (EntryRow r) => r.Color);
        var avatar = new Border { WidthRequest = 44, HeightRequest = 44, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 22 }, Content = initial, VerticalOptions = LayoutOptions.Center };
        avatar.SetBinding(VisualElement.BackgroundColorProperty, static (EntryRow r) => r.Tint);

        var name = Ui.Text("", 16, bold: true);
        name.LineBreakMode = LineBreakMode.TailTruncation;
        name.SetBinding(Label.TextProperty, static (EntryRow r) => r.Name);
        var subtitle = Ui.Text("", 13, sub: true);
        subtitle.LineBreakMode = LineBreakMode.TailTruncation;
        subtitle.SetBinding(Label.TextProperty, static (EntryRow r) => r.Subtitle);

        var star = new Label { Text = "★", FontSize = 16, TextColor = Ui.Gold, HorizontalOptions = LayoutOptions.End };
        star.SetBinding(IsVisibleProperty, static (EntryRow r) => r.Favorite);
        var folder = Ui.Text("", 12, sub: true);
        folder.HorizontalOptions = LayoutOptions.End;
        folder.MaximumWidthRequest = 110;
        folder.LineBreakMode = LineBreakMode.TailTruncation;
        folder.SetBinding(Label.TextProperty, static (EntryRow r) => r.Folder);

        var grid = new Grid
        {
            Padding = new Thickness(14, 12),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 14,
            Children =
            {
                avatar,
                new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { name, subtitle } }.Column(1),
                new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { star, folder } }.Column(2)
            }
        };
        return new ContentView { Padding = new Thickness(12, 4), Content = Ui.Card(grid, padding: 0) };
    }
}
