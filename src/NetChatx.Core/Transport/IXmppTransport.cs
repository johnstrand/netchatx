using System.IO.Pipelines;

namespace NetChatx.Core.Transport;

public interface IXmppTransport : IAsyncDisposable
{
    PipeReader Input { get; }
    PipeWriter Output { get; }
    bool IsSecure { get; }

    ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
    ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default);
    ValueTask CloseAsync();
}
