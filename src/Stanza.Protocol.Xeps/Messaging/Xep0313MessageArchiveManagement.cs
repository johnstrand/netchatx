using System.Collections.Concurrent;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Messaging;

public sealed class MamMessageItem
{
    public required string ArchiveId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required MessageStanza Message { get; init; }
}

public sealed class MamQueryResult
{
    public required IReadOnlyList<MamMessageItem> Messages { get; init; }
    public bool IsComplete { get; init; }
    public string? FirstId { get; init; }
    public string? LastId { get; init; }
    public int? Count { get; init; }
}

public class Xep0313MessageArchiveManagement : XepFeatureBase
{
    public const string NsMam = "urn:xmpp:mam:2";
    public const string NsForward = "urn:xmpp:forward:0";
    public const string NsDelay = "urn:xmpp:delay";
    public const string NsRsm = "http://jabber.org/protocol/rsm";

    public override string Name => "XEP-0313: Message Archive Management";
    public override string FeatureUri => NsMam;

    private readonly ConcurrentDictionary<string, List<MamMessageItem>> _activeQueries = new();

    public virtual async Task<MamQueryResult> QueryArchiveAsync(
        Jid? withJid = null,
        Jid? archiveJid = null,
        int maxResults = 50,
        string? before = null,
        string? after = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        if (Client.State == XmppClientState.Disconnected || Client.State == XmppClientState.Disconnecting)
            throw new InvalidOperationException("Client is disconnected.");

        var queryId = Guid.NewGuid().ToString("N");
        var items = new List<MamMessageItem>();
        _activeQueries[queryId] = items;

        try
        {
            var iq = IqStanza.CreateSet();
            if (archiveJid is not null)
            {
                iq.To = archiveJid;
            }

            var queryElem = new XmppElement("query", NsMam).Attr("queryid", queryId);

            var form = new XmppElement("x", "jabber:x:data").Attr("type", "submit");
            form.Child(new XmppElement("field").Attr("var", "FORM_TYPE").Child(new XmppElement("value") { Value = NsMam }));

            if (withJid is not null && archiveJid is null)
            {
                form.Child(new XmppElement("field").Attr("var", "with").Child(new XmppElement("value") { Value = withJid.BareJid.ToString() }));
            }
            if (start.HasValue)
            {
                form.Child(new XmppElement("field").Attr("var", "start").Child(new XmppElement("value") { Value = start.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") }));
            }
            if (end.HasValue)
            {
                form.Child(new XmppElement("field").Attr("var", "end").Child(new XmppElement("value") { Value = end.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") }));
            }

            queryElem.Child(form);

            // RSM
            var rsm = new XmppElement("set", NsRsm);
            rsm.Child(new XmppElement("max") { Value = maxResults.ToString() });
            if (before is not null)
            {
                rsm.Child(new XmppElement("before") { Value = string.IsNullOrEmpty(before) ? null : before });
            }
            else if (after is null && !start.HasValue)
            {
                // Default to most recent page (or page ending at 'end') per XEP-0313 Section 4.1.4
                rsm.Child(new XmppElement("before"));
            }

            if (after is not null)
            {
                rsm.Child(new XmppElement("after") { Value = after });
            }

            queryElem.Child(rsm);
            iq.RawElement.Child(queryElem);

            var resultIq = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
            var fin = resultIq.RawElement.Element("fin", NsMam) ?? resultIq.RawElement.Element("fin");

            var isComplete = fin?.GetAttr("complete") == "true";
            string? first = null;
            string? last = null;
            int? count = null;

            var resSet = fin?.Element("set", NsRsm) ?? fin?.Element("set");
            if (resSet is not null)
            {
                first = resSet.Element("first")?.Value;
                last = resSet.Element("last")?.Value;
                if (int.TryParse(resSet.Element("count")?.Value, out int c))
                    count = c;
            }

            first ??= items.FirstOrDefault()?.ArchiveId;
            last ??= items.LastOrDefault()?.ArchiveId;

            return new MamQueryResult
            {
                Messages = items,
                IsComplete = isComplete,
                FirstId = first,
                LastId = last,
                Count = count
            };
        }
        finally
        {
            _activeQueries.TryRemove(queryId, out _);
        }
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var resultElem = element.Element("result", NsMam) ?? element.Element("result");
            if (resultElem is not null)
            {
                var queryId = resultElem.GetAttr("queryid") ?? element.GetAttr("queryid");
                var archiveId = resultElem.GetAttr("id") ?? Guid.NewGuid().ToString("N");

                var forwarded = resultElem.Element("forwarded", NsForward) ?? resultElem.Element("forwarded");
                var innerMsgElem = forwarded?.Element("message");

                if (innerMsgElem is not null)
                {
                    var innerMsg = new MessageStanza(innerMsgElem);
                    var delayElem = forwarded?.Element("delay", NsDelay)
                                 ?? forwarded?.Element("delay")
                                 ?? forwarded?.Element("x", "jabber:x:delay")
                                 ?? forwarded?.Element("x")
                                 ?? innerMsgElem.Element("delay", NsDelay)
                                 ?? innerMsgElem.Element("delay")
                                 ?? innerMsgElem.Element("x", "jabber:x:delay");
                    var timestamp = DateTimeOffset.UtcNow;

                    if (delayElem?.GetAttr("stamp") is string stampStr &&
                        DateTimeOffset.TryParse(stampStr, out var parsedStamp))
                    {
                        timestamp = parsedStamp;
                    }

                    var item = new MamMessageItem
                    {
                        ArchiveId = archiveId,
                        Timestamp = timestamp,
                        Message = innerMsg
                    };

                    if (!string.IsNullOrEmpty(queryId) && _activeQueries.TryGetValue(queryId, out var list))
                    {
                        lock (list)
                        {
                            list.Add(item);
                        }
                    }
                    else if (_activeQueries.Count > 0)
                    {
                        var singleList = _activeQueries.Values.FirstOrDefault();
                        if (singleList is not null)
                        {
                            lock (singleList)
                            {
                                singleList.Add(item);
                            }
                        }
                    }
                }

                return ValueTask.FromResult(false); // Handled MAM result
            }
        }

        return ValueTask.FromResult(true);
    }
}
