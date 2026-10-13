using Microsoft.Maui.Controls.Shapes;
using SecretVault.Crypto;

namespace SecretVault.Mobile;

// Form building blocks: labelled text fields, password fields with Show/Generate, strength bar, toast.
public static class Forms
{
    public static Entry Entry(string text = "", string placeholder = "", bool password = false, Keyboard? keyboard = null)
    {
        var entry = new Entry
        {
            Text = text, Placeholder = placeholder, IsPassword = password, FontSize = 16, Keyboard = keyboard ?? Keyboard.Default,
            IsSpellCheckEnabled = false, IsTextPredictionEnabled = !password && keyboard is null
        };
        return entry.Themed(Microsoft.Maui.Controls.Entry.TextColorProperty, Ui.TextLight, Ui.TextDark)
            .Themed(Microsoft.Maui.Controls.Entry.PlaceholderColorProperty, Ui.SubLight, Ui.SubDark);
    }

    public static Editor Editor(string text = "", string placeholder = "", double height = 110)
    {
        var editor = new Editor { Text = text, Placeholder = placeholder, FontSize = 16, HeightRequest = height, AutoSize = EditorAutoSizeOption.Disabled };
        return editor.Themed(Microsoft.Maui.Controls.Editor.TextColorProperty, Ui.TextLight, Ui.TextDark)
            .Themed(Microsoft.Maui.Controls.Editor.PlaceholderColorProperty, Ui.SubLight, Ui.SubDark);
    }

    // Rounded box around an input, with a small label above it.
    public static View Field(string label, View input, View? trailing = null, string? hint = null)
    {
        var box = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 6,
            Children = { input }
        };
        if (trailing is not null) box.Children.Add(trailing.Column(1));
        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 1, Padding = new Thickness(10, 2), Content = box
        }.Themed(VisualElement.BackgroundColorProperty, Ui.CardLight, Ui.CardDark).Themed(Border.StrokeProperty, Ui.BorderLight, Ui.BorderDark);

        var stack = new VerticalStackLayout { Spacing = 5, Children = { Ui.Text(label, 13, bold: true, sub: true), border } };
        if (hint is not null) stack.Children.Add(Ui.Text(hint, 12, sub: true));
        return stack;
    }

    // Password input with Show/Hide and (optionally) a Generate button that opens the generator.
    public static View PasswordField(string label, Entry entry, Page? owner = null, string? hint = null)
    {
        var buttons = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var show = Ui.PillButton("Show");
        show.Clicked += (_, _) => { entry.IsPassword = !entry.IsPassword; show.Text = entry.IsPassword ? "Show" : "Hide"; };
        buttons.Children.Add(show);
        if (owner is not null)
        {
            var generate = Ui.PillButton("Generate");
            generate.Clicked += async (_, _) => await owner.Navigation.PushAsync(new GeneratorPage(password =>
            {
                entry.Text = password;
                entry.IsPassword = false;
                show.Text = "Hide";
            }));
            buttons.Children.Add(generate);
        }
        return Field(label, entry, buttons, hint);
    }

    public static Color StrengthColor(StrengthLevel level) => level switch
    {
        StrengthLevel.VeryWeak => Color.FromRgb(200, 60, 60),
        StrengthLevel.Weak => Color.FromRgb(210, 120, 40),
        StrengthLevel.Fair => Color.FromRgb(200, 170, 30),
        StrengthLevel.Strong => Color.FromRgb(80, 160, 70),
        _ => Color.FromRgb(40, 160, 80)
    };

    public static string StrengthName(StrengthLevel level) => level switch
    {
        StrengthLevel.VeryWeak => "Very weak",
        StrengthLevel.VeryStrong => "Very strong",
        _ => level.ToString()
    };

    // Five-segment strength meter that follows an Entry.
    public static View StrengthBar(Entry entry)
    {
        var segments = Enumerable.Range(0, 5).Select(_ => new BoxView { HeightRequest = 5, CornerRadius = 3 }).ToList();
        var label = Ui.Text("", 12, bold: true);
        var bar = new Grid { ColumnSpacing = 5, Margin = new Thickness(2, 2, 2, 0) };
        for (var i = 0; i < 5; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            bar.Children.Add(segments[i].Column(i));
        }
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(84)));
        label.HorizontalTextAlignment = TextAlignment.End;
        bar.Children.Add(label.Column(5));

        void Update()
        {
            var text = entry.Text ?? "";
            var level = PasswordStrength.Evaluate(text);
            var filled = text.Length == 0 ? 0 : (int)level + 1;
            var color = StrengthColor(level);
            for (var i = 0; i < 5; i++)
                segments[i].Color = i < filled ? color : (Application.Current?.RequestedTheme == AppTheme.Dark ? Ui.BorderDark : Ui.BorderLight);
            label.Text = text.Length == 0 ? "" : StrengthName(level);
            label.TextColor = color;
        }
        entry.TextChanged += (_, _) => Update();
        Update();
        return bar;
    }

    public static Label Error()
    {
        var label = Ui.Text("", 14).Themed(Label.TextColorProperty, Ui.DangerLight, Ui.DangerDark);
        label.IsVisible = false;
        return label;
    }

    public static void Show(this Label label, string text)
    {
        label.Text = text;
        label.IsVisible = true;
    }
}

// Modal page asking for one password (backup password, shared copy's password…). Returns null when cancelled.
public sealed class PasswordPromptPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new();

    private PasswordPromptPage(string title, string message)
    {
        this.CanvasBackground();
        var entry = Forms.Entry(password: true, placeholder: "Password");
        var ok = Ui.PrimaryButton("Continue");
        var cancel = Ui.SecondaryButton("Cancel");
        ok.Clicked += async (_, _) => { _result.TrySetResult(entry.Text ?? ""); await Navigation.PopModalAsync(); };
        entry.Completed += async (_, _) => { _result.TrySetResult(entry.Text ?? ""); await Navigation.PopModalAsync(); };
        cancel.Clicked += async (_, _) => { _result.TrySetResult(null); await Navigation.PopModalAsync(); };
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 48), Spacing = 16,
                Children = { Ui.Text(title, 22, bold: true), Ui.Text(message, 15, sub: true), Forms.PasswordField("Password", entry), ok, cancel }
            }
        };
        Loaded += (_, _) => entry.Focus();
    }

    protected override bool OnBackButtonPressed()
    {
        _result.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    public static async Task<string?> AskAsync(Page owner, string title, string message)
    {
        var page = new PasswordPromptPage(title, message);
        await owner.Navigation.PushModalAsync(page);
        return await page._result.Task;
    }
}
