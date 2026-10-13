namespace SecretVault.Gui;

// The app icon. It is also set as <ApplicationIcon> (exe / Explorer / taskbar); the window copy is loaded
// from the embedded resource so it is correct regardless of which executable hosts the forms.
public static class AppIcon
{
    private static Icon? _icon;

    public static Icon? Value => _icon ??= Load();

    private static Icon? Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SecretVault.ico");
        return stream is null ? null : new Icon(stream);
    }
}
