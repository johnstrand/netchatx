using Terminal.Gui.App;
using NetChatx.Tui.Engine;
using NetChatx.Tui.Views;

namespace NetChatx.App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
        {
            Console.WriteLine("NetChatx v1.0.0 (.NET 10 XMPP TUI Client with OMEMO)");
            return 0;
        }

        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
        {
            Console.WriteLine("NetChatx - Terminal XMPP Client");
            Console.WriteLine("Usage: netchatx [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -v, --version    Show version information");
            Console.WriteLine("  -h, --help       Show help documentation");
            return 0;
        }

        try
        {
            Application.Init();

            var window = new MainChatWindow();
            await using var controller = new ChatAppController(window);
            await controller.InitializeAsync();

            Application.Run(window);
            Application.Shutdown();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal NetChatx Error: {ex.Message}");
            return 1;
        }
    }
}
