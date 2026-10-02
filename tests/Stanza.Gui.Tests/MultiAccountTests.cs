using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.Services;
using Stanza.Gui.ViewModels;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Registration;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class MultiAccountTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly AccountRepository _accountRepo;

    public MultiAccountTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stanza_multiacc_test_{Guid.NewGuid():N}.db");
        _dbContext = new DatabaseContext(_dbPath, TestSecretProtector.Instance);
        _accountRepo = new AccountRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void AccountSession_Properties_DisplayNameFallbackAndColors()
    {
        var profileWithLabel = new AccountProfile
        {
            Jid = "work@example.com",
            Password = "pass",
            Label = "Work Office",
            ColorHex = "#3B82F6",
            IsActive = true
        };
        var sessionWithLabel = new AccountSession(profileWithLabel);
        Assert.Equal("Work Office", sessionWithLabel.DisplayName);
        Assert.Equal("Work Office", sessionWithLabel.Label);
        Assert.Equal("#3B82F6", sessionWithLabel.ColorHex);
        Assert.NotNull(sessionWithLabel.AccountBrush);
        Assert.Equal("WO", sessionWithLabel.Initials);
        Assert.False(sessionWithLabel.HasUnread);

        var profileWithoutLabel = new AccountProfile
        {
            Jid = "alice@example.com",
            Password = "pass",
            Label = null,
            ColorHex = null,
            IsActive = true
        };
        var sessionWithoutLabel = new AccountSession(profileWithoutLabel);
        Assert.Equal("alice@example.com", sessionWithoutLabel.DisplayName);
        Assert.Null(sessionWithoutLabel.Label);
        Assert.Equal("#00F0FF", sessionWithoutLabel.ColorHex); // default fallback color
        Assert.NotNull(sessionWithoutLabel.AccountBrush);

        sessionWithoutLabel.UnreadCount = 3;
        Assert.True(sessionWithoutLabel.HasUnread);

        sessionWithoutLabel.UnreadCount = 0;
        Assert.False(sessionWithoutLabel.HasUnread);
    }

    [Fact]
    public async Task AccountSessionManager_Lifecycle_AddAndRemove()
    {
        var p1 = new AccountProfile
        {
            Jid = "alice@example.com",
            Password = "pass1",
            Label = "Personal",
            ColorHex = "#00F0FF",
            IsActive = true
        };
        var p2 = new AccountProfile
        {
            Jid = "work@example.com",
            Password = "pass2",
            Label = "Work",
            ColorHex = "#F43F5E",
            IsActive = true
        };
        await _accountRepo.SaveAccountAsync(p1);
        await _accountRepo.SaveAccountAsync(p2);

        var manager = new AccountSessionManager(_dbContext, _accountRepo);
        await manager.InitializeAsync(autoConnect: false);

        Assert.Equal(2, manager.Sessions.Count);
        Assert.NotNull(manager.GetSession("alice@example.com"));
        Assert.NotNull(manager.GetSession("ALICE@EXAMPLE.COM/desktop"));
        Assert.NotNull(manager.GetSession("work@example.com"));
        Assert.Null(manager.GetSession("nobody@example.com"));

        // Test Unread Count aggregation
        manager.Sessions[0].UnreadCount = 4;
        manager.Sessions[1].UnreadCount = 2;
        manager.UpdateTotalUnreadCount();
        Assert.Equal(6, manager.TotalUnreadCount);

        // Add a 3rd account
        AccountSession? addedSession = null;
        manager.SessionAdded += s => addedSession = s;

        var p3 = new AccountProfile
        {
            Jid = "hobby@example.com",
            Password = "pass3",
            Label = "Hobby",
            ColorHex = "#10B981",
            IsActive = true
        };
        var newSession = await manager.AddAccountAsync(p3, autoConnect: false);
        Assert.Equal(3, manager.Sessions.Count);
        Assert.Same(newSession, addedSession);

        // Remove the 2nd account
        AccountSession? removedSession = null;
        manager.SessionRemoved += s => removedSession = s;

        await manager.RemoveAccountAsync("work@example.com");
        Assert.Equal(2, manager.Sessions.Count);
        Assert.NotNull(removedSession);
        Assert.Equal("work@example.com", removedSession.AccountJid);

        var dbAccounts = await _accountRepo.GetAccountsAsync();
        Assert.DoesNotContain(dbAccounts, a => a.Jid == "work@example.com");
    }

    [Fact]
    public async Task AccountSessionManager_ChangePassword_PersistsOnlyAfterServerSuccess()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport);
        server.Start();

        var profile = new AccountProfile
        {
            Jid = "alice@mock.example.com",
            Password = "old-password",
            IsActive = true
        };
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse(profile.Jid),
            Password = profile.Password
        }, transport);
        var manager = new AccountSessionManager(_dbContext, _accountRepo);
        var session = await manager.AddAccountAsync(profile, client, autoConnect: false);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ChangePasswordAsync(profile.Jid, "new-password"));
            Assert.Equal("old-password", (await _accountRepo.GetAccountAsync(profile.Jid))?.Password);

            Assert.True(await session.ConnectAsync());
            var settings = new SettingsViewModel(
                new SettingsRepository(_dbContext),
                profile.Jid,
                sessionManager: manager,
                accountRepo: _accountRepo);
            settings.OpenChangePasswordDialog(new AccountProfileViewModel(profile, session));
            settings.NewAccountPassword = "new-password";
            settings.ConfirmAccountPassword = "different-password";
            await settings.ChangeAccountPasswordAsync();

            Assert.True(settings.IsAccountPasswordStatusError);
            Assert.Equal("old-password", (await _accountRepo.GetAccountAsync(profile.Jid))?.Password);

            settings.ConfirmAccountPassword = "new-password";
            string? serverAcceptedPassword = null;
            server.OnIqReceived += iq =>
            {
                if (iq.RawElement.Element("query", Xep0077InBandRegistration.NsRegister) is { } query)
                {
                    serverAcceptedPassword = query.Element("password")?.Value;
                }
            };

            await settings.ChangeAccountPasswordAsync();

            Assert.False(settings.IsAccountPasswordStatusError);
            Assert.Equal("new-password", serverAcceptedPassword);
            Assert.Equal("new-password", (await _accountRepo.GetAccountAsync(profile.Jid))?.Password);
            Assert.Equal("new-password", session.Profile.Password);
            Assert.Equal("new-password", client.Options.Password);
        }
        finally
        {
            await manager.DisposeAsync();
        }
    }

    [Fact]
    public async Task AddAccountDialogViewModel_ValidationAndSubmission()
    {
        var added = false;
        AccountProfile? submittedProfile = null;
        var closed = false;

        var vm = new AddAccountDialogViewModel(
            profile =>
            {
                added = true;
                submittedProfile = profile;
                return Task.FromResult((true, (string?)null));
            },
            () => closed = true
        );

        // Empty JID validation
        await vm.AddAccountAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(added);

        // Invalid JID validation
        vm.Jid = "not-a-valid-jid@@@";
        vm.Password = "pass";
        await vm.AddAccountAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(added);

        // Missing password validation
        vm.Jid = "valid@example.com";
        vm.Password = "";
        await vm.AddAccountAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(added);

        // Invalid port validation
        vm.Password = "secret";
        vm.Port = "abc";
        await vm.AddAccountAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(added);

        // Valid submission
        vm.Port = "5222";
        vm.Label = "Consulting";
        vm.ColorHex = "#8B5CF6";
        await vm.AddAccountAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.True(added);
        Assert.NotNull(submittedProfile);
        Assert.Equal("valid@example.com", submittedProfile.Jid);
        Assert.Equal("secret", submittedProfile.Password);
        Assert.Equal("Consulting", submittedProfile.Label);
        Assert.Equal("#8B5CF6", submittedProfile.ColorHex);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task MainChatViewModel_MultiAccount_UnifiedAndFilteredView()
    {
        var p1 = new AccountProfile
        {
            Jid = "alice@example.com",
            Password = "pass1",
            Label = "Work",
            ColorHex = "#3B82F6",
            IsActive = true
        };
        var p2 = new AccountProfile
        {
            Jid = "bob@example.com",
            Password = "pass2",
            Label = "Personal",
            ColorHex = "#10B981",
            IsActive = true
        };
        await _accountRepo.SaveAccountAsync(p1);
        await _accountRepo.SaveAccountAsync(p2);

        var manager = new AccountSessionManager(_dbContext, _accountRepo);
        await manager.InitializeAsync(autoConnect: false);

        var chatVm = new MainChatViewModel(manager, _dbContext, () => Task.CompletedTask);
        await chatVm.InitializeAsync();

        var sessionAlice = manager.GetSession("alice@example.com")!;
        var sessionBob = manager.GetSession("bob@example.com")!;

        // Add contact for Alice
        var contactAlice = new ContactItemViewModel
        {
            AccountJid = "alice@example.com",
            AccountLabel = "Work",
            AccountColorHex = "#3B82F6",
            ContactJid = "colleague@example.com",
            Name = "Colleague"
        };
        // Add contact for Bob
        var contactBob = new ContactItemViewModel
        {
            AccountJid = "bob@example.com",
            AccountLabel = "Personal",
            AccountColorHex = "#10B981",
            ContactJid = "friend@example.com",
            Name = "Friend"
        };

        // Add to chatVm
        chatVm.Contacts.Add(contactAlice);
        chatVm.Contacts.Add(contactBob);

        // Add conversations for each
        var convAlice = chatVm.GetOrCreateConversation("alice@example.com", "colleague@example.com", "Colleague", Jid.Parse("colleague@example.com"), false);
        var convBob = chatVm.GetOrCreateConversation("bob@example.com", "friend@example.com", "Friend", Jid.Parse("friend@example.com"), false);

        // 1. Initial State: All Accounts selected
        chatVm.SelectAllAccounts();
        Assert.True(chatVm.IsAllAccountsSelected);
        Assert.Null(chatVm.SelectedAccountSession);
        Assert.Contains(convAlice, chatVm.Conversations);
        Assert.Contains(convBob, chatVm.Conversations);
        Assert.True(convAlice.ShowAccountBadge);
        Assert.True(convBob.ShowAccountBadge);

        // 2. Select Alice's account: Bob's conversation should be filtered out
        chatVm.SelectAccountSession(sessionAlice);
        Assert.False(chatVm.IsAllAccountsSelected);
        Assert.Same(sessionAlice, chatVm.SelectedAccountSession);
        Assert.Contains(convAlice, chatVm.Conversations);
        Assert.DoesNotContain(convBob, chatVm.Conversations);
        Assert.False(convAlice.ShowAccountBadge);

        // 3. Select Bob's account: Alice's conversation should be filtered out
        chatVm.SelectAccountSession(sessionBob);
        Assert.False(chatVm.IsAllAccountsSelected);
        Assert.Same(sessionBob, chatVm.SelectedAccountSession);
        Assert.Contains(convBob, chatVm.Conversations);
        Assert.DoesNotContain(convAlice, chatVm.Conversations);
        Assert.False(convBob.ShowAccountBadge);

        // 4. Back to All Accounts
        chatVm.SelectAllAccounts();
        Assert.True(chatVm.IsAllAccountsSelected);
        Assert.Contains(convAlice, chatVm.Conversations);
        Assert.Contains(convBob, chatVm.Conversations);
        Assert.True(convAlice.ShowAccountBadge);
        Assert.True(convBob.ShowAccountBadge);
    }

    [Fact]
    public async Task MainChatViewModel_SetPresence_BroadcastsToAllSessions()
    {
        var p1 = new AccountProfile
        {
            Jid = "alice@example.com",
            Password = "pass1",
            Label = "Work",
            IsActive = true
        };
        var p2 = new AccountProfile
        {
            Jid = "bob@example.com",
            Password = "pass2",
            Label = "Personal",
            IsActive = true
        };
        await _accountRepo.SaveAccountAsync(p1);
        await _accountRepo.SaveAccountAsync(p2);

        var manager = new AccountSessionManager(_dbContext, _accountRepo);
        await manager.InitializeAsync(autoConnect: false);

        var chatVm = new MainChatViewModel(manager, _dbContext, () => Task.CompletedTask);
        await chatVm.InitializeAsync();

        // Broadcast presence change
        chatVm.StatusMessage = "In a meeting";
        await chatVm.SetPresenceAsync("dnd");

        Assert.Equal("dnd", chatVm.UserPresence);
        Assert.Equal("In a meeting", chatVm.StatusMessage);

        foreach (var s in manager.Sessions)
        {
            Assert.Equal("dnd", s.PresenceShow);
            Assert.Equal("In a meeting", s.StatusMessage);
        }
    }

    [AvaloniaFact]
    public async Task MainChatViewModel_Search_ScopesToSelectedAccountAndAllAccounts()
    {
        var p1 = new AccountProfile
        {
            Jid = "alice@example.com",
            Password = "pass1",
            Label = "Work",
            ColorHex = "#3B82F6",
            IsActive = true
        };
        var p2 = new AccountProfile
        {
            Jid = "bob@example.com",
            Password = "pass2",
            Label = "Personal",
            ColorHex = "#10B981",
            IsActive = true
        };
        await _accountRepo.SaveAccountAsync(p1);
        await _accountRepo.SaveAccountAsync(p2);

        var msgRepo = new MessageRepository(_dbContext);
        await msgRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = "alice@example.com",
            RemoteJid = "colleague@example.com",
            SenderJid = "colleague@example.com",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            Direction = MessageDirection.Inbound,
            Body = "Quarterly budget review for Alice"
        });
        await msgRepo.SaveMessageAsync(new ChatMessage
        {
            AccountJid = "bob@example.com",
            RemoteJid = "friend@example.com",
            SenderJid = "friend@example.com",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Direction = MessageDirection.Inbound,
            Body = "Holiday budget trip for Bob"
        });

        var manager = new AccountSessionManager(_dbContext, _accountRepo);
        await manager.InitializeAsync(autoConnect: false);

        var chatVm = new MainChatViewModel(manager, _dbContext, () => Task.CompletedTask);
        await chatVm.InitializeAsync();

        var sessionAlice = manager.GetSession("alice@example.com")!;
        var sessionBob = manager.GetSession("bob@example.com")!;

        // 1. In Alice's account view: search returns only Alice's messages
        chatVm.SelectAccountSession(sessionAlice);
        chatVm.SearchQuery = "budget";
        await chatVm.ExecuteSearchAsync();

        Assert.True(chatVm.IsSearching);
        Assert.Single(chatVm.SearchResults);
        Assert.Equal("alice@example.com", chatVm.SearchResults[0].AccountJid);
        Assert.Contains("for Alice", chatVm.SearchResults[0].Body);
        Assert.False(chatVm.SearchResults[0].ShowAccountBadge);

        // 2. In Bob's account view: search returns only Bob's messages
        chatVm.SelectAccountSession(sessionBob);
        chatVm.SearchQuery = "budget";
        await chatVm.ExecuteSearchAsync();

        Assert.True(chatVm.IsSearching);
        Assert.Single(chatVm.SearchResults);
        Assert.Equal("bob@example.com", chatVm.SearchResults[0].AccountJid);
        Assert.Contains("for Bob", chatVm.SearchResults[0].Body);
        Assert.False(chatVm.SearchResults[0].ShowAccountBadge);

        // 3. In All Accounts view: search returns both messages with account badges
        chatVm.SelectAllAccounts();
        chatVm.SearchQuery = "budget";
        await chatVm.ExecuteSearchAsync();

        Assert.True(chatVm.IsSearching);
        Assert.Equal(2, chatVm.SearchResults.Count);
        Assert.All(chatVm.SearchResults, r => Assert.True(r.ShowAccountBadge));
        Assert.Contains(chatVm.SearchResults, r => r.AccountJid == "alice@example.com" && r.Body.Contains("for Alice"));
        Assert.Contains(chatVm.SearchResults, r => r.AccountJid == "bob@example.com" && r.Body.Contains("for Bob"));

        // 4. Select a search result belonging to Bob: opens conversation in Bob's account
        var bobResult = chatVm.SearchResults.First(r => r.AccountJid == "bob@example.com");
        await chatVm.SelectSearchResultAsync(bobResult);

        Assert.False(chatVm.IsSearching);
        Assert.NotNull(chatVm.ActiveConversation);
        Assert.Equal("bob@example.com", chatVm.ActiveConversation.AccountJid);
        Assert.Equal("friend@example.com", chatVm.ActiveConversation.RemoteJid.ToString());

        // 5. Select Alice's account view, then select search result for Bob -> switches account to Bob
        chatVm.SelectAccountSession(sessionAlice);
        chatVm.SearchQuery = "budget";
        await chatVm.ExecuteSearchAsync();

        // Simulate choosing a result that belongs to Bob
        await chatVm.SelectSearchResultAsync(bobResult);

        Assert.False(chatVm.IsSearching);
        Assert.Same(sessionBob, chatVm.SelectedAccountSession);
        Assert.NotNull(chatVm.ActiveConversation);
        Assert.Equal("bob@example.com", chatVm.ActiveConversation.AccountJid);
        Assert.Equal("friend@example.com", chatVm.ActiveConversation.RemoteJid.ToString());
    }
}
