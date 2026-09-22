using System;
using System.Threading.Tasks;

namespace NetChatx.Gui.Services;

public interface ISystemResumeWatcher : IDisposable
{
    event Func<Task>? Resumed;
    void TriggerResume();
}
