using Microsoft.Maui.Controls.Shapes;
using SecretVault.Models;

namespace SecretVault.Mobile;

// Identity & Payments: payment cards (drawn like the desktop app) and personal info.
public sealed class CardsPage : AppPage
{
    private bool _showIdentity;
    private readonly Button _cardsTab, _identityTab;

    public CardsPage() : base("Cards & ID")
    {
        ToolbarItems.Insert(0, new ToolbarItem("New", null, async () => await NewAsync()));
        _cardsTab = Ui.PillButton("Payment cards");
        _identityTab = Ui.PillButton("Personal info");
        _cardsTab.Clicked += (_, _) => { _showIdentity = false; Refresh(); };
        _identityTab.Clicked += (_, _) => { _showIdentity = true; Refresh(); };
    }

    private async Task NewAsync()
    {
        if (_showIdentity) await Navigation.PushAsync(new PersonalInfoEditPage(null));
        else await Navigation.PushAsync(new CardEditPage(null));
    }

    protected override void Refresh()
    {
        if (VaultSession.Data is not { } data) return;
        StyleTab(_cardsTab, !_showIdentity);
        StyleTab(_identityTab, _showIdentity);

        var stack = new VerticalStackLayout { Padding = new Thickness(16, 12, 16, 40), Spacing = 14 };
        stack.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { _cardsTab, _identityTab } });

        if (!_showIdentity)
        {
            if (data.PaymentCards.Count == 0) stack.Children.Add(Empty("No payment cards", "Tap New to add a card."));
            foreach (var card in data.PaymentCards.OrderBy(c => c.CardholderName, StringComparer.CurrentCultureIgnoreCase))
            {
                var face = CardFace(card);
                face.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new CardPage(card))) });
                stack.Children.Add(face);
            }
        }
        else
        {
            if (data.PersonalInfoRecords.Count == 0) stack.Children.Add(Empty("No personal info yet", "Store your name, phone numbers and addresses to fill forms in seconds. Tap New."));
            foreach (var info in data.PersonalInfoRecords)
            {
                var row = Ui.Card(new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14,
                    Children =
                    {
                        Ui.Avatar(info.Username, Ui.Primary, 48),
                        new VerticalStackLayout
                        {
                            Spacing = 2, VerticalOptions = LayoutOptions.Center,
                            Children =
                            {
                                Ui.Text(string.IsNullOrWhiteSpace(info.Username) ? "Personal details" : info.Username, 17, bold: true),
                                Ui.Text($"{info.PhoneNumbers.Count} phone number(s) · {info.Addresses.Count} address(es)", 13, sub: true)
                            }
                        }.Column(1)
                    }
                });
                row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushAsync(new PersonalInfoPage(info))) });
                stack.Children.Add(row);
            }
        }
        Body = new ScrollView { Content = stack };
    }

    private static void StyleTab(Button tab, bool selected)
    {
        if (selected) tab.Themed(VisualElement.BackgroundColorProperty, Ui.Primary, Ui.PrimaryDark).TextColor = Colors.White;
        else tab.Themed(VisualElement.BackgroundColorProperty, Ui.TintLight, Ui.TintDark).Themed(Button.TextColorProperty, Ui.Primary, Ui.PrimaryDark);
    }

    private static View Empty(string title, string detail) => new VerticalStackLayout
    {
        Padding = new Thickness(24, 50), Spacing = 6,
        Children = { Ui.Text(title, 17, bold: true).Center(), Ui.Text(detail, 14, sub: true).Center() }
    };

    public static Border CardFace(PaymentCard card)
    {
        var brand = CardNetworks.Resolve(card);
        var (start, end) = Ui.CardColors(brand);
        var white = Colors.White;
        return new Border
        {
            HeightRequest = 190, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Padding = new Thickness(22, 20),
            Background = new LinearGradientBrush(new GradientStopCollection { new(start, 0), new(end, 1) }, new Point(0, 0), new Point(1, 1)),
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
                Children =
                {
                    new Label { Text = CardNetworks.ShortName(brand), TextColor = white, FontSize = 18, FontAttributes = FontAttributes.Bold | FontAttributes.Italic, HorizontalOptions = LayoutOptions.End },
                    new Label { Text = $"••••  ••••  ••••  {Ui.LastDigits(card.CardNumber)}", TextColor = white, FontSize = 21, FontFamily = "monospace", VerticalOptions = LayoutOptions.Center }.Row(1),
                    new Grid
                    {
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                        Children =
                        {
                            new Label { Text = card.CardholderName.ToUpperInvariant(), TextColor = white.WithAlpha(0.9f), FontSize = 14, LineBreakMode = LineBreakMode.TailTruncation },
                            new Label { Text = card.ExpirationDate, TextColor = white.WithAlpha(0.9f), FontSize = 14 }.Column(1)
                        }
                    }.Row(2)
                }
            }
        };
    }
}

public sealed class CardPage : AppPage
{
    private PaymentCard _card;

    public CardPage(PaymentCard card) : base(CardNetworks.DisplayName(CardNetworks.Resolve(card)))
    {
        _card = card;
        ToolbarItems.Insert(0, new ToolbarItem("Edit", null, async () => await Navigation.PushAsync(new CardEditPage(_card))));
        ToolbarItems.Insert(1, new ToolbarItem("Delete", null, async () => await DeleteAsync()));
    }

    protected override void Refresh()
    {
        if (VaultSession.Data?.PaymentCards.FirstOrDefault(c => c.Id == _card.Id) is not { } card)
        {
            Dispatcher.Dispatch(async () => { if (Navigation.NavigationStack.LastOrDefault() == this) await Navigation.PopAsync(); });
            return;
        }
        _card = card;
        Title = CardNetworks.DisplayName(CardNetworks.Resolve(card));
        var rows = new List<View>();
        if (card.CardNumber.Length > 0) rows.Add(FieldRow.Create("Card number", card.CardNumber, secret: true));
        if (card.CardholderName.Length > 0) rows.Add(FieldRow.Create("Cardholder", card.CardholderName));
        if (card.ExpirationDate.Length > 0) rows.Add(FieldRow.Create("Expiry", card.ExpirationDate));
        if (card.SecurityCode.Length > 0) rows.Add(FieldRow.Create("Security code", card.SecurityCode, secret: true));
        var stack = new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 32), Spacing = 14, Children = { CardsPage.CardFace(card), FieldRow.Section(null, rows) } };
        if (!string.IsNullOrWhiteSpace(card.Notes))
            stack.Children.Add(Ui.Card(new VerticalStackLayout { Spacing = 8, Children = { Ui.Caption("Notes"), Ui.Text(card.Notes, 15) } }));
        Body = new ScrollView { Content = stack };
    }

    private async Task DeleteAsync()
    {
        if (!await ConfirmAsync("Delete this card?", $"\"{_card.CardholderName}\" will be permanently removed from your vault.", "Delete card")) return;
        VaultOperations.DeleteCard(VaultSession.Vault, _card);
        if (await SaveAsync()) await Navigation.PopAsync();
    }
}

public sealed class CardEditPage : AppPage
{
    private readonly PaymentCard? _original;
    private readonly Entry _holder, _number, _expiry, _cvv;
    private readonly Editor _notes;
    private readonly Picker _brand;
    private readonly Label _error = Forms.Error();

    public CardEditPage(PaymentCard? original) : base(original is null ? "New card" : "Edit card", lockButton: false)
    {
        _original = original;
        ToolbarItems.Add(new ToolbarItem("Save", null, async () => await SaveCardAsync()));
        _holder = Forms.Entry(original?.CardholderName ?? "", "Name on the card");
        _number = Forms.Entry(original?.CardNumber ?? "", "1234 5678 9012 3456", keyboard: Keyboard.Numeric);
        _expiry = Forms.Entry(original?.ExpirationDate ?? "", "MM/YY");
        _cvv = Forms.Entry(original?.SecurityCode ?? "", "CVV", password: true, keyboard: Keyboard.Numeric);
        _notes = Forms.Editor(original?.Notes ?? "", "Notes", 90);
        _brand = new Picker { FontSize = 16 }.Themed(Picker.TextColorProperty, Ui.TextLight, Ui.TextDark);
        _brand.Items.Add("Detect from number");
        foreach (var b in CardNetworks.Selectable) _brand.Items.Add(CardNetworks.DisplayName(b));
        var chosen = Enum.TryParse<CardBrand>(original?.Brand, true, out var parsed) ? parsed : CardBrand.Unknown;
        _brand.SelectedIndex = chosen == CardBrand.Unknown ? 0 : Array.IndexOf(CardNetworks.Selectable, chosen) + 1;
        var detected = Ui.Text("", 12, sub: true);
        void UpdateDetected() => detected.Text = "Detected: " + CardNetworks.DisplayName(CardNetworks.Detect(_number.Text ?? ""));
        _number.TextChanged += (_, _) => UpdateDetected();
        UpdateDetected();

        var save = Ui.PrimaryButton(original is null ? "Add card" : "Save changes");
        save.Clicked += async (_, _) => await SaveCardAsync();
        Body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 40), Spacing = 14,
                Children =
                {
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            Forms.Field("Cardholder name *", _holder), Forms.Field("Card number *", _number), detected,
                            new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10,
                                       Children = { Forms.Field("Expiry", _expiry), Forms.PasswordField("Security code", _cvv).Column(1) } },
                            Forms.Field("Card network", _brand), Forms.Field("Notes", _notes)
                        }
                    }),
                    _error, save
                }
            }
        };
    }

    private async Task SaveCardAsync()
    {
        if (string.IsNullOrWhiteSpace(_holder.Text) || string.IsNullOrWhiteSpace(_number.Text)) { _error.Show("Enter the cardholder name and card number."); return; }
        var brand = _brand.SelectedIndex <= 0 ? CardBrand.Unknown : CardNetworks.Selectable[_brand.SelectedIndex - 1];
        VaultOperations.SaveCard(VaultSession.Vault, new PaymentCard
        {
            CardholderName = _holder.Text.Trim(), CardNumber = _number.Text.Trim(), ExpirationDate = _expiry.Text?.Trim() ?? "",
            SecurityCode = _cvv.Text?.Trim() ?? "", Brand = brand == CardBrand.Unknown ? "" : brand.ToString(), Notes = _notes.Text?.Trim() ?? ""
        }, _original);
        if (await SaveAsync()) await Navigation.PopAsync();
    }
}

public sealed class PersonalInfoPage : AppPage
{
    private PersonalInfo _info;

    public PersonalInfoPage(PersonalInfo info) : base("Personal info")
    {
        _info = info;
        ToolbarItems.Insert(0, new ToolbarItem("Edit", null, async () => await Navigation.PushAsync(new PersonalInfoEditPage(_info))));
        ToolbarItems.Insert(1, new ToolbarItem("Delete", null, async () => await DeleteAsync()));
    }

    protected override void Refresh()
    {
        if (VaultSession.Data?.PersonalInfoRecords.FirstOrDefault(p => p.Id == _info.Id) is not { } info)
        {
            Dispatcher.Dispatch(async () => { if (Navigation.NavigationStack.LastOrDefault() == this) await Navigation.PopAsync(); });
            return;
        }
        _info = info;
        var rows = new List<View> { FieldRow.Create("Name", info.Username) };
        rows.AddRange(info.PhoneNumbers.Select(p => FieldRow.Create("Phone", p)));
        rows.AddRange(info.Addresses.Select(a => FieldRow.Create("Address", a, multiLine: true)));
        Body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 32), Spacing = 14,
                Children =
                {
                    FieldRow.Section(null, rows),
                    Ui.Text(info.ModifiedAt == default ? "" : $"Last updated {info.ModifiedAt.LocalDateTime:d MMM yyyy, HH:mm}", 12, sub: true).Center()
                }
            }
        };
    }

    private async Task DeleteAsync()
    {
        if (!await ConfirmAsync("Delete personal info?", "These details will be permanently removed from your vault.", "Delete")) return;
        VaultOperations.DeletePersonalInfo(VaultSession.Vault, _info);
        if (await SaveAsync()) await Navigation.PopAsync();
    }
}

public sealed class PersonalInfoEditPage : AppPage
{
    private readonly PersonalInfo? _original;
    private readonly Entry _name;
    private readonly Editor _phones, _addresses;
    private readonly Label _error = Forms.Error();

    public PersonalInfoEditPage(PersonalInfo? original) : base(original is null ? "New personal info" : "Edit personal info", lockButton: false)
    {
        _original = original;
        ToolbarItems.Add(new ToolbarItem("Save", null, async () => await SaveInfoAsync()));
        _name = Forms.Entry(original?.Username ?? "", "e.g. Jane Doe");
        _phones = Forms.Editor(string.Join("\n", original?.PhoneNumbers ?? new List<string>()), "+1 555 0100", 90);
        _addresses = Forms.Editor(string.Join("\n", original?.Addresses ?? new List<string>()), "221B Baker Street, London", 110);
        var save = Ui.PrimaryButton("Save");
        save.Clicked += async (_, _) => await SaveInfoAsync();
        Body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 40), Spacing = 14,
                Children =
                {
                    Ui.Card(new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            Forms.Field("Name *", _name),
                            Forms.Field("Phone numbers", _phones, hint: "One per line."),
                            Forms.Field("Addresses", _addresses, hint: "One per line.")
                        }
                    }),
                    _error, save
                }
            }
        };
    }

    private async Task SaveInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) { _error.Show("Enter a name."); return; }
        var now = DateTimeOffset.UtcNow;
        var info = _original ?? new PersonalInfo { CreatedAt = now };
        info.Username = _name.Text.Trim();
        info.PhoneNumbers = VaultOperations.SplitLines(_phones.Text ?? "");
        info.Addresses = VaultOperations.SplitLines(_addresses.Text ?? "");
        info.ModifiedAt = now;
        if (_original is null) VaultSession.Vault.PersonalInfoRecords.Add(info);
        if (await SaveAsync()) await Navigation.PopAsync();
    }
}
