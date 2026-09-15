using NetChatx.Gui.Services;

namespace NetChatx.Gui.Tests.Mocks;

public class MockStartupService : IStartupService
{
    public bool IsEnabled { get; set; }
    public int SetCallsCount { get; private set; }

    public bool IsStartupEnabled() => IsEnabled;

    public bool SetStartupEnabled(bool enable)
    {
        IsEnabled = enable;
        SetCallsCount++;
        return true;
    }
}
