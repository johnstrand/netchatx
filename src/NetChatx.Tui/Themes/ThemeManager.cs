using Terminal.Gui.Drawing;

namespace NetChatx.Tui.Themes;

public enum TuiTheme
{
    Catppuccin,
    Gruvbox,
    Nord,
    Classic
}

public sealed class ThemeManager
{
    public TuiTheme CurrentTheme { get; private set; } = TuiTheme.Catppuccin;

    public void ApplyTheme(TuiTheme theme)
    {
        CurrentTheme = theme;
    }

    public static Scheme GetColorScheme(TuiTheme theme)
    {
        return theme switch
        {
            TuiTheme.Catppuccin => new Scheme
            {
                Normal = new Terminal.Gui.Drawing.Attribute(new Color(205, 214, 244), new Color(30, 30, 46)),
                Focus = new Terminal.Gui.Drawing.Attribute(new Color(137, 180, 250), new Color(49, 50, 68)),
                HotNormal = new Terminal.Gui.Drawing.Attribute(new Color(243, 139, 168), new Color(30, 30, 46)),
                HotFocus = new Terminal.Gui.Drawing.Attribute(new Color(243, 139, 168), new Color(49, 50, 68)),
                Disabled = new Terminal.Gui.Drawing.Attribute(new Color(108, 112, 134), new Color(30, 30, 46))
            },
            TuiTheme.Gruvbox => new Scheme
            {
                Normal = new Terminal.Gui.Drawing.Attribute(new Color(235, 219, 178), new Color(40, 40, 40)),
                Focus = new Terminal.Gui.Drawing.Attribute(new Color(254, 128, 25), new Color(60, 56, 54)),
                HotNormal = new Terminal.Gui.Drawing.Attribute(new Color(204, 36, 29), new Color(40, 40, 40)),
                HotFocus = new Terminal.Gui.Drawing.Attribute(new Color(204, 36, 29), new Color(60, 56, 54)),
                Disabled = new Terminal.Gui.Drawing.Attribute(new Color(146, 131, 116), new Color(40, 40, 40))
            },
            TuiTheme.Nord => new Scheme
            {
                Normal = new Terminal.Gui.Drawing.Attribute(new Color(236, 239, 244), new Color(46, 52, 64)),
                Focus = new Terminal.Gui.Drawing.Attribute(new Color(136, 192, 208), new Color(59, 66, 82)),
                HotNormal = new Terminal.Gui.Drawing.Attribute(new Color(191, 97, 106), new Color(46, 52, 64)),
                HotFocus = new Terminal.Gui.Drawing.Attribute(new Color(191, 97, 106), new Color(59, 66, 82)),
                Disabled = new Terminal.Gui.Drawing.Attribute(new Color(76, 86, 106), new Color(46, 52, 64))
            },
            _ => new Scheme
            {
                Normal = new Terminal.Gui.Drawing.Attribute(Color.White, Color.Black),
                Focus = new Terminal.Gui.Drawing.Attribute(Color.BrightCyan, Color.DarkGray),
                HotNormal = new Terminal.Gui.Drawing.Attribute(Color.BrightYellow, Color.Black),
                HotFocus = new Terminal.Gui.Drawing.Attribute(Color.BrightYellow, Color.DarkGray),
                Disabled = new Terminal.Gui.Drawing.Attribute(Color.DarkGray, Color.Black)
            }
        };
    }
}
