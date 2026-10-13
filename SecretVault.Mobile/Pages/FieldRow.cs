namespace SecretVault.Mobile;

// One labelled value with "Copy" (and "Show" for secrets). Tapping the row also copies.
public static class FieldRow
{
    public const string Mask = "••••••••••";

    public static View Create(string label, string value, bool secret = false, string? copyName = null, bool multiLine = false, Button? extraButton = null)
    {
        var valueLabel = Ui.Text(secret ? Mask : value, 16);
        valueLabel.LineBreakMode = multiLine ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation;
        if (secret) valueLabel.FontFamily = "monospace";

        var buttons = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (extraButton is not null) buttons.Children.Add(extraButton);
        if (secret)
        {
            var show = Ui.PillButton("Show");
            show.Clicked += (_, _) =>
            {
                var hidden = valueLabel.Text == Mask;
                valueLabel.Text = hidden ? value : Mask;
                show.Text = hidden ? "Hide" : "Show";
            };
            buttons.Children.Add(show);
        }
        var copy = Ui.PillButton("Copy");
        copy.Clicked += (_, _) => SecureClipboard.Copy(value, copyName ?? label);
        buttons.Children.Add(copy);

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 10),
            Children =
            {
                new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { Ui.Text(label, 12, sub: true), valueLabel } },
                buttons.Column(1)
            }
        };
        grid.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SecureClipboard.Copy(value, copyName ?? label)) });
        return grid;
    }

    // Stacks rows in a card with thin dividers between them.
    public static Border Section(string? caption, IEnumerable<View> rows)
    {
        var stack = new VerticalStackLayout();
        if (caption is not null) stack.Children.Add(new ContentView { Content = Ui.Caption(caption), Padding = new Thickness(0, 2, 0, 4) });
        var first = true;
        foreach (var row in rows)
        {
            if (!first) stack.Children.Add(new BoxView { HeightRequest = 1 }.Themed(BoxView.ColorProperty, Ui.BorderLight, Ui.BorderDark));
            stack.Children.Add(row);
            first = false;
        }
        return Ui.Card(stack, padding: 16);
    }
}
