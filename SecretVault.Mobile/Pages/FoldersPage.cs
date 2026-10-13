using Microsoft.Maui.Controls.Shapes;
using SecretVault.Models;

namespace SecretVault.Mobile;

// Folder tree. Filter mode: choose which folder the record list shows. Pick mode: choose a record's folder.
// Both: create, rename and delete folders (same rules as the desktop app, from SecretVault.Core).
public sealed class FoldersPage : AppPage
{
    public enum Mode { Filter, Pick }

    private readonly Mode _mode;
    private readonly string? _current;
    private readonly Action<string?> _chosen;
    private readonly SearchBar _search;
    private readonly VerticalStackLayout _rows = new() { Spacing = 0 };

    public FoldersPage(Mode mode, string? current, Action<string?> chosen) : base(mode == Mode.Filter ? "Folders" : "Choose folder", lockButton: false)
    {
        _mode = mode;
        _current = current is null ? null : VaultOperations.NormalizeFolder(current);
        _chosen = chosen;
        ToolbarItems.Add(new ToolbarItem("New folder", null, async () => await CreateAsync(null)));

        _search = new SearchBar { Placeholder = "Search folders", FontSize = 15 }
            .Themed(SearchBar.TextColorProperty, Ui.TextLight, Ui.TextDark)
            .Themed(SearchBar.PlaceholderColorProperty, Ui.SubLight, Ui.SubDark);
        _search.TextChanged += (_, _) => Refresh();

        Body = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            Children =
            {
                new Border { Margin = new Thickness(12, 10, 12, 6), StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1, Content = _search }
                    .Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark).Themed(Border.StrokeProperty, Ui.BorderLight, Ui.BorderDark),
                new ScrollView { Content = new ContentView { Padding = new Thickness(12, 4, 12, 24), Content = Ui.Card(_rows, padding: 4) } }.Row(1)
            }
        };
    }

    protected override void Refresh()
    {
        if (VaultSession.Data is not { } data) return;
        _rows.Children.Clear();
        var live = VaultOperations.Live(data).ToList();
        var term = _search.Text?.Trim();
        var folders = VaultOperations.FolderTree(data);
        if (!string.IsNullOrEmpty(term))
            folders = folders.Where(f => f.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();

        if (_mode == Mode.Filter && string.IsNullOrEmpty(term))
            AddRow(null, "All folders", 0, live.Count, deletable: false);
        foreach (var folder in folders)
        {
            var depth = string.IsNullOrEmpty(term) ? folder.Count(c => c == '\\') : 0;
            var name = string.IsNullOrEmpty(term) ? VaultOperations.LeafFolderName(folder) : folder.Replace("\\", " › ");
            AddRow(folder, name, depth, live.Count(e => VaultOperations.IsInFolder(e, folder)), deletable: true);
        }
        if (folders.Count == 0 && _rows.Children.Count == 0)
            _rows.Children.Add(new ContentView { Padding = 24, Content = Ui.Text("No folders yet. Tap New folder to create one.", 14, sub: true).Center() });
    }

    private void AddRow(string? path, string name, int depth, int count, bool deletable)
    {
        var selected = string.Equals(path, _current, StringComparison.OrdinalIgnoreCase);
        var label = Ui.Text(name, 16, bold: selected);
        if (selected) label.Themed(Label.TextColorProperty, Ui.Primary, Ui.PrimaryDark);
        label.LineBreakMode = LineBreakMode.TailTruncation;
        label.VerticalOptions = LayoutOptions.Center;
        var icon = new Label { Text = path is null ? "▦" : depth > 0 ? "└" : "▸", FontSize = 15, VerticalOptions = LayoutOptions.Center }
            .Themed(Label.TextColorProperty, Ui.SubLight, Ui.SubDark);
        var countLabel = Ui.Text(count.ToString(), 13, sub: true);
        countLabel.VerticalOptions = LayoutOptions.Center;

        var grid = new Grid
        {
            Padding = new Thickness(12 + depth * 18, 6, 4, 6), ColumnSpacing = 10, MinimumHeightRequest = 50,
            ColumnDefinitions = { new ColumnDefinition(20), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(44) },
            Children = { icon, label.Column(1), countLabel.Column(2) }
        };
        if (path is not null)
        {
            var more = new Button { Text = "⋯", FontSize = 20, BackgroundColor = Colors.Transparent, Padding = 0, WidthRequest = 44, HeightRequest = 40 }
                .Themed(Button.TextColorProperty, Ui.SubLight, Ui.SubDark);
            more.Clicked += async (_, _) => await ShowActionsAsync(path, count);
            grid.Children.Add(more.Column(3));
        }
        if (selected) grid.Themed(VisualElement.BackgroundColorProperty, Ui.TintLight, Ui.TintDark);
        grid.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await ChooseAsync(path)) });
        _rows.Children.Add(grid);
    }

    private async Task ChooseAsync(string? path)
    {
        if (_mode == Mode.Pick && path is null) return;
        _chosen(path);
        await Navigation.PopAsync();
    }

    private async Task ShowActionsAsync(string path, int count)
    {
        // Only empty folders can be deleted (move or delete their records first), like on the desktop.
        var deleteText = count == 0 ? "Delete folder" : null;
        var title = path.Replace("\\", " › ") + (count > 0 ? $"  ({count} records)" : "");
        var action = await DisplayActionSheetAsync(title, "Cancel", deleteText, "New subfolder", "Rename");
        switch (action)
        {
            case "New subfolder": await CreateAsync(path); break;
            case "Rename": await RenameAsync(path); break;
            case "Delete folder":
                if (VaultOperations.DeleteFolder(VaultSession.Vault, path) && await SaveAsync("Folder deleted")) Refresh();
                break;
        }
    }

    private async Task CreateAsync(string? parent)
    {
        var name = await DisplayPromptAsync(parent is null ? "New folder" : "New subfolder",
            parent is null ? "Name of the new folder." : $"Create a folder inside \"{VaultOperations.LeafFolderName(parent)}\".", "Create", "Cancel");
        if (name is null) return;
        if (VaultOperations.CheckNewFolder(VaultSession.Vault, parent, name) is { } problem)
        {
            await DisplayAlertAsync("Can't create folder", problem, "OK");
            return;
        }
        var path = VaultOperations.CreateFolder(VaultSession.Vault, parent, name);
        if (!await SaveAsync($"Folder \"{name.Trim()}\" created")) return;
        if (_mode == Mode.Pick) { await ChooseAsync(path); return; }
        Refresh();
    }

    private async Task RenameAsync(string path)
    {
        var oldName = VaultOperations.LeafFolderName(path);
        var name = await DisplayPromptAsync("Rename folder", $"New name for \"{oldName}\" (records and subfolders move with it).", "Rename", "Cancel", initialValue: oldName);
        if (name is null || name.Trim() == oldName) return;
        var cut = path.LastIndexOf('\\');
        var problem = VaultOperations.CheckFolderName(name)
            ?? (!name.Trim().Equals(oldName, StringComparison.OrdinalIgnoreCase) && VaultOperations.FolderExists(VaultSession.Vault, VaultOperations.JoinFolder(cut < 0 ? null : path[..cut], name))
                ? "A folder with that name already exists." : null);
        if (problem is not null)
        {
            await DisplayAlertAsync("Can't rename folder", problem, "OK");
            return;
        }
        VaultOperations.RenameFolder(VaultSession.Vault, path, name);
        if (await SaveAsync($"Renamed to \"{name.Trim()}\"")) Refresh();
    }
}
