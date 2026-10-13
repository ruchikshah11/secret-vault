using Microsoft.Maui.Controls.Shapes;
using SecretVault.Crypto;

namespace SecretVault.Mobile;

// Password / passphrase generator (SecretVault.Core's PasswordGenerator, same options as the desktop app).
// Opened from a password field it offers "Use this password".
public sealed class GeneratorPage : AppPage
{
    private readonly Action<string>? _use;
    private readonly PasswordOptions _options = AppPrefs.Generator;
    private readonly Label _preview;
    private readonly Label _details = Ui.Text("", 13, sub: true);
    private readonly VerticalStackLayout _optionsHost = new() { Spacing = 12 };
    private string _current = "";

    public GeneratorPage(Action<string>? use = null) : base("Password generator", lockButton: false)
    {
        _use = use;
        _preview = new Label { FontFamily = "monospace", FontSize = 22, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.CharacterWrap, HorizontalTextAlignment = TextAlignment.Center }
            .Themed(Label.TextColorProperty, Ui.TextLight, Ui.TextDark);

        var again = Ui.SecondaryButton("Generate another");
        again.Clicked += (_, _) => Generate();
        var copy = Ui.SecondaryButton("Copy");
        copy.Clicked += (_, _) => SecureClipboard.Copy(_current, "Password");
        var useButton = Ui.PrimaryButton("Use this password");
        useButton.IsVisible = use is not null;
        useButton.Clicked += async (_, _) => { _use?.Invoke(_current); await Navigation.PopAsync(); };

        var mode = new Picker { FontSize = 16, ItemsSource = new[] { "Random characters", "Passphrase (words)" }, SelectedIndex = _options.Passphrase ? 1 : 0 }
            .Themed(Picker.TextColorProperty, Ui.TextLight, Ui.TextDark);
        mode.SelectedIndexChanged += (_, _) => { _options.Passphrase = mode.SelectedIndex == 1; BuildOptions(); Generate(); };

        Body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 40), Spacing = 14,
                Children =
                {
                    new Border
                    {
                        StrokeShape = new RoundRectangle { CornerRadius = 16 }, StrokeThickness = 1, Padding = new Thickness(16, 24),
                        Content = new VerticalStackLayout { Spacing = 10, Children = { _preview, _details.Center() } }
                    }.Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark).Themed(Border.StrokeProperty, Ui.BorderLight, Ui.BorderDark),
                    useButton,
                    new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10,
                               Children = { again, copy.Column(1) } },
                    Ui.Card(new VerticalStackLayout { Spacing = 12, Children = { Ui.Caption("Options"), Forms.Field("Type", mode), _optionsHost } })
                }
            }
        };
        BuildOptions();
        Generate();
    }

    private void BuildOptions()
    {
        _optionsHost.Children.Clear();
        if (_options.Passphrase)
        {
            _optionsHost.Children.Add(Slider("Words", 3, 10, _options.Words, v => _options.Words = v));
            var separator = Forms.Entry(_options.Separator, "-");
            separator.MaxLength = 3;
            separator.TextChanged += (_, _) => { _options.Separator = separator.Text ?? ""; Generate(); };
            _optionsHost.Children.Add(Forms.Field("Separator", separator));
            _optionsHost.Children.Add(Toggle("Capitalise words", _options.Capitalize, v => _options.Capitalize = v));
            _optionsHost.Children.Add(Toggle("Add a number", _options.IncludeNumber, v => _options.IncludeNumber = v));
        }
        else
        {
            _optionsHost.Children.Add(Slider("Length", 8, 64, _options.Length, v => _options.Length = v));
            _optionsHost.Children.Add(Toggle("Uppercase (A–Z)", _options.Uppercase, v => _options.Uppercase = v));
            _optionsHost.Children.Add(Toggle("Lowercase (a–z)", _options.Lowercase, v => _options.Lowercase = v));
            _optionsHost.Children.Add(Toggle("Digits (2–9)", _options.Digits, v => _options.Digits = v));
            _optionsHost.Children.Add(Toggle("Symbols (!@#…)", _options.Symbols, v => _options.Symbols = v));
        }
    }

    private View Slider(string label, int min, int max, int value, Action<int> set)
    {
        var title = Ui.Text($"{label}: {value}", 15, bold: true);
        var slider = new Slider(min, max, value) { MinimumTrackColor = Ui.Primary, ThumbColor = Ui.Primary };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            title.Text = $"{label}: {v}";
            set(v);
            Generate();
        };
        return new VerticalStackLayout { Spacing = 2, Children = { title, slider } };
    }

    private View Toggle(string label, bool value, Action<bool> set)
    {
        var toggle = new Switch { IsToggled = value }.Themed(Switch.OnColorProperty, Ui.Primary, Ui.PrimaryDark);
        toggle.Toggled += (_, e) =>
        {
            set(e.Value);
            // At least one character set must stay on.
            if (!_options.Passphrase && !(_options.Uppercase || _options.Lowercase || _options.Digits || _options.Symbols))
            {
                set(true);
                toggle.IsToggled = true;
                return;
            }
            Generate();
        };
        return new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { Ui.Text(label, 15).WithCenter(), toggle.Column(1) }
        };
    }

    private void Generate()
    {
        _current = PasswordGenerator.Generate(_options);
        _preview.Text = _current;
        var level = PasswordStrength.Evaluate(_current);
        _details.Text = $"{Forms.StrengthName(level)} · about {PasswordGenerator.EstimateEntropyBits(_options):0} bits";
        _details.TextColor = Forms.StrengthColor(level);
        AppPrefs.Generator = _options;
    }
}
