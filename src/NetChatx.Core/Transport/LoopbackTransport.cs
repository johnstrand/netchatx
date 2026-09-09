using System.IO.Pipelines;

namespace NetChatx.Core.Transport;

/// <summary>
/// In-memory loopback transport for ultra-fast, zero-network integration testing and mock server fixtures.
/// </summary>
public sealed class LoopbackTransport : IXmppTransport
{
    private readonly Pipe _clientToServerPipe;
    private readonly Pipe _serverToClientPipe;

    public PipeReader Input => _serverToClientPipe.Reader;
    public PipeWriter Output => _clientToServerPipe.Writer;
    public bool IsSecure { get; private set; }

    public PipeReader ServerInput => _clientToServerPipe.Reader;
    public PipeWriter ServerOutput => _serverToClientPipe.Writer;

    public LoopbackTransport()
    {
        _clientToServerPipe = new Pipe();
        _serverToClientPipe = new Pipe();
    }

    public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default)
    {
        IsSecure = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask CloseAsync()
    {
        await _clientToServerPipe.Writer.CompleteAsync();
        await _serverToClientPipe.Writer.CompleteAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }
}
