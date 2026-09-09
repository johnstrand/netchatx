using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Sharing;

public sealed class HttpUploadSlot
{
    public required string PutUrl { get; init; }
    public required string GetUrl { get; init; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Xep0363HttpFileUpload : XepFeatureBase
{
    public const string NsHttpUpload = "urn:xmpp:http:upload:0";

    public override string Name => "XEP-0363: HTTP File Upload";
    public override string FeatureUri => NsHttpUpload;

    private readonly HttpClient _httpClient = new();

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

        var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct);
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

        string? putUrl = putElem?.GetAttr("url");
        string? getUrl = getElem?.GetAttr("url");

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
                string? name = h.GetAttr("name");
                string? val = h.Value;
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
        var slot = await RequestSlotAsync(uploadServiceJid, fileInfo.Name, fileInfo.Length, contentType, ct);

        using var fileStream = fileInfo.OpenRead();
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Put, slot.PutUrl)
        {
            Content = content
        };

        foreach (var kv in slot.Headers)
        {
            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        progress?.Report(0.1);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        progress?.Report(1.0);

        return slot.GetUrl;
    }
}
