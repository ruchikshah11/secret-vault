using SecretVault.Crypto;

namespace SecretVault.Gui;

public enum ThemeMode { Dark, Light }

public class ThemePalette
{
    public Color Shell = Color.Black;
    public Color Sidebar = Color.Black;
    public Color CardBg = Color.Black;
    public Color Field = Color.Black;
    public Color FieldBorder = Color.Black;
    public Color Border = Color.Black;
    public Color Text = Color.Black;
    public Color SubText = Color.Black;
    public Color Accent = Color.Black;
    public Color Gold = Color.Black;
    public Color GoldHover = Color.Black;
    // Primary action colour: a left-to-right gradient Primary -> PrimaryEnd with white text.
    public Color Primary = Color.Black;
    public Color PrimaryEnd = Color.Black;
    public Color Danger = Color.Black;
    public Color Selection = Color.Black;
    public Color Hover = Color.Black;

    public Color StrengthVeryWeak = Color.Black;
    public Color StrengthWeak = Color.Black;
    public Color StrengthFair = Color.Black;
    public Color StrengthStrong = Color.Black;
    public Color StrengthVeryStrong = Color.Black;
}

public static class AppTheme
{
    public const string RoleShell = "role:shell";
    public const string RoleSidebar = "role:sidebar";
    public const string RoleCard = "role:card";
    public const string NoTheme = "no-theme";

    public static readonly ThemePalette Dark = new()
    {
        Shell = Color.FromArgb(20, 22, 27),
        Sidebar = Color.FromArgb(15, 17, 21),
        CardBg = Color.FromArgb(28, 31, 38),
        Field = Color.FromArgb(38, 41, 50),
        FieldBorder = Color.FromArgb(58, 62, 72),
        Border = Color.FromArgb(46, 49, 58),
        Text = Color.FromArgb(235, 236, 240),
        SubText = Color.FromArgb(146, 152, 165),
        Accent = Color.FromArgb(66, 148, 255),
        Gold = Color.FromArgb(245, 166, 35),
        GoldHover = Color.FromArgb(255, 185, 70),
        Primary = Color.FromArgb(59, 130, 246),
        PrimaryEnd = Color.FromArgb(99, 102, 241),
        Danger = Color.FromArgb(235, 90, 90),
        Selection = Color.FromArgb(40, 55, 80),
        Hover = Color.FromArgb(36, 39, 47),
        StrengthVeryWeak = Color.FromArgb(220, 80, 80),
        StrengthWeak = Color.FromArgb(230, 140, 60),
        StrengthFair = Color.FromArgb(230, 200, 60),
        StrengthStrong = Color.FromArgb(120, 200, 90),
        StrengthVeryStrong = Color.FromArgb(70, 200, 100),
    };

    public static readonly ThemePalette Light = new()
    {
        Shell = Color.FromArgb(255, 255, 255),
        Sidebar = Color.FromArgb(236, 238, 242),
        CardBg = Color.FromArgb(255, 255, 255),
        Field = Color.FromArgb(255, 255, 255),
        FieldBorder = Color.FromArgb(210, 213, 219),
        Border = Color.FromArgb(224, 226, 230),
        Text = Color.FromArgb(30, 32, 38),
        SubText = Color.FromArgb(110, 116, 128),
        Accent = Color.FromArgb(30, 110, 230),
        Gold = Color.FromArgb(235, 155, 25),
        GoldHover = Color.FromArgb(250, 175, 50),
        Primary = Color.FromArgb(37, 99, 235),
        PrimaryEnd = Color.FromArgb(79, 70, 229),
        Danger = Color.FromArgb(200, 55, 55),
        Selection = Color.FromArgb(224, 235, 252),
        Hover = Color.FromArgb(240, 241, 244),
        StrengthVeryWeak = Color.FromArgb(200, 60, 60),
        StrengthWeak = Color.FromArgb(210, 120, 40),
        StrengthFair = Color.FromArgb(200, 170, 30),
        StrengthStrong = Color.FromArgb(80, 160, 70),
        StrengthVeryStrong = Color.FromArgb(40, 160, 80),
    };

    // Fixed nav-rail colors: Keeper's rail stays a constant dark navy regardless of the
    // light/dark content theme, so these are NOT part of ThemePalette.
    public static readonly Color RailBg = Color.FromArgb(31, 71, 136);
    public static readonly Color RailSelection = Color.FromArgb(54, 98, 168);
    public static readonly Color RailIconActive = Color.White;
    public static readonly Color RailIconInactive = Color.FromArgb(163, 186, 219);
    public static readonly Color RailTextActive = Color.White;
    public static readonly Color RailTextInactive = Color.FromArgb(163, 186, 219);

    public static ThemeMode Mode { get; private set; } = ThemeMode.Light;
    public static ThemePalette Current { get; private set; } = Light;

    public static event Action? ThemeChanged;

    public static void SetMode(ThemeMode mode)
    {
        Mode = mode;
        Current = mode == ThemeMode.Dark ? Dark : Light;
        AppSettings.Current.ThemeMode = mode;
        AppSettings.Current.Save();
        ThemeChanged?.Invoke();
    }

    public static void Toggle() => SetMode(Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

    // Convenience passthroughs so call sites read as AppTheme.Text, AppTheme.Field, etc.
    public static Color Shell => Current.Shell;
    public static Color Sidebar => RailBg;
    public static Color CardBg => Current.CardBg;
    public static Color Field => Current.Field;
    public static Color FieldBorder => Current.FieldBorder;
    public static Color Border => Current.Border;
    public static Color Text => Current.Text;
    public static Color SubText => Current.SubText;
    public static Color Accent => Current.Accent;
    public static Color Gold => Current.Gold;
    public static Color GoldHover => Current.GoldHover;
    public static Color Primary => Current.Primary;
    public static Color PrimaryEnd => Current.PrimaryEnd;
    public static readonly Color OnPrimary = Color.White;
    public static readonly Color RailMarker = Color.FromArgb(147, 197, 253);

    // Fills a shape with the primary gradient (lightened on hover, faded when disabled).
    public static void FillPrimary(Graphics g, System.Drawing.Drawing2D.GraphicsPath path, RectangleF bounds, bool hovered, bool enabled, Color background)
    {
        var start = Current.Primary;
        var end = Current.PrimaryEnd;
        if (hovered) { start = Gui.UiKit.Blend(Color.White, start, 0.12f); end = Gui.UiKit.Blend(Color.White, end, 0.12f); }
        if (!enabled) { start = Gui.UiKit.Blend(start, background, 0.45f); end = Gui.UiKit.Blend(end, background, 0.45f); }
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(bounds, start, end, System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
        g.FillPath(brush, path);
    }
    public static Color Danger => Current.Danger;
    public static Color Selection => Current.Selection;
    public static Color Hover => Current.Hover;

    // Page background behind rounded cards: a soft grey in light mode so white cards stand out.
    public static Color Canvas => Mode == ThemeMode.Dark ? Current.Shell : Color.FromArgb(240, 245, 252);

    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            var tag = c.Tag as string;

            if (tag == NoTheme)
            {
                if (c.HasChildren) Apply(c);
                continue;
            }

            switch (tag)
            {
                case RoleShell:
                    c.BackColor = Current.Shell;
                    break;
                case RoleSidebar:
                    c.BackColor = RailBg;
                    break;
                case RoleCard:
                    c.BackColor = Current.CardBg;
                    break;
                default:
                    switch (c)
                    {
                        case Button b:
                            b.FlatStyle = FlatStyle.Flat;
                            b.FlatAppearance.BorderColor = Current.FieldBorder;
                            b.FlatAppearance.MouseOverBackColor = Current.Hover;
                            b.BackColor = Current.Field;
                            b.ForeColor = Current.Text;
                            b.Cursor = Cursors.Hand;
                            break;
                        case TextBox tb:
                            tb.BackColor = Current.Field;
                            tb.ForeColor = Current.Text;
                            tb.BorderStyle = BorderStyle.FixedSingle;
                            break;
                        case ListBox lb:
                            lb.BackColor = Current.Sidebar;
                            lb.ForeColor = Current.Text;
                            lb.BorderStyle = BorderStyle.None;
                            break;
                        case ListView lv:
                            lv.BackColor = Current.CardBg;
                            lv.ForeColor = Current.Text;
                            lv.BorderStyle = BorderStyle.None;
                            break;
                        case ComboBox cb:
                            cb.BackColor = Current.Field;
                            cb.ForeColor = Current.Text;
                            cb.FlatStyle = FlatStyle.Flat;
                            break;
                        case DateTimePicker dtp:
                            dtp.CalendarMonthBackground = Current.CardBg;
                            dtp.CalendarForeColor = Current.Text;
                            break;
                        case Label lbl:
                            lbl.ForeColor = Current.Text;
                            break;
                        case Panel or GroupBox or TableLayoutPanel or FlowLayoutPanel:
                            c.BackColor = Current.CardBg;
                            break;
                    }
                    break;
            }

            if (c.HasChildren)
                Apply(c);
        }
    }

    public static Color StrengthColor(StrengthLevel level) => level switch
    {
        StrengthLevel.VeryWeak => Current.StrengthVeryWeak,
        StrengthLevel.Weak => Current.StrengthWeak,
        StrengthLevel.Fair => Current.StrengthFair,
        StrengthLevel.Strong => Current.StrengthStrong,
        _ => Current.StrengthVeryStrong
    };
}
