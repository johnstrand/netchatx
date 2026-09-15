namespace NetChatx.Gui.Services;

public interface IStartupService
{
    bool IsStartupEnabled();
    bool SetStartupEnabled(bool enable);
}
