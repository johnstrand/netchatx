using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Stanza.Core.Transport;
using Xunit;

namespace Stanza.Core.Tests;

public class TcpTlsTransportTests
{
    [Fact]
    public async Task UpgradeToTlsAsync_UntrustedCertificate_ThrowsAuthenticationException()
    {
        // Generate a self-signed certificate (untrusted by standard validation)
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = cert.Export(X509ContentType.Pfx);
        using var serverCert = X509CertificateLoader.LoadPkcs12(pfx, null);

        // Start a TCP listener on local loopback with dynamic port
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var clientSocket = await listener.AcceptTcpClientAsync();
                using var sslStream = new SslStream(clientSocket.GetStream(), false);
                await sslStream.AuthenticateAsServerAsync(serverCert, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
            }
            catch
            {
                // Expected server side handshake failure when client rejects cert
            }
        });

        try
        {
            await using var transport = new TcpTlsTransport();
            await transport.ConnectAsync("127.0.0.1", port);

            // Attempting TLS upgrade to untrusted self-signed certificate server must throw AuthenticationException
            await Assert.ThrowsAsync<AuthenticationException>(async () =>
            {
                await transport.UpgradeToTlsAsync("localhost");
            });
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }
}
