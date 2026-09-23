using System;
using System.Threading.Tasks;

namespace Stanza.Gui.Services;

public interface ISystemResumeWatcher : IDisposable
{
    event Func<Task>? Resumed;
    void TriggerResume();
}
