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

        await _mam.AttachAsync(_client);
        await _omemo.AttachAsync(_client);
        await _muc.AttachAsync(_client);

        // Wire incoming messages
        _client.MessageReceived += async msg =>
        {
            await HandleIncomingMessageAsync(msg);
        };

        // Wire OMEMO decrypted messages
        _omemo.MessageDecrypted += async dec =>
        {
            await HandleDecryptedMessageAsync(dec);
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
                foreach (var item in queryElem.Elements("item"))
                {
                    string? cJid = item.GetAttr("jid");
                    string? name = item.GetAttr("name");
                    string sub = item.GetAttr("subscription") ?? "none";
                    if (!string.IsNullOrEmpty(cJid))
                    {
                        await _rosterRepo.UpsertContactAsync(new RosterContact
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
            _omemo);

        Conversations.Add(newConv);
        return newConv;
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

        await _client.SendStanzaAsync(stanza);
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
        await _onDisconnectRequested();
    }

    private async Task HandleIncomingMessageAsync(MessageStanza msg)
    {
        if (string.IsNullOrEmpty(msg.Body)) return;

        var sender = msg.From ?? Jid.Parse(AccountJid);
        var remote = msg.Type == MessageStanza.TypeGroupChat ? sender.BareJid : sender.BareJid;
        bool isGroup = msg.Type == MessageStanza.TypeGroupChat;

        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remote.ToString(),
            SenderJid = sender.ToString(),
            Body = msg.Body,
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow,
            StanzaId = msg.Id
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        Dispatcher.UIThread.Post(() =>
        {
            var conv = GetOrCreateConversation(remote.ToString(), remote.ToString(), remote, isGroup);
            conv.ReceiveMessage(chatMsg);

            var contact = Contacts.FirstOrDefault(c => c.ContactJid.Equals(remote.ToString(), StringComparison.OrdinalIgnoreCase));
            if (contact is not null)
            {
                contact.LastMessagePreview = msg.Body;
                if (ActiveConversation?.Id != remote.ToString())
                {
                    contact.UnreadCount++;
                }
            }
        });
    }

    private async Task HandleDecryptedMessageAsync(DecryptedOmemoMessage dec)
    {
        var remoteJid = dec.SenderJid.BareJid;
        var chatMsg = new ChatMessage
        {
            AccountJid = AccountJid,
            RemoteJid = remoteJid.ToString(),
            SenderJid = dec.SenderJid.ToString(),
            Body = dec.PlaintextBody,
            Direction = MessageDirection.Inbound,
            Timestamp = DateTimeOffset.UtcNow,
            IsEncrypted = true,
            EncryptionType = "OMEMO"
        };

        await _messageRepo.SaveMessageAsync(chatMsg);

        Dispatcher.UIThread.Post(() =>
        {
            var conv = GetOrCreateConversation(remoteJid.ToString(), remoteJid.ToString(), remoteJid, isGroupChat: false);
            conv.IsEncrypted = true;
            conv.ReceiveMessage(chatMsg);
        });
    }
}
