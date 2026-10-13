using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SecretVault.Gui;

// App-styled context menus: rounded window, roomy items, icons, shortcut hints, soft accent hover, theme colours.
public static class ModernMenu
{
    // disposeOnClose: true for one-off menus (right-click); false for menus kept and shown repeatedly (dropdowns).
    public static ContextMenuStrip Create(bool showIcons = true, bool disposeOnClose = true)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new MenuRenderer(),
            ShowImageMargin = showIcons,
            Padding = new Padding(6, 6, 6, 6),
            Font = new Font("Segoe UI", 9.5f),
            BackColor = AppTheme.CardBg,
            ForeColor = AppTheme.Text,
            ImageScalingSize = new Size(16, 16)
        };
        // Rounded corners for the menu window itself.
        menu.Opened += (_, _) => menu.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, menu.Width + 1, menu.Height + 1, 14, 14));
        if (disposeOnClose) menu.Closed += (_, _) => menu.BeginInvoke(menu.Dispose);
        return menu;
    }

    // Adds an item with an optional icon, shortcut hint and "danger" (red) styling.
    public static ToolStripMenuItem AddItem(this ContextMenuStrip menu, string text, IconKind? icon, Action onClick,
        string? shortcut = null, bool danger = false, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text)
        {
            Padding = new Padding(4, 7, 8, 7),
            ShortcutKeyDisplayString = shortcut,
            Enabled = enabled,
            Tag = danger ? DangerTag : null
        };
        if (icon is { } kind) item.Image = IconBitmap(kind, danger ? AppTheme.Danger : AppTheme.SubText);
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
        return item;
    }

    public static void AddSeparator(this ContextMenuStrip menu) => menu.Items.Add(new ToolStripSeparator());

    private const string DangerTag = "danger";

    private static Bitmap IconBitmap(IconKind kind, Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Icons.Draw(g, kind, new Rectangle(1, 1, 14, 14), color, 1.6f, AppTheme.CardBg);
        return bmp;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) => RoundedEdges = false;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(AppTheme.CardBg);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedField.RoundRect(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1.5f, e.ToolStrip.Height - 1.5f), 7);
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { } // no grey icon column

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var danger = (string?)e.Item.Tag == DangerTag;
            var rect = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var path = RoundedField.RoundRect(rect, 6);
            using var b = new SolidBrush(UiKit.Tint(danger ? AppTheme.Danger : AppTheme.Accent, AppTheme.CardBg));
            e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var danger = (string?)e.Item.Tag == DangerTag;
            var isShortcut = e.Text == (e.Item as ToolStripMenuItem)?.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled ? UiKit.Blend(AppTheme.SubText, AppTheme.CardBg, 0.6f)
                : danger ? AppTheme.Danger
                : isShortcut ? AppTheme.SubText
                : AppTheme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = AppTheme.SubText;
            base.OnRenderArrow(e);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => AppTheme.CardBg;
        public override Color ImageMarginGradientBegin => AppTheme.CardBg;
        public override Color ImageMarginGradientMiddle => AppTheme.CardBg;
        public override Color ImageMarginGradientEnd => AppTheme.CardBg;
        public override Color MenuBorder => AppTheme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => UiKit.Tint(AppTheme.Accent, AppTheme.CardBg);
        public override Color SeparatorDark => AppTheme.Border;
        public override Color SeparatorLight => AppTheme.CardBg;
    }
}
