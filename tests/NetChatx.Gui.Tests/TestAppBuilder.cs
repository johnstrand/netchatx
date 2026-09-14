using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(NetChatx.Gui.Tests.TestAppBuilder))]

namespace NetChatx.Gui.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
