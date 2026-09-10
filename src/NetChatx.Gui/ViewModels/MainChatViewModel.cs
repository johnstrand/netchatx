using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Stanzas;
using NetChatx.Core.Xml;
using NetChatx.Protocol.Xeps.Messaging;
using NetChatx.Protocol.Xeps.Muc;
using NetChatx.Protocol.Xeps.Omemo;
using NetChatx.Protocol.Xeps.Sharing;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;

namespace NetChatx.Gui.ViewModels;

public sealed partial class MainChatViewModel : ViewModelBase
{
    private readonly XmppClient _client;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly RosterRepository _rosterRepo;
    private readonly AccountRepository _accountRepo;
    private readonly OmemoRepository _omemoRepo;
    private readonly Func<Task> _onDisconnectRequested;

    private Xep0313MessageArchiveManagement? _mam;
    private Xep0384OmemoManager? _omemo;
    private Xep0045MultiUserChat? _muc;
    private Xep0363HttpFileUpload? _httpUpload;
    private Xep0280MessageCarbons? _carbons;
    private Xep0444Reactions? _reactions;
    private Xep0184MessageDeliveryReceipts? _receipts;
    private Xep0333ChatMarkers? _chatMarkers;
    private Xep0085ChatStates? _chatStates;

    [ObservableProperty]
    private string _accountJid;

    [ObservableProperty]
    private string _userBoundJid;

    [ObservableProperty]
    private string _userPresence = "available";

    [ObservableProperty]
    private string _statusMessage = "Online with NetChatx";

    [ObservableProperty]
    private ChatConversationViewModel? _activeConversation;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isDetailsOpen;

    [ObservableProperty]
    private OmemoDetailsViewModel _omemoDetails = new();

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];
    public ObservableCollection<ChatConversationViewModel> Conversations { get; } = [];
    public ObservableCollection<MessageBubbleViewModel> SearchResults { get; } = [];

    public MainChatViewModel(
        XmppClient client,
        DatabaseContext dbContext,
        Func<Task> onDisconnectRequested)
    {
        _client = client;
        _dbContext = dbContext;
        _onDisconnectRequested = onDisconnectRequested;

        _messageRepo = new MessageRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);
        _omemoRepo = new OmemoRepository(_dbContext);

        _accountJid = client.Options.Jid.BareJid.ToString();
        _userBoundJid = client.BoundJid.ToString();
    }

    public async Task InitializeAsync()
    {
        // Setup XEP managers
        _mam = new Xep0313MessageArchiveManagement();
        _omemo = new Xep0384OmemoManager();
        _muc = new Xep0045MultiUserChat();
        _httpUpload = new Xep0363HttpFileUpload();
        _carbons = new Xep0280MessageCarbons();
        _reactions = new Xep0444Reactions();
        _receipts = new Xep0184MessageDeliveryReceipts();
        _chatMarkers = new Xep0333ChatMarkers();
        _chatStates = new Xep0085ChatStates();

        await _mam.AttachAsync(_client);
        await _omemo.AttachAsync(_client);
        await _muc.AttachAsync(_client);
        await _httpUpload.AttachAsync(_client);
        await _carbons.AttachAsync(_client);
        await _reactions.AttachAsync(_client);
        await _receipts.AttachAsync(_client);
        await _chatMarkers.AttachAsync(_client);
        await _chatStates.AttachAsync(_client);

        _chatStates.ChatStateReceived += (fromJid, state) =>
        {
            PostToUi(() =>
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                conv?.HandleRemoteChatState(state);
            });
        };

        _receipts.ReceiptReceived += async (stanzaId, fromJid) =>
        {
            await HandleReceiptOrMarkerReceivedAsync(stanzaId, fromJid);
        };

        _chatMarkers.MarkerReceived += async (stanzaId, fromJid, markerType) =>
        {
            await HandleReceiptOrMarkerReceivedAsync(stanzaId, fromJid);
        };

        try
        {
            await _carbons.EnableAsync();
        }
        catch
        {
            // Soft failure if server does not support carbons
        }

        // Wire incoming messages
        _client.MessageReceived += async msg =>
        {
            await HandleIncomingMessageAsync(msg);
        };

        // Wire carbon copy messages
        _carbons.CarbonMessageReceived += async (msg, isSentByUs) =>
        {
            await HandleCarbonMessageAsync(msg, isSentByUs);
        };

        // Wire OMEMO decrypted messages
        _omemo.MessageDecrypted += async dec =>
        {
            await HandleDecryptedMessageAsync(dec);
        };

        // Wire reactions
        _reactions.ReactionReceived += async args =>
        {
            await HandleIncomingReactionAsync(args);
        };

        // Load cached contacts from SQLite
        var cachedContacts = await _rosterRepo.GetContactsAsync(AccountJid);
        foreach (var c in cachedContacts)
        {
            Contacts.Add(ContactItemViewModel.FromRosterContact(c));
        }

        // Query roster from server per RFC 6121
        try
        {
            var rosterIq = IqStanza.CreateGet();
            rosterIq.RawElement.Child(new XmppElement("query", "jabber:iq:roster"));
            var result = await _client.SendIqAsync(rosterIq);
            var queryElem = result.RawElement.Element("query", "jabber:iq:roster");
            if (queryElem is not null)
            {
                var contactsToUpsert = new System.Collections.Generic.List<RosterContact>();
                foreach (var item in queryElem.Elements("item"))
                {
                    string? cJid = item.GetAttr("jid");
                    string? name = item.GetAttr("name");
                    string sub = item.GetAttr("subscription") ?? "none";
                    if (!string.IsNullOrEmpty(cJid))
                    {
                        contactsToUpsert.Add(new RosterContact
                        {
                            AccountJid = AccountJid,
                            ContactJid = cJid,
                            Name = name,
                            Subscription = sub
                        });

                        var existing = Contacts.FirstOrDefault(x => x.ContactJid.Equals(cJid, StringComparison.OrdinalIgnoreCase));
                        if (existing is null)
                        {
                            Contacts.Add(new ContactItemViewModel
                            {
                                AccountJid = AccountJid,
                                ContactJid = cJid,
                                Name = name,
                                Subscription = sub
                            });
                        }
                        else
                        {
                            existing.Name = name;
                            existing.Subscription = sub;
                        }
                    }
                }

                if (contactsToUpsert.Count > 0)
                {
                    await _rosterRepo.UpsertContactsAsync(contactsToUpsert);
                }
            }
        }
        catch
        {
            // Soft failure for roster fetch
        }

        // If contacts exist, select the first one by default
        if (Contacts.Count > 0)
        {
            await SelectContactAsync(Contacts[0]);
        }
    }

    [RelayCommand]
    public async Task SelectContactAsync(ContactItemViewModel contact)
    {
        if (!Jid.TryParse(contact.ContactJid, out var jid)) return;

        contact.UnreadCount = 0;
        var conv = GetOrCreateConversation(jid.BareJid.ToString(), contact.DisplayName, jid.BareJid, isGroupChat: false);
        ActiveConversation = conv;
        await conv.EnsureHistoryLoadedAsync();

        // Load OMEMO devices for contact
        OmemoDetails.ContactJid = contact.ContactJid;
        var devices = await _omemoRepo.GetSessionsAsync(AccountJid, contact.ContactJid);
        var devVms = devices.Select(d => new OmemoDeviceItemViewModel(
            _omemoRepo,
            AccountJid,
            contact.ContactJid,
            (uint)d.DeviceId,
            $"{d.DeviceId:X8}",
            (OmemoTrustState)d.TrustState
        ));
        OmemoDetails.LoadDevices(devVms);
    }

    [RelayCommand]
    public async Task SelectConversationAsync(ChatConversationViewModel conv)
    {
        ActiveConversation = conv;

        var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(conv.RemoteJid.ToString(), StringComparison.OrdinalIgnoreCase));
        if (contact is not null)
        {
            contact.UnreadCount = 0;
        }

        await conv.EnsureHistoryLoadedAsync();
    }

    public ChatConversationViewModel GetOrCreateConversation(string id, string title, Jid remoteJid, bool isGroupChat)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var newConv = new ChatConversationViewModel(
            AccountJid,
            id,
            title,
            remoteJid,
            isGroupChat,
            _messageRepo,
            _client,
            _mam,
            _omemo,
            _httpUpload,
            _reactions,
            _chatMarkers,
            _chatStates);

        Conversations.Add(newConv);
        return newConv;
    }

    private async Task HandleReceiptOrMarkerReceivedAsync(string stanzaId, Jid? fromJid)
    {
        if (string.IsNullOrEmpty(stanzaId)) return;

        await _messageRepo.MarkMessageAsReadAsync(AccountJid, stanzaId);

        PostToUi(() =>
        {
            if (fromJid is not null)
            {
                var conv = Conversations.FirstOrDefault(c => c.RemoteJid.EqualsBare(fromJid));
                conv?.MarkMessageAsRead(stanzaId);
            }
            else
            {
                foreach (var conv in Conversations)
                {
                    conv.MarkMessageAsRead(stanzaId);
                }
            }
        });
    }

    [RelayCommand]
    public void ToggleDetails()
    {
        IsDetailsOpen = !IsDetailsOpen;
    }

    [RelayCommand]
    public async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchResults.Clear();
            IsSearching = false;
            return;
        }

        IsSearching = true;
        var results = await _messageRepo.SearchMessagesAsync(AccountJid, SearchQuery.Trim(), limit: 50);
        SearchResults.Clear();
        foreach (var r in results)
        {
            SearchResults.Add(MessageBubbleViewModel.FromChatMessage(r));
        }
    }

    [RelayCommand]
    public void CloseSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        IsSearching = false;
    }

    [RelayCommand]
    public async Task SetPresenceAsync(string show)
    {
        UserPresence = show;
        var stanza = show switch
        {
            "away" => PresenceStanza.Available(show: PresenceStanza.ShowAway, status: StatusMessage),
            "dnd" => PresenceStanza.Available(show: PresenceStanza.ShowDnd, status: StatusMessage),
            "xa" => PresenceStanza.Available(show: PresenceStanza.ShowXa, status: StatusMessage),
            _ => PresenceStanza.Available(status: StatusMessage)
        };

        try
        {
            await _client.SendStanzaAsync(stanza);
        }
        catch
        {
            // Soft failure on presence send error
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
        await _onDisconnectRequested();
    }

    private async Task HandleIncomingReactionAsync(ReactionEventArgs args)
    {
        var conv = GetOrCreateConversation(args.RemoteJid.ToString(), args.RemoteJid.ToString(), args.RemoteJid, isGroupChat: false);
        await conv.HandleIncomingReactionAsync(args);
    }

    private async Task HandleIncomingMessageAsync(MessageStanza msg)
    {
        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(AccountJid);
        var sender = msg.From ?? accountJid;

        bool isFromSelf = sender.EqualsBare(accountJid);
        Jid remote;
        MessageDirection direction;

        if (isFromSelf)
        {
            remote = (msg.To ?? accountJid).BareJid;
            direction = MessageDirection.Outbound;
        }
        else
        {
            remote = msg.Type == MessageStanza.TypeGroupChat ? sender.BareJid : sender.BareJid;
            direction = MessageDirection.Inbound;
        }

        bool isGroup = msg.Type == MessageStanza.TypeGroupChat;
        bool isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remote.ToString(),
            SenderJid = sender.ToString(),
            Body = msg.Body,
            Direction = direction,
            Timestamp = DateTimeOffset.UtcNow,
            StanzaId = msg.Id,
            RawXml = msg.ToXmlString(indent: true),
            IsRead = (direction == MessageDirection.Outbound) || (isActiveConv && direction == MessageDirection.Inbound)
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        if (isActiveConv && direction == MessageDirection.Inbound && !string.IsNullOrEmpty(chatMsg.StanzaId) && _chatMarkers is not null)
        {
            try
            {
                await _chatMarkers.SendDisplayedMarkerAsync(remote, chatMsg.StanzaId);
            }
            catch
            {
                // Soft failure sending displayed marker
            }
        }

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = msg.Body;
                if (ActiveConversation?.Id != remote.ToString() && direction == MessageDirection.Inbound)
                {
                    contact.UnreadCount++;
                }
            }
        });
    }

    private async Task HandleCarbonMessageAsync(MessageStanza msg, bool isSentByUs)
    {
        if (string.IsNullOrEmpty(msg.Body)) return;

        var accountJid = Jid.Parse(AccountJid);
        Jid remote;
        MessageDirection direction;

        if (isSentByUs)
        {
            remote = (msg.To ?? accountJid).BareJid;
            direction = MessageDirection.Outbound;
        }
        else
        {
            remote = (msg.From ?? accountJid).BareJid;
            direction = MessageDirection.Inbound;
        }

        bool isGroup = msg.Type == MessageStanza.TypeGroupChat;
        var sender = msg.From ?? (isSentByUs ? accountJid : remote);
        bool isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remote) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remote.ToString(),
            SenderJid = sender.ToString(),
            Body = msg.Body,
            Direction = direction,
            Timestamp = DateTimeOffset.UtcNow,
            StanzaId = msg.Id,
            RawXml = msg.ToXmlString(indent: true),
            IsRead = (direction == MessageDirection.Outbound) || (isActiveConv && direction == MessageDirection.Inbound)
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        if (isActiveConv && direction == MessageDirection.Inbound && !string.IsNullOrEmpty(chatMsg.StanzaId) && _chatMarkers is not null)
        {
            try
            {
                await _chatMarkers.SendDisplayedMarkerAsync(remote, chatMsg.StanzaId);
            }
            catch
            {
                // Soft failure sending displayed marker
            }
        }

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = msg.Body;
                if (ActiveConversation?.Id != remote.ToString() && direction == MessageDirection.Inbound)
                {
                    contact.UnreadCount++;
                }
            }
        });
    }

    private async Task HandleDecryptedMessageAsync(DecryptedOmemoMessage dec)
    {
        var remoteJid = dec.SenderJid.BareJid;
        bool isActiveConv = ActiveConversation?.RemoteJid.EqualsBare(remoteJid) == true;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remoteJid.ToString(),
            SenderJid = dec.SenderJid.ToString(),
            Body = dec.PlaintextBody,
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow,
            IsEncrypted = true,
            EncryptionType = "OMEMO",
            RawXml = dec.OriginalStanza.ToXmlString(indent: true),
            IsRead = isActiveConv
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        PostToUi(() =>
        {
            var conv = GetOrCreateConversation(remoteJid.ToString(), remoteJid.ToString(), remoteJid, isGroupChat: false);
            conv.IsEncrypted = true;
            conv.ReceiveMessage(chatMsg);
        });
    }

    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
