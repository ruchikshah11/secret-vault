using Microsoft.Maui.Controls.Shapes;
using SecretVault.Models;

namespace SecretVault.Mobile;

// Colours and small building blocks shared by every page. Same white & blue palette as the desktop app,
// with a dark variant that follows the phone's theme.
public static class Ui
{
    public static readonly Color Primary = Color.FromArgb("#2563EB");
    public static readonly Color PrimaryDark = Color.FromArgb("#4294FF");
    public static readonly Color Navy = Color.FromArgb("#1F4788");
    public static readonly Color Gold = Color.FromArgb("#F5A623");

    public static readonly Color CanvasLight = Color.FromArgb("#F4F6FA"), CanvasDark = Color.FromArgb("#111318");
    public static readonly Color CardLight = Colors.White, CardDark = Color.FromArgb("#1C1F26");
    public static readonly Color TextLight = Color.FromArgb("#1E2026"), TextDark = Color.FromArgb("#EBECF0");
    public static readonly Color SubLight = Color.FromArgb("#6E7480"), SubDark = Color.FromArgb("#9298A5");
    public static readonly Color BorderLight = Color.FromArgb("#E3E6EB"), BorderDark = Color.FromArgb("#2E313A");
    public static readonly Color DangerLight = Color.FromArgb("#C83737"), DangerDark = Color.FromArgb("#EB5A5A");
    public static readonly Color TintLight = Color.FromArgb("#E0EBFC"), TintDark = Color.FromArgb("#1E2C44");

    public static T Themed<T>(this T view, BindableProperty property, Color light, Color dark) where T : BindableObject
    {
        view.SetAppThemeColor(property, light, dark);
        return view;
    }

    public static T CanvasBackground<T>(this T view) where T : VisualElement =>
        view.Themed(VisualElement.BackgroundColorProperty, CanvasLight, CanvasDark);

    public static Label Text(string text, double size = 15, bool bold = false, bool sub = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
        return sub ? label.Themed(Label.TextColorProperty, SubLight, SubDark) : label.Themed(Label.TextColorProperty, TextLight, TextDark);
    }

    public static Label Caption(string text) => Text(text.ToUpperInvariant(), 12, bold: true, sub: true);

    public static Border Card(View content, double padding = 16) =>
        new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            StrokeThickness = 1,
            Padding = padding,
            Content = content
        }
        .Themed(VisualElement.BackgroundColorProperty, CardLight, CardDark)
        .Themed(Border.StrokeProperty, BorderLight, BorderDark);

    public static Button PrimaryButton(string text) =>
        new Button { Text = text, FontAttributes = FontAttributes.Bold, FontSize = 16, HeightRequest = 52, CornerRadius = 14, TextColor = Colors.White }
            .Themed(VisualElement.BackgroundColorProperty, Primary, PrimaryDark);

    public static Button SecondaryButton(string text) =>
        new Button { Text = text, FontSize = 15, HeightRequest = 48, CornerRadius = 14, BorderWidth = 1 }
            .Themed(VisualElement.BackgroundColorProperty, CardLight, CardDark)
            .Themed(Button.TextColorProperty, Primary, PrimaryDark)
            .Themed(Button.BorderColorProperty, BorderLight, BorderDark);

    // Small pill button used on field rows ("Copy", "Show").
    public static Button PillButton(string text) =>
        new Button { Text = text, FontSize = 13, FontAttributes = FontAttributes.Bold, HeightRequest = 36, MinimumWidthRequest = 64, Padding = new Thickness(14, 0), CornerRadius = 18 }
            .Themed(VisualElement.BackgroundColorProperty, TintLight, TintDark)
            .Themed(Button.TextColorProperty, Primary, PrimaryDark);

    public static Image Logo(double size) => new() { Source = "logo.png", WidthRequest = size, HeightRequest = size, HorizontalOptions = LayoutOptions.Center };

    // Coloured circle with the first letter of the name.
    public static Border Avatar(string name, Color color, double size = 44)
    {
        var initial = name.FirstOrDefault(char.IsLetterOrDigit);
        return new Border
        {
            WidthRequest = size, HeightRequest = size, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            BackgroundColor = color.WithAlpha(0.16f),
            Content = new Label
            {
                Text = initial == default ? "•" : char.ToUpperInvariant(initial).ToString(),
                TextColor = color, FontSize = size * 0.42, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
    }

    // Same colours as the desktop "Record Types" icons.
    public static Color TypeColor(string type) => type switch
    {
        RecordTypes.Server => Color.FromArgb("#0D9488"),
        RecordTypes.Database => Color.FromArgb("#7C3AED"),
        RecordTypes.AppCredentials => Color.FromArgb("#4F46E5"),
        RecordTypes.Certificate => Color.FromArgb("#D97706"),
        RecordTypes.SoftwareLicense => Color.FromArgb("#16A34A"),
        RecordTypes.SecureNote => Color.FromArgb("#475569"),
        _ => Primary
    };

    public static (Color Start, Color End) CardColors(CardBrand brand) => brand switch
    {
        CardBrand.Visa => (Color.FromRgb(20, 40, 120), Color.FromRgb(40, 100, 220)),
        CardBrand.Mastercard => (Color.FromRgb(28, 28, 36), Color.FromRgb(70, 70, 86)),
        CardBrand.Amex => (Color.FromRgb(0, 92, 150), Color.FromRgb(40, 160, 210)),
        CardBrand.RuPay => (Color.FromRgb(10, 60, 90), Color.FromRgb(20, 130, 120)),
        CardBrand.Discover => (Color.FromRgb(160, 70, 10), Color.FromRgb(245, 140, 40)),
        CardBrand.Jcb => (Color.FromRgb(10, 80, 60), Color.FromRgb(30, 150, 100)),
        CardBrand.Diners => (Color.FromRgb(40, 60, 90), Color.FromRgb(100, 120, 150)),
        CardBrand.Maestro => (Color.FromRgb(10, 50, 110), Color.FromRgb(30, 130, 200)),
        CardBrand.UnionPay => (Color.FromRgb(120, 20, 30), Color.FromRgb(200, 50, 60)),
        _ => (Color.FromRgb(70, 48, 140), Color.FromRgb(140, 90, 220))
    };

    public static string LastDigits(string number, int count = 4)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= count ? digits : digits[^count..];
    }

    public static string LeafFolder(string folder)
    {
        var parts = folder.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? folder : parts[^1];
    }
}
