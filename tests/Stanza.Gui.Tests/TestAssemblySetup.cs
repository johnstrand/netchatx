using System.Runtime.CompilerServices;
using Stanza.Gui.Services;

namespace Stanza.Gui.Tests;

public static class TestAssemblySetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        NotificationService.EnableNativeNotifications = false;
    }
}
