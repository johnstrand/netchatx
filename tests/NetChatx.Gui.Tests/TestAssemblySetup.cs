using System.Runtime.CompilerServices;
using NetChatx.Gui.Services;

namespace NetChatx.Gui.Tests;

public static class TestAssemblySetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        NotificationService.EnableNativeNotifications = false;
    }
}
