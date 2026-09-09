using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace NetChatx.Core.Transport;

public sealed class TcpTlsTransport : IXmppTransport
{
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private SslStream? _sslStream;
    private Stream? _activeStream;

    private PipeReader? _reader;
    private PipeWriter? _writer;

    public PipeReader Input => _reader ?? throw new InvalidOperationException("Transport is not connected.");
    public PipeWriter Output => _writer ?? throw new InvalidOperationException("Transport is not connected.");
    public bool IsSecure => _sslStream is not null;

    public async ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        _tcpClient = new TcpClient
        {
            NoDelay = true
        };

        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        _networkStream = _tcpClient.GetStream();
        _activeStream = _networkStream;

        _reader = PipeReader.Create(_activeStream);
        _writer = PipeWriter.Create(_activeStream);
    }

    public async ValueTask UpgradeToTlsAsync(string targetHost, CancellationToken cancellationToken = default)
    {
        if (_networkStream is null)
            throw new InvalidOperationException("Cannot upgrade to TLS before connecting.");

        _sslStream = new SslStream(_networkStream, false);

        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        await _sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken);
        _activeStream = _sslStream;

        _reader = PipeReader.Create(_activeStream);
        _writer = PipeWriter.Create(_activeStream);
    }

    public async ValueTask CloseAsync()
    {
        if (_writer is not null)
        {
            await _writer.CompleteAsync();
            _writer = null;
        }

        if (_reader is not null)
        {
            await _reader.CompleteAsync();
            _reader = null;
        }

        _sslStream?.Dispose();
        _sslStream = null;

        _networkStream?.Dispose();
        _networkStream = null;

        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }
}
