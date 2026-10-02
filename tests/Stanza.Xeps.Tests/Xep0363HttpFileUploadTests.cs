using System.Net;
using System.Net.Http;
using System.Text;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Sharing;
using Xunit;

namespace Stanza.Xeps.Tests;

public class Xep0363HttpFileUploadTests
{
    private static readonly IReadOnlyList<TimeSpan> FastDelays =
    [
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMilliseconds(2),
        TimeSpan.FromMilliseconds(3),
    ];

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public List<string> ReceivedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                ReceivedBodies.Add(Encoding.UTF8.GetString(contentBytes));
            }
            return handler(request);
        }
    }

    private static async Task<(XmppClient client, MockXmppServer server)> SetupClientAndServerAsync()
    {
        var transport = new LoopbackTransport();
        var server = new MockXmppServer(transport);
        server.Start();

        server.OnIqReceived += async iq =>
        {
            var req = iq.RawElement.Element("request", Xep0363HttpFileUpload.NsHttpUpload);
            if (req is not null)
            {
                var slot = new XmppElement("slot", Xep0363HttpFileUpload.NsHttpUpload)
                    .Child(new XmppElement("put").Attr("url", "https://upload.example.com/put/file123")
                        .Child(new XmppElement("header") { Value = "Bearer test-token" }.Attr("name", "Authorization")))
                    .Child(new XmppElement("get").Attr("url", "https://upload.example.com/get/file123"));
                var result = iq.CreateResult(slot);
                await server.InjectStanzaAsync(result);
            }
        };

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.example.com"),
            Password = "password123"
        };
        var client = new XmppClient(options, transport);
        await client.ConnectAsync();

        return (client, server);
    }

    [Fact]
    public async Task UploadBytesAsync_SucceedsOnFirstAttempt_NoRetries()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "test image bytes"u8.ToArray();
        var progressReports = new List<double>();
        var progress = new Progress<double>(p => progressReports.Add(p));

        var getUrl = await uploader.UploadBytesAsync(
            Jid.Parse("upload.mock.example.com"),
            data,
            "test.png",
            "image/png",
            progress);

        Assert.Equal("https://upload.example.com/get/file123", getUrl);
        Assert.Equal(1, handler.CallCount);
        Assert.Single(handler.ReceivedBodies);
        Assert.Equal("test image bytes", handler.ReceivedBodies[0]);
    }

    [Fact]
    public async Task UploadBytesAsync_Transient500ThenSuccess_RetriesAndSucceeds()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var callCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
            };
        });

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "test payload"u8.ToArray();
        var getUrl = await uploader.UploadBytesAsync(
            Jid.Parse("upload.mock.example.com"),
            data,
            "test.txt",
            "text/plain");

        Assert.Equal("https://upload.example.com/get/file123", getUrl);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, handler.ReceivedBodies.Count);
        Assert.Equal("test payload", handler.ReceivedBodies[0]);
        Assert.Equal("test payload", handler.ReceivedBodies[1]);
    }

    [Fact]
    public async Task UploadBytesAsync_TransientHttpRequestExceptionThenSuccess_RetriesAndSucceeds()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var callCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new HttpRequestException("Network failure simulation");
            }
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "retry payload"u8.ToArray();
        var getUrl = await uploader.UploadBytesAsync(
            Jid.Parse("upload.mock.example.com"),
            data,
            "retry.txt",
            "text/plain");

        Assert.Equal("https://upload.example.com/get/file123", getUrl);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task UploadBytesAsync_TransientTimeoutThenSuccess_RetriesAndSucceeds()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var callCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new TimeoutException("HTTP request timeout simulation");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "timeout payload"u8.ToArray();
        var getUrl = await uploader.UploadBytesAsync(
            Jid.Parse("upload.mock.example.com"),
            data,
            "timeout.txt",
            "text/plain");

        Assert.Equal("https://upload.example.com/get/file123", getUrl);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task UploadBytesAsync_NonTransient400BadRequest_FailsImmediatelyWithoutRetry()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "bad request payload"u8.ToArray();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            uploader.UploadBytesAsync(
                Jid.Parse("upload.mock.example.com"),
                data,
                "bad.txt",
                "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task UploadBytesAsync_AllRetriesExhausted_ThrowsHttpRequestException()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "persistent failure payload"u8.ToArray();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            uploader.UploadBytesAsync(
                Jid.Parse("upload.mock.example.com"),
                data,
                "fail.txt",
                "text/plain"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        // Initial attempt (0) + 3 retries = 4 total calls
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task UploadFileAsync_TransientFailure_RecreatesStreamAndSucceeds()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "File content to upload with retry");

            var callCount = 0;
            var handler = new MockHttpMessageHandler(_ =>
            {
                callCount++;
                return callCount switch
                {
                    1 => new HttpResponseMessage(HttpStatusCode.BadGateway),
                    _ => new HttpResponseMessage(HttpStatusCode.OK)
                };
            });

            using var httpClient = new HttpClient(handler);
            var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
            await uploader.AttachAsync(client);

            var getUrl = await uploader.UploadFileAsync(
                Jid.Parse("upload.mock.example.com"),
                tempFile,
                "text/plain");

            Assert.Equal("https://upload.example.com/get/file123", getUrl);
            Assert.Equal(2, handler.CallCount);
            Assert.Equal(2, handler.ReceivedBodies.Count);
            Assert.Equal("File content to upload with retry", handler.ReceivedBodies[0]);
            Assert.Equal("File content to upload with retry", handler.ReceivedBodies[1]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task UploadBytesAsync_CancellationRequested_CancelsImmediatelyWithoutRetry()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        using var cts = new CancellationTokenSource();
        var handler = new MockHttpMessageHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "cancelled payload"u8.ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            uploader.UploadBytesAsync(
                Jid.Parse("upload.mock.example.com"),
                data,
                "cancel.txt",
                "text/plain",
                ct: cts.Token));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void DefaultRetryDelays_HasExpectedValues()
    {
        Assert.Equal(3, Xep0363HttpFileUpload.DefaultRetryDelays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(500), Xep0363HttpFileUpload.DefaultRetryDelays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), Xep0363HttpFileUpload.DefaultRetryDelays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), Xep0363HttpFileUpload.DefaultRetryDelays[2]);

        var uploader = new Xep0363HttpFileUpload();
        Assert.Equal(3, uploader.RetryDelays.Count);
    }

    [Fact]
    public async Task UploadBytesAsync_NonTransient404NotFound_FailsImmediatelyWithoutRetry()
    {
        var (client, server) = await SetupClientAndServerAsync();
        await using var _ = server;
        await using var __ = client;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler);
        var uploader = new Xep0363HttpFileUpload(httpClient, FastDelays);
        await uploader.AttachAsync(client);

        var data = "not found payload"u8.ToArray();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            uploader.UploadBytesAsync(
                Jid.Parse("upload.mock.example.com"),
                data,
                "notfound.txt",
                "text/plain"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }
}
