using System.Collections.Concurrent;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Common;

namespace NetChatx.Protocol.Xeps.Muc;

public sealed class MucOccupant
{
    public required string Nickname { get; init; }
    public string Affiliation { get; set; } = "none";
    public string Role { get; set; } = "participant";
    public Jid? RealJid { get; set; }
    public string? Status { get; set; }
}

public sealed class MucRoomState
{
    public required Jid RoomJid { get; init; }
    public required string Nickname { get; set; }
    public string? Subject { get; set; }
    public ConcurrentDictionary<string, MucOccupant> Occupants { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Xep0045MultiUserChat : XepFeatureBase
{
    public const string NsMuc = "http://jabber.org/protocol/muc";
    public const string NsMucUser = "http://jabber.org/protocol/muc#user";

    public override string Name => "XEP-0045: Multi-User Chat";
    public override string FeatureUri => NsMuc;

    private readonly ConcurrentDictionary<Jid, MucRoomState> _joinedRooms = new();

    public IReadOnlyDictionary<Jid, MucRoomState> JoinedRooms => _joinedRooms;

    public event Action<Jid, string>? RoomSubjectChanged; // roomJid, newSubject
    public event Action<Jid, MucOccupant, bool>? RoomOccupantChanged; // roomJid, occupant, isJoined

    public async Task JoinRoomAsync(Jid roomJid, string nickname, string? password = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var occupantJid = roomJid.WithResource(nickname);
        var pres = new PresenceStanza(to: occupantJid);

        var mucElem = new XmppElement("x", NsMuc);
        if (!string.IsNullOrEmpty(password))
        {
            mucElem.Child(new XmppElement("password") { Value = password });
        }
        pres.RawElement.Child(mucElem);

        var state = new MucRoomState
        {
            RoomJid = roomJid.BareJid,
            Nickname = nickname
        };
        _joinedRooms[roomJid.BareJid] = state;

        await Client.SendStanzaAsync(pres, ct);
    }

    public async Task LeaveRoomAsync(Jid roomJid, string? status = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        if (_joinedRooms.TryRemove(roomJid.BareJid, out var state))
        {
            var occupantJid = roomJid.WithResource(state.Nickname);
            var pres = new PresenceStanza(type: PresenceStanza.TypeUnavailable, to: occupantJid, status: status);
            await Client.SendStanzaAsync(pres, ct);
        }
    }

    public async Task SendGroupMessageAsync(Jid roomJid, string body, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var msg = MessageStanza.CreateGroupChat(roomJid.BareJid, body);
        await Client.SendStanzaAsync(msg, ct);
    }

    public async Task SetSubjectAsync(Jid roomJid, string subject, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var msg = new MessageStanza(to: roomJid.BareJid, type: MessageStanza.TypeGroupChat);
        msg.Subject = subject;
        await Client.SendStanzaAsync(msg, ct);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "presence")
        {
            string? fromStr = element.GetAttr("from");
            if (!string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid) && fromJid.IsFull)
            {
                var roomJid = fromJid.BareJid;
                if (_joinedRooms.TryGetValue(roomJid, out var room))
                {
                    string nick = fromJid.Resource!;
                    string? type = element.GetAttr("type");
                    bool isUnavailable = string.Equals(type, PresenceStanza.TypeUnavailable, StringComparison.OrdinalIgnoreCase);

                    var mucUser = element.Element("x", NsMucUser);
                    var item = mucUser?.Element("item");

                    if (isUnavailable)
                    {
                        if (room.Occupants.TryRemove(nick, out var removed))
                        {
                            RoomOccupantChanged?.Invoke(roomJid, removed, false);
                        }
                    }
                    else
                    {
                        var occupant = room.Occupants.GetOrAdd(nick, n => new MucOccupant { Nickname = n });
                        if (item is not null)
                        {
                            occupant.Affiliation = item.GetAttr("affiliation") ?? occupant.Affiliation;
                            occupant.Role = item.GetAttr("role") ?? occupant.Role;
                            string? jidStr = item.GetAttr("jid");
                            if (!string.IsNullOrEmpty(jidStr) && Jid.TryParse(jidStr, out var realJid))
                            {
                                occupant.RealJid = realJid;
                            }
                        }
                        occupant.Status = element.Element("status")?.Value;
                        RoomOccupantChanged?.Invoke(roomJid, occupant, true);
                    }
                }
            }
        }
        else if (element.Name == "message" && element.GetAttr("type") == "groupchat")
        {
            string? fromStr = element.GetAttr("from");
            if (!string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
            {
                var roomJid = fromJid.BareJid;
                if (_joinedRooms.TryGetValue(roomJid, out var room))
                {
                    string? subject = element.Element("subject")?.Value;
                    if (subject is not null && subject != room.Subject)
                    {
                        room.Subject = subject;
                        RoomSubjectChanged?.Invoke(roomJid, subject);
                    }
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
