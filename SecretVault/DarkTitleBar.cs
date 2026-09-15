using System.Runtime.InteropServices;

namespace SecretVault.Gui;

public static class DarkTitleBar
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19;

    public static void Apply(IntPtr handle, bool dark)
    {
        var value = dark ? 1 : 0;
        var result = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        if (result != 0)
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeOld, ref value, sizeof(int));
    }
}
