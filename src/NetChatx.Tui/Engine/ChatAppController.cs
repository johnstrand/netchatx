using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Core;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Muc;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Protocol.Xeps.Resilience;
using NetChatx.Protocol.Xeps.Sharing;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using NetChatx.Tui.Commands;
using NetChatx.Tui.Models;
using NetChatx.Tui.Themes;
using NetChatx.Tui.Views;

namespace NetChatx.Tui.Engine;

public sealed class ChatAppController : IAsyncDisposable
{
    private readonly MainChatWindow _window;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly RosterRepository _rosterRepo;
    private readonly AccountRepository _accountRepo;

    private readonly Dictionary<string, XmppClient> _activeClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Xep0045MultiUserChat> _mucManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Xep0384OmemoManager> _omemoManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Xep0199Ping> _pingManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Xep0313MessageArchiveManagement> _mamManagers = new(StringComparer.OrdinalIgnoreCase);

    private XmppClient? _primaryClient;

    public ChatAppController(MainChatWindow window, DatabaseContext? dbContext = null)
    {
        _window = window;
        _dbContext = dbContext ?? new DatabaseContext();
        _messageRepo = new MessageRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);

        _window.OnCommandSubmitted += HandleInputAsync;
        _window.OnBufferSwitched += EnsureBufferHistoryLoadedAsync;
        _window.OnContactActivated += HandleContactActivatedAsync;
    }

    public async Task InitializeAsync()
    {
        var accounts = await _accountRepo.GetAccountsAsync();
        foreach (var acc in accounts)
        {
            if (acc.IsActive)
            {
                _window.ActiveBuffer.AddSystemMessage($"Configured account: {acc.Jid} (type /connect to connect)");
                var contacts = await _rosterRepo.GetContactsAsync(acc.Jid);
                var displays = contacts.Select(c => string.IsNullOrEmpty(c.Name) ? c.ContactJid : $"{c.Name} ({c.ContactJid})").ToList();
                _window.UpdateRoster(displays);
            }
        }
    }

    private async Task HandleInputAsync(string input)
    {
        if (TuiCommandProcessor.IsCommand(input))
        {
            var cmd = TuiCommandProcessor.Parse(input);
            await ExecuteCommandAsync(cmd);
        }
        else
        {
            await SendCurrentBufferMessageAsync(input);
        }
    }

    private async Task ExecuteCommandAsync(ParsedCommand cmd)
    {
        var console = _window.ActiveBuffer;

        switch (cmd.Command)
        {
            case "/help":
                ShowHelp();
                break;

            case "/connect":
                await HandleConnectCommandAsync(cmd.Arguments);
                break;

            case "/disconnect":
                await HandleDisconnectCommandAsync();
                break;

            case "/join":
                await HandleJoinCommandAsync(cmd.Arguments);
                break;

            case "/leave":
                await HandleLeaveCommandAsync(cmd.Arguments);
                break;

            case "/msg":
                await HandleMsgCommandAsync(cmd.Arguments);
                break;

            case "/query":
                if (cmd.Arguments.Length > 0 && Jid.TryParse(cmd.Arguments[0], out var queryJid))
                {
                    var buf = _window.GetOrCreateBuffer(queryJid.BareJid.ToString(), queryJid.BareJid.ToString(), BufferType.DirectChat, queryJid);
                    _window.SwitchToBuffer(buf);
                    await EnsureBufferHistoryLoadedAsync(buf);
                    _window.RefreshActiveBufferView();
                }
                else
                {
                    console.AddSystemMessage("Usage: /query <jid>");
                    _window.RefreshActiveBufferView();
                }
                break;

            case "/history":
                await HandleHistoryCommandAsync(cmd.Arguments);
                break;

            case "/search":
                await HandleSearchCommandAsync(cmd.Arguments);
                break;

            case "/roster":
                await HandleRosterCommandAsync();
                break;

            case "/close":
                _window.CloseActiveBuffer();
                break;

            case "/clear":
                _window.ActiveBuffer.Clear();
                _window.RefreshActiveBufferView();
                break;

            case "/status":
                await HandleStatusCommandAsync(cmd.Arguments);
                break;

            case "/theme":
                if (cmd.Arguments.Length > 0 && Enum.TryParse<TuiTheme>(cmd.Arguments[0], true, out var t))
                {
                    _window.SetScheme(ThemeManager.GetColorScheme(t));
                    console.AddSystemMessage($"Theme switched to {t}.");
                }
                else
                {
                    console.AddSystemMessage("Usage: /theme <Catppuccin|Gruvbox|Nord|Classic>");
                }
                _window.RefreshActiveBufferView();
                break;

            case "/quit":
                Terminal.Gui.App.Application.RequestStop();
                break;

            default:
                console.AddSystemMessage($"Unknown command: '{cmd.Command}'. Type /help for available commands.");
                _window.RefreshActiveBufferView();
                break;
        }
    }

    private void ShowHelp()
    {
        var buf = _window.ActiveBuffer;
        buf.AddSystemMessage("--- NetChatx Commands ---");
        buf.AddSystemMessage("/connect <jid> <password> [host] [port] - Connect to XMPP account");
        buf.AddSystemMessage("/disconnect                           - Disconnect active session");
        buf.AddSystemMessage("/join <room@conference> [nick] [pass] - Join Multi-User Chat room");
        buf.AddSystemMessage("/leave [room@conference]              - Leave group chat room");
        buf.AddSystemMessage("/msg <jid> <text>                     - Send 1-on-1 message");
        buf.AddSystemMessage("/query <jid>                          - Open chat buffer & view contact history");
        buf.AddSystemMessage("/history [count]                      - View older chat history & sync MAM");
        buf.AddSystemMessage("/search <text>                        - Search message history in database");
        buf.AddSystemMessage("/roster                               - View contact roster");
        buf.AddSystemMessage("/close                                - Close current buffer");
        buf.AddSystemMessage("/clear                                - Clear current buffer history");
        buf.AddSystemMessage("/status <available|away|dnd|xa> [msg] - Set presence status");
        buf.AddSystemMessage("/theme <Catppuccin|Gruvbox|Nord|Classic> - Change TUI theme");
        buf.AddSystemMessage("/quit                                 - Exit application");
        buf.AddSystemMessage("Keyboard shortcuts: Alt+1..9 switch buffers, Enter sends text or opens contact.");
        _window.RefreshActiveBufferView();
    }

    public async Task EnsureBufferHistoryLoadedAsync(ChatBuffer buffer)
    {
        if (buffer.HasLoadedHistory || buffer.Type == BufferType.Console || buffer.RemoteJid is null)
            return;

        string? accountKey = _primaryClient?.Options.Jid.BareJid.ToString();
        if (string.IsNullOrEmpty(accountKey))
        {
            var accounts = await _accountRepo.GetAccountsAsync();
            accountKey = accounts.FirstOrDefault()?.Jid;
        }

        if (!string.IsNullOrEmpty(accountKey))
        {
            var messages = await _messageRepo.GetMessagesAsync(accountKey, buffer.RemoteJid.ToString(), limit: 50);
            buffer.LoadHistory(messages);

            if (messages.Count > 0)
            {
                buffer.AddSystemMessage($"--- Loaded {messages.Count} past messages with {buffer.RemoteJid} ---");
            }
            else
            {
                buffer.AddSystemMessage($"No local history found for {buffer.RemoteJid}. Type /history to sync server archive (MAM).");
            }

            _window.RefreshActiveBufferView();
        }
    }

    private async Task HandleContactActivatedAsync(string contactEntry)
    {
        // Contact entry format: "Name (user@example.com)" or "user@example.com"
        string jidStr = contactEntry;
        int openParen = contactEntry.IndexOf('(');
        int closeParen = contactEntry.IndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
        {
            jidStr = contactEntry.Substring(openParen + 1, closeParen - openParen - 1);
        }

        if (Jid.TryParse(jidStr.Trim(), out var jid))
        {
            var buffer = _window.GetOrCreateBuffer(jid.BareJid.ToString(), jid.BareJid.ToString(), BufferType.DirectChat, jid.BareJid);
            _window.SwitchToBuffer(buffer);
            await EnsureBufferHistoryLoadedAsync(buffer);
            _window.RefreshActiveBufferView();
        }
    }

    private async Task HandleHistoryCommandAsync(string[] args)
    {
        var buffer = _window.ActiveBuffer;
        if (buffer.Type == BufferType.Console || buffer.RemoteJid is null)
        {
            buffer.AddSystemMessage("Cannot load history: Active buffer is not associated with a contact or room.");
            _window.RefreshActiveBufferView();
            return;
        }

        int count = args.Length > 0 && int.TryParse(args[0], out int c) ? c : 50;
        string? accountKey = _primaryClient?.Options.Jid.BareJid.ToString();
        if (string.IsNullOrEmpty(accountKey))
        {
            var accounts = await _accountRepo.GetAccountsAsync();
            accountKey = accounts.FirstOrDefault()?.Jid;
        }

        if (string.IsNullOrEmpty(accountKey))
        {
            buffer.AddSystemMessage("No account available for history lookup.");
            _window.RefreshActiveBufferView();
            return;
        }

        // 1. Fetch older local history from SQLite
        var olderMessages = await _messageRepo.GetMessagesAsync(accountKey, buffer.RemoteJid.ToString(), limit: count, before: buffer.OldestMessageTimestamp);
        if (olderMessages.Count > 0)
        {
            buffer.PrependHistory(olderMessages);
            buffer.AddSystemMessage($"[Local] Loaded {olderMessages.Count} older messages.");
        }

        // 2. Query MAM server archive if connected
        if (_primaryClient is not null && _mamManagers.TryGetValue(accountKey, out var mam))
        {
            try
            {
                buffer.AddSystemMessage($"[MAM] Querying server archive for messages with {buffer.RemoteJid}...");
                _window.RefreshActiveBufferView();

                var mamResult = await mam.QueryArchiveAsync(withJid: buffer.RemoteJid, maxResults: count);
                int imported = 0;

                foreach (var item in mamResult.Messages)
                {
                    var msg = item.Message;
                    if (!string.IsNullOrEmpty(msg.Body))
                    {
                        var chatMsg = new ChatMessage
                        {
                            AccountJid = accountKey,
                            RemoteJid = buffer.RemoteJid.ToString(),
                            SenderJid = (msg.From ?? buffer.RemoteJid).ToString(),
                            Body = msg.Body,
                            Direction = (msg.From?.EqualsBare(_primaryClient.BoundJid) == true) ? MessageDirection.Outbound : MessageDirection.Inbound,
                            Timestamp = item.Timestamp,
                            StanzaId = item.ArchiveId
                        };

                        await _messageRepo.SaveMessageAsync(chatMsg);
                        imported++;
                    }
                }

                // Reload combined history
                var allMessages = await _messageRepo.GetMessagesAsync(accountKey, buffer.RemoteJid.ToString(), limit: 100);
                buffer.LoadHistory(allMessages);
                buffer.AddSystemMessage($"[MAM] Synchronized {imported} messages from server archive.");
            }
            catch (Exception ex)
            {
                buffer.AddSystemMessage($"[MAM Error] {ex.Message}");
            }
        }

        _window.RefreshActiveBufferView();
    }

    private async Task HandleSearchCommandAsync(string[] args)
    {
        var buffer = _window.ActiveBuffer;
        if (args.Length == 0)
        {
            buffer.AddSystemMessage("Usage: /search <query>");
            _window.RefreshActiveBufferView();
            return;
        }

        string query = string.Join(" ", args);
        string? accountKey = _primaryClient?.Options.Jid.BareJid.ToString();
        if (string.IsNullOrEmpty(accountKey))
        {
            var accounts = await _accountRepo.GetAccountsAsync();
            accountKey = accounts.FirstOrDefault()?.Jid;
        }

        if (string.IsNullOrEmpty(accountKey))
        {
            buffer.AddSystemMessage("No account available for searching history.");
            _window.RefreshActiveBufferView();
            return;
        }

        var results = await _messageRepo.SearchMessagesAsync(accountKey, query, limit: 25);
        buffer.AddSystemMessage($"--- Search results for '{query}' ({results.Count} matches) ---");

        foreach (var msg in results)
        {
            buffer.AddSystemMessage(ChatBuffer.FormatChatMessage(msg));
        }

        if (results.Count == 0)
        {
            buffer.AddSystemMessage("No matching messages found.");
        }

        _window.RefreshActiveBufferView();
    }

    private async Task HandleRosterCommandAsync()
    {
        var buffer = _window.ActiveBuffer;
        string? accountKey = _primaryClient?.Options.Jid.BareJid.ToString();
        if (string.IsNullOrEmpty(accountKey))
        {
            var accounts = await _accountRepo.GetAccountsAsync();
            accountKey = accounts.FirstOrDefault()?.Jid;
        }

        if (string.IsNullOrEmpty(accountKey))
        {
            buffer.AddSystemMessage("No account active. Use /connect <jid> <password>.");
            _window.RefreshActiveBufferView();
            return;
        }

        var contacts = await _rosterRepo.GetContactsAsync(accountKey);
        buffer.AddSystemMessage($"--- Roster ({contacts.Count} contacts) ---");
        foreach (var c in contacts)
        {
            string nameInfo = string.IsNullOrEmpty(c.Name) ? "" : $" [{c.Name}]";
            buffer.AddSystemMessage($"* {c.ContactJid}{nameInfo} - subscription: {c.Subscription}");
        }

        if (contacts.Count == 0)
        {
            buffer.AddSystemMessage("Roster is empty.");
        }

        _window.RefreshActiveBufferView();
    }

    private async Task HandleConnectCommandAsync(string[] args)
    {
        var buf = _window.ActiveBuffer;
        if (args.Length < 2)
        {
            buf.AddSystemMessage("Usage: /connect <jid> <password> [host] [port]");
            _window.RefreshActiveBufferView();
            return;
        }

        if (!Jid.TryParse(args[0], out var jid))
        {
            buf.AddSystemMessage($"Invalid JID: '{args[0]}'");
            _window.RefreshActiveBufferView();
            return;
        }

        string password = args[1];
        string? host = args.Length > 2 ? args[2] : null;
        int port = args.Length > 3 && int.TryParse(args[3], out int p) ? p : 5222;

        try
        {
            _window.SetStatus($"Connecting to {jid}...");
            buf.AddSystemMessage($"Connecting {jid} to {host ?? jid.Domain}:{port}...");
            _window.RefreshActiveBufferView();

            var options = new XmppClientOptions
            {
                Jid = jid,
                Password = password,
                Host = host,
                Port = port
            };

            var client = new XmppClient(options);
            await SetupXepSuiteAsync(client, jid);

            await client.ConnectAsync();
            _activeClients[jid.BareJid.ToString()] = client;
            _primaryClient = client;

            _window.SetStatus($"Connected: {client.BoundJid} [TLS]");
            buf.AddSystemMessage($"Successfully connected and bound as {client.BoundJid}!");

            await _accountRepo.SaveAccountAsync(new AccountProfile
            {
                Jid = jid.BareJid.ToString(),
                Password = password,
                Host = host,
                Port = port
            });

            // Send initial presence
            await client.SendStanzaAsync(PresenceStanza.Available(status: "Online with NetChatx"));

            // Fetch roster per RFC 6121
            var rosterIq = IqStanza.CreateGet();
            rosterIq.RawElement.Child(new XmppElement("query", "jabber:iq:roster"));
            try
            {
                var rosterResult = await client.SendIqAsync(rosterIq);
                var queryElem = rosterResult.RawElement.Element("query", "jabber:iq:roster");
                if (queryElem is not null)
                {
                    var contactsList = new List<string>();
                    foreach (var item in queryElem.Elements("item"))
                    {
                        string? contactJid = item.GetAttr("jid");
                        string? name = item.GetAttr("name");
                        string subscription = item.GetAttr("subscription") ?? "none";
                        if (!string.IsNullOrEmpty(contactJid))
                        {
                            await _rosterRepo.UpsertContactAsync(new RosterContact
                            {
                                AccountJid = jid.BareJid.ToString(),
                                ContactJid = contactJid,
                                Name = name,
                                Subscription = subscription
                            });
                            string display = string.IsNullOrEmpty(name) ? contactJid : $"{name} ({contactJid})";
                            contactsList.Add(display);
                        }
                    }
                    _window.UpdateRoster(contactsList);
                }
            }
            catch
            {
                // Soft failure for roster query
            }

            _window.RefreshActiveBufferView();
        }
        catch (Exception ex)
        {
            _window.SetStatus($"Connection failed: {ex.Message}");
            buf.AddSystemMessage($"Connection error: {ex.Message}");
            _window.RefreshActiveBufferView();
        }
    }

    private async Task SetupXepSuiteAsync(XmppClient client, Jid jid)
    {
        string accountKey = jid.BareJid.ToString();

        var disco = new Xep0030ServiceDiscovery();
        var ping = new Xep0199Ping();
        var sm = new Xep0198StreamManagement();
        var csi = new Xep0352ClientStateIndication();
        var carbons = new Xep0280MessageCarbons();
        var mam = new Xep0313MessageArchiveManagement();
        var sid = new Xep0359StanzaIds();
        var receipts = new Xep0184MessageDeliveryReceipts();
        var chatStates = new Xep0085ChatStates();
        var correction = new Xep0308LastMessageCorrection();
        var muc = new Xep0045MultiUserChat();
        var upload = new Xep0363HttpFileUpload();
        var omemo = new Xep0384OmemoManager();

        await disco.AttachAsync(client);
        await ping.AttachAsync(client);
        await sm.AttachAsync(client);
        await csi.AttachAsync(client);
        await carbons.AttachAsync(client);
        await mam.AttachAsync(client);
        await sid.AttachAsync(client);
        await receipts.AttachAsync(client);
        await chatStates.AttachAsync(client);
        await correction.AttachAsync(client);
        await muc.AttachAsync(client);
        await upload.AttachAsync(client);
        await omemo.AttachAsync(client);

        _mucManagers[accountKey] = muc;
        _omemoManagers[accountKey] = omemo;
        _pingManagers[accountKey] = ping;
        _mamManagers[accountKey] = mam;

        // Wire incoming messages
        client.MessageReceived += async msg =>
        {
            await HandleIncomingMessageAsync(accountKey, msg);
        };

        // Wire carbons
        carbons.CarbonMessageReceived += async (msg, isSent) =>
        {
            await HandleIncomingMessageAsync(accountKey, msg, isCarbon: true);
        };

        // Wire OMEMO decrypted messages
        omemo.MessageDecrypted += async dec =>
        {
            var remoteJid = dec.SenderJid.BareJid;
            var buffer = _window.GetOrCreateBuffer(remoteJid.ToString(), remoteJid.ToString(), BufferType.DirectChat, remoteJid);
            buffer.IsEncrypted = true;

            var chatMsg = new ChatMessage
            {
                AccountJid = accountKey,
                RemoteJid = remoteJid.ToString(),
                SenderJid = dec.SenderJid.ToString(),
                Body = dec.PlaintextBody,
                Direction = MessageDirection.Inbound,
                IsEncrypted = true,
                EncryptionType = "OMEMO"
            };

            await _messageRepo.SaveMessageAsync(chatMsg);
            buffer.AddChatMessage(chatMsg);
            _window.RefreshActiveBufferView();
        };

        // Wire MUC occupant changes
        muc.RoomOccupantChanged += (roomJid, occ, isJoined) =>
        {
            var buffer = _window.GetOrCreateBuffer(roomJid.ToString(), roomJid.ToString(), BufferType.GroupChat, roomJid);
            buffer.AddSystemMessage($"{occ.Nickname} has {(isJoined ? "joined" : "left")} the room ({occ.Role}).");
            _window.RefreshActiveBufferView();
        };
    }

    private async Task HandleIncomingMessageAsync(string accountKey, MessageStanza msg, bool isCarbon = false)
    {
        if (string.IsNullOrEmpty(msg.Body)) return;

        var sender = msg.From ?? Jid.Parse(accountKey);
        var remote = msg.Type == MessageStanza.TypeGroupChat ? sender.BareJid : sender.BareJid;
        var bufferType = msg.Type == MessageStanza.TypeGroupChat ? BufferType.GroupChat : BufferType.DirectChat;

        var buffer = _window.GetOrCreateBuffer(remote.ToString(), remote.ToString(), bufferType, remote);

        var chatMsg = new ChatMessage
        {
            AccountJid = accountKey,
            RemoteJid = remote.ToString(),
            SenderJid = sender.ToString(),
            Body = msg.Body,
            Direction = isCarbon ? MessageDirection.Outbound : MessageDirection.Inbound,
            StanzaId = msg.Id
        };

        await _messageRepo.SaveMessageAsync(chatMsg);
        buffer.AddChatMessage(chatMsg);
        _window.RefreshActiveBufferView();
    }

    private async Task SendCurrentBufferMessageAsync(string text)
    {
        var buffer = _window.ActiveBuffer;
        if (buffer.Type == BufferType.Console || buffer.RemoteJid is null || _primaryClient is null)
        {
            buffer.AddSystemMessage("Cannot send message: no active chat buffer or client not connected.");
            _window.RefreshActiveBufferView();
            return;
        }

        string accountKey = _primaryClient.Options.Jid.BareJid.ToString();

        if (buffer.Type == BufferType.GroupChat)
        {
            var groupMsg = MessageStanza.CreateGroupChat(buffer.RemoteJid, text);
            await _primaryClient.SendStanzaAsync(groupMsg);
        }
        else
        {
            var chatMsg = MessageStanza.CreateChat(buffer.RemoteJid, text);
            await _primaryClient.SendStanzaAsync(chatMsg);
        }

        var saved = new ChatMessage
        {
            AccountJid = accountKey,
            RemoteJid = buffer.RemoteJid.ToString(),
            SenderJid = _primaryClient.BoundJid.ToString(),
            Body = text,
            Direction = MessageDirection.Outbound
        };

        await _messageRepo.SaveMessageAsync(saved);
        buffer.AddChatMessage(saved);
        _window.RefreshActiveBufferView();
    }

    private async Task HandleDisconnectCommandAsync()
    {
        if (_primaryClient is not null)
        {
            await _primaryClient.DisconnectAsync();
            _primaryClient = null;
            _window.SetStatus("[Disconnected]");
            _window.ActiveBuffer.AddSystemMessage("Disconnected from XMPP server.");
            _window.RefreshActiveBufferView();
        }
    }

    private async Task HandleJoinCommandAsync(string[] args)
    {
        if (_primaryClient is null)
        {
            _window.ActiveBuffer.AddSystemMessage("Please connect first with /connect.");
            _window.RefreshActiveBufferView();
            return;
        }

        if (args.Length == 0 || !Jid.TryParse(args[0], out var roomJid))
        {
            _window.ActiveBuffer.AddSystemMessage("Usage: /join <room@conference> [nickname] [password]");
            _window.RefreshActiveBufferView();
            return;
        }

        string nick = args.Length > 1 ? args[1] : (_primaryClient.BoundJid.LocalPart ?? "NetChatx");
        string? pass = args.Length > 2 ? args[2] : null;

        string accountKey = _primaryClient.Options.Jid.BareJid.ToString();
        if (_mucManagers.TryGetValue(accountKey, out var muc))
        {
            await muc.JoinRoomAsync(roomJid, nick, pass);
            var buf = _window.GetOrCreateBuffer(roomJid.BareJid.ToString(), roomJid.BareJid.ToString(), BufferType.GroupChat, roomJid.BareJid);
            _window.SwitchToBuffer(buf);
            buf.AddSystemMessage($"Joined {roomJid} as {nick}...");
            _window.RefreshActiveBufferView();
        }
    }

    private async Task HandleLeaveCommandAsync(string[] args)
    {
        if (_primaryClient is null) return;

        Jid? targetRoom = null;
        if (args.Length > 0 && Jid.TryParse(args[0], out var r))
            targetRoom = r;
        else if (_window.ActiveBuffer.Type == BufferType.GroupChat)
            targetRoom = _window.ActiveBuffer.RemoteJid;

        if (targetRoom is not null)
        {
            string accountKey = _primaryClient.Options.Jid.BareJid.ToString();
            if (_mucManagers.TryGetValue(accountKey, out var muc))
            {
                await muc.LeaveRoomAsync(targetRoom);
                _window.ActiveBuffer.AddSystemMessage($"Left {targetRoom}.");
                _window.RefreshActiveBufferView();
            }
        }
    }

    private async Task HandleMsgCommandAsync(string[] args)
    {
        if (_primaryClient is null)
        {
            _window.ActiveBuffer.AddSystemMessage("Please connect first with /connect.");
            _window.RefreshActiveBufferView();
            return;
        }

        if (args.Length < 2 || !Jid.TryParse(args[0], out var toJid))
        {
            _window.ActiveBuffer.AddSystemMessage("Usage: /msg <jid> <text>");
            _window.RefreshActiveBufferView();
            return;
        }

        string text = string.Join(" ", args.Skip(1));
        var buffer = _window.GetOrCreateBuffer(toJid.BareJid.ToString(), toJid.BareJid.ToString(), BufferType.DirectChat, toJid.BareJid);
        _window.SwitchToBuffer(buffer);
        await EnsureBufferHistoryLoadedAsync(buffer);

        var msg = MessageStanza.CreateChat(toJid, text);
        await _primaryClient.SendStanzaAsync(msg);

        var saved = new ChatMessage
        {
            AccountJid = _primaryClient.Options.Jid.BareJid.ToString(),
            RemoteJid = toJid.BareJid.ToString(),
            SenderJid = _primaryClient.BoundJid.ToString(),
            Body = text,
            Direction = MessageDirection.Outbound
        };

        await _messageRepo.SaveMessageAsync(saved);
        buffer.AddChatMessage(saved);
        _window.RefreshActiveBufferView();
    }

    private async Task HandleStatusCommandAsync(string[] args)
    {
        if (_primaryClient is null) return;

        string show = args.Length > 0 ? args[0].ToLowerInvariant() : "available";
        string? msg = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

        var pres = show switch
        {
            "away" => PresenceStanza.Available(show: PresenceStanza.ShowAway, status: msg),
            "dnd" => PresenceStanza.Available(show: PresenceStanza.ShowDnd, status: msg),
            "xa" => PresenceStanza.Available(show: PresenceStanza.ShowXa, status: msg),
            _ => PresenceStanza.Available(status: msg)
        };

        await _primaryClient.SendStanzaAsync(pres);
        _window.ActiveBuffer.AddSystemMessage($"Status updated to [{show}] {msg}");
        _window.RefreshActiveBufferView();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _activeClients.Values)
        {
            await client.DisposeAsync();
        }
        _dbContext.Dispose();
    }
}
