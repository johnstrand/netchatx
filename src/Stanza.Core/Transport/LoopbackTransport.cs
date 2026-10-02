using System.IO.Pipelines;

namespace Stanza.Core.Transport;

/// <summary>
/// In-memory loopback transport for ultra-fast, zero-network integration testing and mock server fixtures.
/// </summary>
public sealed class LoopbackTransport : IXmppTransport
{
    private readonly Pipe _clientToServerPipe;
    private readonly Pipe _serverToClientPipe;
    private readonly bool _isSecureOnConnect;

    public PipeReader Input => _serverToClientPipe.Reader;
    public PipeWriter Output => _clientToServerPipe.Writer;
    public bool IsSecure { get; private set; }

    public PipeReader ServerInput => _clientToServerPipe.Reader;
    public PipeWriter ServerOutput => _serverToClientPipe.Writer;

    /// <param name="isSecureOnConnect">
    /// Whether the in-memory channel should be treated as secure after connecting. This defaults to true
    /// because the loopback channel never crosses an untrusted network. Set false to test plaintext flows.
    /// </param>
    public LoopbackTransport(bool isSecureOnConnect = true)
    {
        _isSecureOnConnect = isSecureOnConnect;
        _clientToServerPipe = new Pipe();
        _serverToClientPipe = new Pipe();
    }

    public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        IsSecure = _isSecureOnConnect;
        return ValueTask.CompletedTask;
    }

    public ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default)
    {
        IsSecure = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask CloseAsync()
    {
        await _clientToServerPipe.Writer.CompleteAsync().ConfigureAwait(false);
        await _serverToClientPipe.Writer.CompleteAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }
}
