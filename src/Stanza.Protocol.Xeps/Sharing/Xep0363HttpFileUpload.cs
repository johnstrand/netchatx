using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Sharing;

public sealed class HttpUploadSlot
{
    public required string PutUrl { get; init; }
    public required string GetUrl { get; init; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Xep0363HttpFileUpload : XepFeatureBase
{
    public const string NsHttpUpload = "urn:xmpp:http:upload:0";

    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
        TimeSpan.FromMilliseconds(2000),
    ];

    public override string Name => "XEP-0363: HTTP File Upload";
    public override string FeatureUri => NsHttpUpload;

    public IReadOnlyList<TimeSpan> RetryDelays => _retryDelays;

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public Xep0363HttpFileUpload(HttpClient? httpClient = null, IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _retryDelays = retryDelays ?? DefaultRetryDelays;
    }

    public async Task<HttpUploadSlot> RequestSlotAsync(
        Jid uploadServiceJid,
        string filename,
        long fileSize,
        string contentType = "application/octet-stream",
        CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateGet(uploadServiceJid);
        var req = new XmppElement("request", NsHttpUpload)
            .Attr("filename", filename)
            .Attr("size", fileSize.ToString())
            .Attr("content-type", contentType);

        iq.RawElement.Child(req);

        var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (resultIq.IsError)
        {
            throw new InvalidOperationException($"Failed to request upload slot: {resultIq.ToXmlString()}");
        }

        var slotElem = resultIq.RawElement.Element("slot", NsHttpUpload);
        if (slotElem is null)
        {
            throw new FormatException("Missing slot element in HTTP upload response.");
        }

        var putElem = slotElem.Element("put");
        var getElem = slotElem.Element("get");

        var putUrl = putElem?.GetAttr("url");
        var getUrl = getElem?.GetAttr("url");

        if (string.IsNullOrEmpty(putUrl) || string.IsNullOrEmpty(getUrl))
        {
            throw new FormatException("Slot element missing put or get URL.");
        }

        var slot = new HttpUploadSlot
        {
            PutUrl = putUrl,
            GetUrl = getUrl
        };

        if (putElem is not null)
        {
            foreach (var h in putElem.Elements("header"))
            {
                var name = h.GetAttr("name");
                var val = h.Value;
                if (!string.IsNullOrEmpty(name) && val is not null)
                {
                    slot.Headers[name] = val;
                }
            }
        }

        return slot;
    }

    public async Task<string> UploadFileAsync(
        Jid uploadServiceJid,
        string filePath,
        string? contentType = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File to upload not found.", filePath);

        contentType ??= "application/octet-stream";
        var slot = await RequestSlotAsync(uploadServiceJid, fileInfo.Name, fileInfo.Length, contentType, ct).ConfigureAwait(false);

        return await UploadWithRetryAsync(
            slot,
            () =>
            {
                var fileStream = fileInfo.OpenRead();
                var content = new StreamContent(fileStream);
                if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
                {
                    content.Headers.ContentType = mediaType;
                }
                else
                {
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
                return content;
            },
            progress,
            ct).ConfigureAwait(false);
    }

    public async Task<string> UploadBytesAsync(
        Jid uploadServiceJid,
        byte[] data,
        string filename,
        string contentType = "image/png",
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var slot = await RequestSlotAsync(uploadServiceJid, filename, data.Length, contentType, ct).ConfigureAwait(false);

        return await UploadWithRetryAsync(
            slot,
            () =>
            {
                var ms = new MemoryStream(data, writable: false);
                var content = new StreamContent(ms);
                if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
                {
                    content.Headers.ContentType = mediaType;
                }
                else
                {
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
                return content;
            },
            progress,
            ct).ConfigureAwait(false);
    }

    private async Task<string> UploadWithRetryAsync(
        HttpUploadSlot slot,
        Func<HttpContent> createContent,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(0.1);

            var isLastAttempt = attempt >= _retryDelays.Count;
            try
            {
                using var content = createContent();
                using var request = new HttpRequestMessage(HttpMethod.Put, slot.PutUrl)
                {
                    Content = content
                };

                foreach (var kv in slot.Headers)
                {
                    if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                    {
                        request.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    progress?.Report(1.0);
                    return slot.GetUrl;
                }

                if (isLastAttempt || !IsTransientStatusCode(response.StatusCode))
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex) when (!isLastAttempt && IsTransientException(ex, ct))
            {
                // Transient failure; proceed to delay and retry
            }

            var delay = _retryDelays[attempt];
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static bool IsTransientException(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return false;

        if (ex is FileNotFoundException or DirectoryNotFoundException)
            return false;

        if (ex is OperationCanceledException or TimeoutException)
            return true;

        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode is null || IsTransientStatusCode(httpEx.StatusCode.Value);
        }

        if (ex is IOException)
            return true;

        return false;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return (code >= 500 && code <= 599) || code == 408 || code == 429;
    }

    public async Task<Jid?> DiscoverUploadServiceAsync(Jid domainJid, CancellationToken ct = default)
    {
        if (Client is null) return null;

        try
        {
            // 1. Check if domain itself supports HTTP upload
            var infoIq = IqStanza.CreateGet(domainJid);
            infoIq.RawElement.Child(new XmppElement("query", "http://jabber.org/protocol/disco#info"));
            var infoResult = await Client.SendIqAsync(infoIq, cancellationToken: ct).ConfigureAwait(false);
            var queryInfo = infoResult.RawElement.Element("query", "http://jabber.org/protocol/disco#info");
            if (queryInfo is not null)
            {
                foreach (var f in queryInfo.Elements("feature"))
                {
                    if (f.GetAttr("var") == NsHttpUpload) return domainJid;
                }
            }

            // 2. Query disco#items of the domain
            var itemsIq = IqStanza.CreateGet(domainJid);
            itemsIq.RawElement.Child(new XmppElement("query", "http://jabber.org/protocol/disco#items"));
            var itemsResult = await Client.SendIqAsync(itemsIq, cancellationToken: ct).ConfigureAwait(false);
            var queryItems = itemsResult.RawElement.Element("query", "http://jabber.org/protocol/disco#items");
            if (queryItems is not null)
            {
                foreach (var item in queryItems.Elements("item"))
                {
                    var jidStr = item.GetAttr("jid");
                    if (!string.IsNullOrEmpty(jidStr) && Jid.TryParse(jidStr, out var itemJid))
                    {
                        var subInfoIq = IqStanza.CreateGet(itemJid);
                        subInfoIq.RawElement.Child(new XmppElement("query", "http://jabber.org/protocol/disco#info"));
                        var subInfoResult = await Client.SendIqAsync(subInfoIq, cancellationToken: ct).ConfigureAwait(false);
                        var subQueryInfo = subInfoResult.RawElement.Element("query", "http://jabber.org/protocol/disco#info");
                        if (subQueryInfo is not null)
                        {
                            foreach (var f in subQueryInfo.Elements("feature"))
                            {
                                if (f.GetAttr("var") == NsHttpUpload) return itemJid;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Soft failure during service discovery
        }

        return null;
    }
}
