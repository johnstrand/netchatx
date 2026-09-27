using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Privacy;

public sealed class Xep0191Blocking : XepFeatureBase
{
    public const string NsBlocking = "urn:xmpp:blocking";
    public const string NsBlockingErrors = "urn:xmpp:blocking:errors";

    public override string Name => "XEP-0191: Blocking Command";
    public override string FeatureUri => NsBlocking;

    private readonly ConcurrentDictionary<string, byte> _blockedJids = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> BlockedJids => _blockedJids.Keys.ToList();

    public event Action<IReadOnlyList<string>>? BlockListUpdated;
    public event Action<IReadOnlyList<string>>? ContactsBlocked;
    public event Action<IReadOnlyList<string>>? ContactsUnblocked;
    public event Action? BlockListCleared;

    public bool IsBlocked(Jid jid)
    {
        var full = jid.ToString();
        if (_blockedJids.ContainsKey(full)) return true;
        if (_blockedJids.ContainsKey(jid.BareJid)) return true;
        if (!string.IsNullOrEmpty(jid.Domain) && _blockedJids.ContainsKey(jid.Domain)) return true;
        return false;
    }

    public bool IsBlocked(string jidOrDomain)
    {
        if (string.IsNullOrWhiteSpace(jidOrDomain)) return false;
        if (_blockedJids.ContainsKey(jidOrDomain)) return true;

        if (Jid.TryParse(jidOrDomain, out var jid))
        {
            return IsBlocked(jid);
        }
        return false;
    }

    public async Task<IReadOnlyList<string>> GetBlockListAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateGet();
        iq.RawElement.Child(new XmppElement("blocklist", NsBlocking));

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to get blocklist: {response.ToXmlString()}");
        }

        var blocklistElem = response.RawElement.Element("blocklist", NsBlocking);
        var items = new List<string>();
        if (blocklistElem is not null)
        {
            foreach (var item in blocklistElem.Elements("item"))
            {
                var jid = item.GetAttr("jid");
                if (!string.IsNullOrWhiteSpace(jid))
                {
                    items.Add(jid);
                }
            }
        }

        _blockedJids.Clear();
        foreach (var j in items)
        {
            _blockedJids.TryAdd(j, 0);
        }

        BlockListUpdated?.Invoke(items);
        return items;
    }

    public async Task BlockAsync(IEnumerable<string> jids, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var list = jids.Where(j => !string.IsNullOrWhiteSpace(j)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return;

        var iq = IqStanza.CreateSet();
        var blockElem = new XmppElement("block", NsBlocking);
        foreach (var jid in list)
        {
            var item = new XmppElement("item");
            item.Attr("jid", jid);
            blockElem.Child(item);
        }
        iq.RawElement.Child(blockElem);

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to block contacts: {response.ToXmlString()}");
        }

        foreach (var jid in list)
        {
            _blockedJids.TryAdd(jid, 0);
        }

        ContactsBlocked?.Invoke(list);
        BlockListUpdated?.Invoke(_blockedJids.Keys.ToList());
    }

    public Task BlockAsync(string jid, CancellationToken ct = default) => BlockAsync([jid], ct);
    public Task BlockAsync(Jid jid, CancellationToken ct = default) => BlockAsync([jid.ToString()], ct);
    public Task BlockAsync(IEnumerable<Jid> jids, CancellationToken ct = default) => BlockAsync(jids.Select(j => j.ToString()), ct);

    public async Task UnblockAsync(IEnumerable<string> jids, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var list = jids.Where(j => !string.IsNullOrWhiteSpace(j)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return;

        var iq = IqStanza.CreateSet();
        var unblockElem = new XmppElement("unblock", NsBlocking);
        foreach (var jid in list)
        {
            var item = new XmppElement("item");
            item.Attr("jid", jid);
            unblockElem.Child(item);
        }
        iq.RawElement.Child(unblockElem);

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to unblock contacts: {response.ToXmlString()}");
        }

        foreach (var jid in list)
        {
            _blockedJids.TryRemove(jid, out _);
        }

        ContactsUnblocked?.Invoke(list);
        BlockListUpdated?.Invoke(_blockedJids.Keys.ToList());
    }

    public Task UnblockAsync(string jid, CancellationToken ct = default) => UnblockAsync([jid], ct);
    public Task UnblockAsync(Jid jid, CancellationToken ct = default) => UnblockAsync([jid.ToString()], ct);
    public Task UnblockAsync(IEnumerable<Jid> jids, CancellationToken ct = default) => UnblockAsync(jids.Select(j => j.ToString()), ct);

    public async Task UnblockAllAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var iq = IqStanza.CreateSet();
        iq.RawElement.Child(new XmppElement("unblock", NsBlocking));

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            throw new InvalidOperationException($"Failed to unblock all contacts: {response.ToXmlString()}");
        }

        var removed = _blockedJids.Keys.ToList();
        _blockedJids.Clear();
        BlockListCleared?.Invoke();
        if (removed.Count > 0)
        {
            ContactsUnblocked?.Invoke(removed);
        }
        BlockListUpdated?.Invoke(Array.Empty<string>());
    }

    public override async ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "iq" && element.GetAttr("type") == "set")
        {
            var blockElem = element.Element("block", NsBlocking);
            if (blockElem is not null)
            {
                var items = new List<string>();
                foreach (var item in blockElem.Elements("item"))
                {
                    var jid = item.GetAttr("jid");
                    if (!string.IsNullOrWhiteSpace(jid))
                    {
                        _blockedJids.TryAdd(jid, 0);
                        items.Add(jid);
                    }
                }

                var iq = new IqStanza(element);
                var result = iq.CreateResult();
                await client.SendStanzaAsync(result, cancellationToken).ConfigureAwait(false);

                if (items.Count > 0)
                {
                    ContactsBlocked?.Invoke(items);
                    BlockListUpdated?.Invoke(_blockedJids.Keys.ToList());
                }
                return false;
            }

            var unblockElem = element.Element("unblock", NsBlocking);
            if (unblockElem is not null)
            {
                var items = new List<string>();
                foreach (var item in unblockElem.Elements("item"))
                {
                    var jid = item.GetAttr("jid");
                    if (!string.IsNullOrWhiteSpace(jid))
                    {
                        _blockedJids.TryRemove(jid, out _);
                        items.Add(jid);
                    }
                }

                var iq = new IqStanza(element);
                var result = iq.CreateResult();
                await client.SendStanzaAsync(result, cancellationToken).ConfigureAwait(false);

                if (items.Count > 0)
                {
                    ContactsUnblocked?.Invoke(items);
                }
                else
                {
                    _blockedJids.Clear();
                    BlockListCleared?.Invoke();
                }
                BlockListUpdated?.Invoke(_blockedJids.Keys.ToList());
                return false;
            }
        }

        if (element.Name is "message" or "presence")
        {
            var from = element.GetAttr("from");
            if (!string.IsNullOrWhiteSpace(from) && IsBlocked(from))
            {
                return false;
            }
        }

        return true;
    }
}
