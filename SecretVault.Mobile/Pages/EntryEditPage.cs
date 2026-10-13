using Microsoft.Maui.Controls.Shapes;
using SecretVault.Models;

namespace SecretVault.Mobile;

// Add / edit a record: every field the desktop editor has, including custom fields and the generator.
public sealed class EntryEditPage : AppPage
{
    private static readonly (CustomFieldType Type, string Name)[] FieldTypes =
    {
        (CustomFieldType.Text, "Text"), (CustomFieldType.Password, "Hidden (password)"), (CustomFieldType.PinCode, "PIN"),
        (CustomFieldType.Email, "Email"), (CustomFieldType.Phone, "Phone"), (CustomFieldType.Url, "Website"),
        (CustomFieldType.Date, "Date"), (CustomFieldType.Name, "Name"), (CustomFieldType.Address, "Address"),
        (CustomFieldType.MultiLineText, "Multi-line text")
    };

    private readonly VaultEntry? _original;
    private readonly Entry _name, _username, _password, _url, _totp, _tags;
    private readonly Editor _notes;
    private readonly Button _folderButton;
    private readonly Picker _type;
    private readonly Switch _favorite;
    private readonly VerticalStackLayout _fieldsHost = new() { Spacing = 12 };
    private readonly List<(Entry Label, Entry Value, Picker Type, View Row)> _fields = new();
    private readonly Label _error = Forms.Error();
    private string _folder;

    public EntryEditPage(VaultEntry? original, string? defaultFolder = null, string? defaultType = null)
        : base(original is null ? "New record" : "Edit record", lockButton: false)
    {
        _original = original;
        ToolbarItems.Add(new ToolbarItem("Save", null, async () => await SaveRecordAsync()));

        _name = Forms.Entry(original?.Name ?? "", "e.g. GitHub");
        _username = Forms.Entry(original?.Username ?? "", "Username or email", keyboard: Keyboard.Email);
        _password = Forms.Entry(original?.Secret ?? "", "Password", password: true);
        _url = Forms.Entry(original?.Url ?? "", "https://", keyboard: Keyboard.Url);
        _totp = Forms.Entry(original?.TotpSecret ?? "", "Base32 secret from the site's 2FA setup", keyboard: Keyboard.Plain);
        _tags = Forms.Entry(string.Join(", ", original?.Tags ?? new List<string>()), "work, 2fa");
        _notes = Forms.Editor(original?.Notes ?? "", "Notes");
        _folder = original?.Folder ?? defaultFolder ?? VaultOperations.DefaultFolder;

        _folderButton = new Button { FontSize = 16, HeightRequest = 48, CornerRadius = 12, BorderWidth = 1, Padding = new Thickness(12, 0) }
            .Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark)
            .Themed(Button.TextColorProperty, Ui.TextLight, Ui.TextDark)
            .Themed(Button.BorderColorProperty, Ui.BorderLight, Ui.BorderDark);
        _folderButton.Clicked += async (_, _) => await Navigation.PushAsync(new FoldersPage(FoldersPage.Mode.Pick, _folder, chosen =>
        {
            if (chosen is not null) { _folder = chosen; UpdateFolderButton(); }
        }));
        UpdateFolderButton();

        _type = new Picker { Title = "Record type", FontSize = 16 }.Themed(Picker.TextColorProperty, Ui.TextLight, Ui.TextDark);
        _type.Items.Add("Auto-detect from contents");
        foreach (var t in RecordTypes.All) _type.Items.Add(t);
        var chosenType = original is null ? defaultType : original.Type;
        _type.SelectedIndex = string.IsNullOrWhiteSpace(chosenType) ? 0 : Math.Max(0, Array.IndexOf(RecordTypes.All, chosenType) + 1);

        _favorite = new Switch { IsToggled = original?.Favorite ?? false }.Themed(Switch.OnColorProperty, Ui.Primary, Ui.PrimaryDark);

        foreach (var field in original?.CustomFields ?? new List<CustomField>()) AddField(field);
        var addField = Ui.PillButton("+ Add field");
        addField.HorizontalOptions = LayoutOptions.Start;
        addField.Clicked += (_, _) => AddField(new CustomField());

        var save = Ui.PrimaryButton(original is null ? "Add record" : "Save changes");
        save.Clicked += async (_, _) => await SaveRecordAsync();

        View Section(string caption, params View[] views)
        {
            var stack = new VerticalStackLayout { Spacing = 14, Children = { Ui.Caption(caption) } };
            foreach (var v in views) stack.Children.Add(v);
            return Ui.Card(stack);
        }

        Body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 40), Spacing = 14,
                Children =
                {
                    Section("Record",
                        Forms.Field("Name *", _name),
                        new VerticalStackLayout { Spacing = 5, Children = { Ui.Text("Folder", 13, bold: true, sub: true), _folderButton } },
                        Forms.Field("Record type", _type),
                        new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                                   Children = { Ui.Text("Favourite", 16).WithCenter(), _favorite.Column(1) } }),
                    Section("Login",
                        Forms.Field("Username", _username),
                        Forms.PasswordField("Password", _password, this),
                        Forms.StrengthBar(_password),
                        Forms.Field("Website", _url),
                        Forms.Field("Two-factor (TOTP) secret", _totp, hint: "Shows a live one-time code on the record.")),
                    Ui.Card(new VerticalStackLayout { Spacing = 14, Children = { Ui.Caption("Custom fields"), _fieldsHost, addField } }),
                    Section("Tags & notes", Forms.Field("Tags", _tags, hint: "Separate with commas."), Forms.Field("Notes", _notes)),
                    _error,
                    save
                }
            }
        };
    }

    private void UpdateFolderButton() => _folderButton.Text = "📁  " + _folder.Replace("\\", " › ");

    private void AddField(CustomField field)
    {
        var label = Forms.Entry(field.Label, "Label (e.g. Hostname)");
        var value = Forms.Entry(field.Value, "Value", password: field.IsSecret);
        var type = new Picker { Title = "Type", FontSize = 14 }.Themed(Picker.TextColorProperty, Ui.TextLight, Ui.TextDark);
        foreach (var (_, name) in FieldTypes) type.Items.Add(name);
        type.SelectedIndex = Math.Max(0, Array.FindIndex(FieldTypes, f => f.Type == field.Type));
        type.SelectedIndexChanged += (_, _) => value.IsPassword = FieldTypes[type.SelectedIndex].Type is CustomFieldType.Password or CustomFieldType.PinCode;

        var remove = new Button { Text = "Remove", FontSize = 13, BackgroundColor = Colors.Transparent, Padding = new Thickness(8, 0), HeightRequest = 36 }
            .Themed(Button.TextColorProperty, Ui.DangerLight, Ui.DangerDark);
        View row = null!;
        remove.Clicked += (_, _) =>
        {
            _fieldsHost.Children.Remove(row);
            _fields.RemoveAll(f => f.Row == row);
        };
        row = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 1, Padding = new Thickness(12, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Children = { type, remove.Column(1) } },
                    label, value
                }
            }
        }.Themed(Border.StrokeProperty, Ui.BorderLight, Ui.BorderDark);
        _fields.Add((label, value, type, row));
        _fieldsHost.Children.Add(row);
    }

    private async Task SaveRecordAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { _error.Show("Give the record a name."); _name.Focus(); return; }

        var entry = VaultOperations.Stamp(new VaultEntry
        {
            Name = _name.Text ?? "",
            Folder = _folder,
            Username = _username.Text?.Trim() ?? "",
            Secret = _password.Text ?? "",
            Url = _url.Text?.Trim() ?? "",
            TotpSecret = _totp.Text,
            Notes = _notes.Text?.Trim() ?? "",
            Type = _type.SelectedIndex <= 0 ? null : RecordTypes.All[_type.SelectedIndex - 1],
            Favorite = _favorite.IsToggled,
            Tags = VaultOperations.SplitTags(_tags.Text ?? ""),
            CustomFields = _fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Label.Text) || !string.IsNullOrWhiteSpace(f.Value.Text))
                .Select(f => new CustomField { Label = f.Label.Text?.Trim() ?? "", Value = f.Value.Text ?? "", Type = FieldTypes[Math.Max(0, f.Type.SelectedIndex)].Type })
                .ToList()
        }, _original);

        var data = VaultSession.Vault;
        var index = _original is null ? -1 : data.Entries.IndexOf(_original);
        if (index >= 0) data.Entries[index] = entry;
        else data.Entries.Add(entry);

        if (!await SaveAsync()) return;
        await Navigation.PopAsync();
        if (_original is null && Navigation.NavigationStack.LastOrDefault() is VaultPage list) _ = list.ToastAsync($"\"{entry.Name}\" added");
    }
}

internal static class LabelExtensions
{
    public static Label WithCenter(this Label label)
    {
        label.VerticalOptions = LayoutOptions.Center;
        return label;
    }
}
