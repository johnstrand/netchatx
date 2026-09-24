using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class MessageMergingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;
    private readonly SettingsRepository _settingsRepo;
    private readonly AccountRepository _accountRepo;
    private readonly RosterRepository _rosterRepo;
    private readonly OmemoRepository _omemoRepo;

    public MessageMergingTests()
    {
        _dbPath = $"test_merging_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
        _settingsRepo = new SettingsRepository(_dbContext);
        _accountRepo = new AccountRepository(_dbContext);
        _rosterRepo = new RosterRepository(_dbContext);
        _omemoRepo = new OmemoRepository(_dbContext);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void CanMergeWith_DirectionMatching_EnforcesSameDirectionAndSender()
    {
        var now = DateTimeOffset.UtcNow;
        var bubble = new MessageBubbleViewModel
        {
            Id = "msg1",
            Body = "First",
            Direction = MessageDirection.Outbound,
            SenderName = "Me",
            Timestamp = now,
            LatestTimestamp = now
        };

        var inboundMsg = new ChatMessage
        {
            Id = "msg2",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "peer@example.com",
            Body = "Second",
            Direction = MessageDirection.Inbound,
            Timestamp = now.AddSeconds(2)
        };

        // Outbound bubble cannot merge with Inbound message
        Assert.False(bubble.CanMergeWith(inboundMsg, enableMerging: true, thresholdSeconds: 10));

        // Outbound bubble can merge with Outbound message
        var outboundMsg = new ChatMessage
        {
            Id = "msg3",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Third",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(2)
        };
        Assert.True(bubble.CanMergeWith(outboundMsg, enableMerging: true, thresholdSeconds: 10));

        // Inbound bubble with sender peer1 cannot merge with sender peer2
        var inboundBubble = new MessageBubbleViewModel
        {
            Id = "msg_in_1",
            Body = "Inbound 1",
            Direction = MessageDirection.Inbound,
            SenderName = "peer1@example.com",
            Timestamp = now,
            LatestTimestamp = now
        };
        var peer2Msg = new ChatMessage
        {
            Id = "msg_in_2",
            AccountJid = "me@example.com",
            RemoteJid = "room@conference.example.com",
            SenderJid = "peer2@example.com",
            Body = "Inbound from peer 2",
            Direction = MessageDirection.Inbound,
            Timestamp = now.AddSeconds(2)
        };
        Assert.False(inboundBubble.CanMergeWith(peer2Msg, enableMerging: true, thresholdSeconds: 10));
    }

    [Fact]
    public void CanMergeWith_ThresholdSeconds_RespectsWindowAndDisableToggle()
    {
        var now = DateTimeOffset.UtcNow;
        var bubble = new MessageBubbleViewModel
        {
            Id = "msg1",
            Body = "First",
            Direction = MessageDirection.Outbound,
            SenderName = "Me",
            Timestamp = now,
            LatestTimestamp = now
        };

        var withinThreshold = new ChatMessage
        {
            Id = "msg2",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Within threshold",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(9)
        };

        var exceedsThreshold = new ChatMessage
        {
            Id = "msg3",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Exceeds threshold",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(15)
        };

        // When enabled with 10s threshold
        Assert.True(bubble.CanMergeWith(withinThreshold, enableMerging: true, thresholdSeconds: 10));
        Assert.False(bubble.CanMergeWith(exceedsThreshold, enableMerging: true, thresholdSeconds: 10));

        // When disabled
        Assert.False(bubble.CanMergeWith(withinThreshold, enableMerging: false, thresholdSeconds: 10));

        // When threshold is 0
        Assert.False(bubble.CanMergeWith(withinThreshold, enableMerging: true, thresholdSeconds: 0));
    }

    [Fact]
    public void CanMergeWith_DifferentCalendarDays_RejectsMerging()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var bubble = new MessageBubbleViewModel
        {
            Id = "msg1",
            Body = "First",
            Direction = MessageDirection.Outbound,
            SenderName = "Me",
            Timestamp = yesterday,
            LatestTimestamp = yesterday
        };

        var today = DateTimeOffset.UtcNow;
        var todayMsg = new ChatMessage
        {
            Id = "msg2",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Today",
            Direction = MessageDirection.Outbound,
            Timestamp = today
        };

        Assert.False(bubble.CanMergeWith(todayMsg, enableMerging: true, thresholdSeconds: 86400));
    }

    [Fact]
    public void CanMergeWith_ActionMessage_RejectsMerging()
    {
        var now = DateTimeOffset.UtcNow;
        var actionBubble = new MessageBubbleViewModel
        {
            Id = "msg1",
            Body = "_**User** dances_",
            IsActionMessage = true,
            Direction = MessageDirection.Outbound,
            SenderName = "Me",
            Timestamp = now,
            LatestTimestamp = now
        };

        var normalMsg = new ChatMessage
        {
            Id = "msg2",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Normal message",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(2)
        };

        var actionMsg = new ChatMessage
        {
            Id = "msg3",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "/me sings",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(2)
        };

        var normalBubble = new MessageBubbleViewModel
        {
            Id = "msg4",
            Body = "Normal bubble",
            Direction = MessageDirection.Outbound,
            SenderName = "Me",
            Timestamp = now,
            LatestTimestamp = now
        };

        // Action bubble cannot merge with normal message
        Assert.False(actionBubble.CanMergeWith(normalMsg, enableMerging: true, thresholdSeconds: 10));

        // Normal bubble cannot merge with incoming action message
        Assert.False(normalBubble.CanMergeWith(actionMsg, enableMerging: true, thresholdSeconds: 10));
    }

    [Fact]
    public void MergeMessage_UpdatesBodyAndTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "msg1",
            StanzaId = "stanza1",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "First line",
            Direction = MessageDirection.Outbound,
            Timestamp = now,
            IsRead = true
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg1);
        Assert.Equal("First line", bubble.Body);
        Assert.Single(bubble.MergedMessages);
        Assert.True(bubble.ContainsMessageId("msg1"));
        Assert.True(bubble.ContainsMessageId("stanza1"));

        var msg2 = new ChatMessage
        {
            Id = "msg2",
            OriginId = "origin2",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Second line",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(3),
            IsRead = true
        };

        bubble.MergeMessage(msg2);

        Assert.Equal("First line\nSecond line", bubble.Body);
        Assert.Equal(2, bubble.MergedMessages.Count);
        Assert.Equal(msg2.Timestamp, bubble.LatestTimestamp);
        Assert.Equal(now, bubble.Timestamp);
        Assert.True(bubble.ContainsMessageId("msg2"));
        Assert.True(bubble.ContainsMessageId("origin2"));
    }

    [Fact]
    public void RemoveMessageById_UpdatesRemainingMergedBubbleOrClears()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "id1",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Part A",
            Direction = MessageDirection.Outbound,
            Timestamp = now,
            IsRead = true
        };
        var msg2 = new ChatMessage
        {
            Id = "id2",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Part B",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(2),
            IsRead = true
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg1);
        bubble.MergeMessage(msg2);
        Assert.Equal(2, bubble.MergedMessages.Count);
        Assert.Equal("Part A\nPart B", bubble.Body);

        // Remove msg1
        var removed = bubble.RemoveMessageById("id1");
        Assert.True(removed);
        Assert.Single(bubble.MergedMessages);
        Assert.Equal("Part B", bubble.Body);
        Assert.False(bubble.ContainsMessageId("id1"));
        Assert.True(bubble.ContainsMessageId("id2"));

        // Remove msg2
        var removedSecond = bubble.RemoveMessageById("id2");
        Assert.True(removedSecond);
        Assert.Empty(bubble.MergedMessages);
        Assert.False(bubble.ContainsMessageId("id2"));
    }

    [Fact]
    public void ChatConversationViewModel_AddOrUpdateMessage_MergesConsecutiveWithinThreshold()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            settingsRepo: _settingsRepo)
        {
            EnableMessageMerging = true,
            MessageMergeThresholdSeconds = 10
        };

        var t0 = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Hello",
            Timestamp = t0,
            Direction = MessageDirection.Outbound
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "World",
            Timestamp = t0.AddSeconds(4),
            Direction = MessageDirection.Outbound
        };

        conv.AddOrUpdateMessage(msg1);
        conv.AddOrUpdateMessage(msg2);

        Assert.Single(conv.Messages);
        var bubble = conv.Messages[0];
        Assert.Equal("Hello\nWorld", bubble.Body);
        Assert.Equal(2, bubble.MergedMessages.Count);
    }

    [Fact]
    public void ChatConversationViewModel_AddOrUpdateMessage_SeparateBubblesWhenExceedingThreshold()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            settingsRepo: _settingsRepo)
        {
            EnableMessageMerging = true,
            MessageMergeThresholdSeconds = 5
        };

        var t0 = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "First",
            Timestamp = t0,
            Direction = MessageDirection.Outbound
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Second (after 6 seconds)",
            Timestamp = t0.AddSeconds(6),
            Direction = MessageDirection.Outbound
        };

        conv.AddOrUpdateMessage(msg1);
        conv.AddOrUpdateMessage(msg2);

        Assert.Equal(2, conv.Messages.Count);
        Assert.Equal("First", conv.Messages[0].Body);
        Assert.Equal("Second (after 6 seconds)", conv.Messages[1].Body);
    }

    [Fact]
    public void ChatConversationViewModel_AddOrUpdateMessage_SeparateBubblesWhenDisabled()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            settingsRepo: _settingsRepo)
        {
            EnableMessageMerging = false,
            MessageMergeThresholdSeconds = 10
        };

        var t0 = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "First",
            Timestamp = t0,
            Direction = MessageDirection.Outbound
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Second",
            Timestamp = t0.AddSeconds(1),
            Direction = MessageDirection.Outbound
        };

        conv.AddOrUpdateMessage(msg1);
        conv.AddOrUpdateMessage(msg2);

        Assert.Equal(2, conv.Messages.Count);
        Assert.Equal("First", conv.Messages[0].Body);
        Assert.Equal("Second", conv.Messages[1].Body);
    }

    [Fact]
    public void ChatConversationViewModel_TogglingMergeSetting_RebuildsExistingBubbles()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            settingsRepo: _settingsRepo)
        {
            EnableMessageMerging = false,
            MessageMergeThresholdSeconds = 10
        };

        var t0 = DateTimeOffset.UtcNow;
        conv.AddOrUpdateMessage(new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Bubble A",
            Timestamp = t0,
            Direction = MessageDirection.Outbound
        });
        conv.AddOrUpdateMessage(new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Bubble B",
            Timestamp = t0.AddSeconds(3),
            Direction = MessageDirection.Outbound
        });

        // With merging disabled: 2 bubbles
        Assert.Equal(2, conv.Messages.Count);

        // Turn merging on: should automatically rebuild into 1 bubble
        conv.EnableMessageMerging = true;
        Assert.Single(conv.Messages);
        Assert.Equal("Bubble A\nBubble B", conv.Messages[0].Body);

        // Change threshold to 2s: 3s diff > 2s threshold, so rebuilds into 2 bubbles
        conv.MessageMergeThresholdSeconds = 2;
        Assert.Equal(2, conv.Messages.Count);
        Assert.Equal("Bubble A", conv.Messages[0].Body);
        Assert.Equal("Bubble B", conv.Messages[1].Body);
    }

    [Fact]
    public void ChatConversationViewModel_RetractingMergedMessage_UpdatesOrRemovesBubble()
    {
        var account = "user@test.org";
        var remote = Jid.Parse("peer@test.org");

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo,
            settingsRepo: _settingsRepo)
        {
            EnableMessageMerging = true,
            MessageMergeThresholdSeconds = 10
        };

        var t0 = DateTimeOffset.UtcNow;
        conv.AddOrUpdateMessage(new ChatMessage
        {
            Id = "m1",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "First",
            Timestamp = t0,
            Direction = MessageDirection.Outbound
        });
        conv.AddOrUpdateMessage(new ChatMessage
        {
            Id = "m2",
            AccountJid = account,
            RemoteJid = remote.ToString(),
            SenderJid = account,
            Body = "Second",
            Timestamp = t0.AddSeconds(2),
            Direction = MessageDirection.Outbound
        });

        Assert.Single(conv.Messages);
        Assert.Equal("First\nSecond", conv.Messages[0].Body);

        // Retract m2: bubble remains, only "First" is left
        conv.HandleIncomingRetraction("m2");
        Assert.Single(conv.Messages);
        Assert.Equal("First", conv.Messages[0].Body);

        // Retract m1: bubble is removed completely
        conv.HandleIncomingRetraction("m1");
        Assert.Empty(conv.Messages);
    }

    [Fact]
    public async Task SettingsRepository_MergeMessagesPreferences_PersistAndRead()
    {
        var account = "user_pref@test.org";

        // Defaults
        var defaultEnabled = await _settingsRepo.GetMergeMessagesEnabledAsync(account);
        var defaultThreshold = await _settingsRepo.GetMergeMessagesThresholdSecondsAsync(account);
        Assert.True(defaultEnabled);
        Assert.Equal(10, defaultThreshold);

        // Update
        await _settingsRepo.SetMergeMessagesEnabledAsync(account, false);
        await _settingsRepo.SetMergeMessagesThresholdSecondsAsync(account, 25);

        var updatedEnabled = await _settingsRepo.GetMergeMessagesEnabledAsync(account);
        var updatedThreshold = await _settingsRepo.GetMergeMessagesThresholdSecondsAsync(account);
        Assert.False(updatedEnabled);
        Assert.Equal(25, updatedThreshold);
    }

    [Fact]
    public void MainChatViewModel_MergeSettingsChange_PropagatesToConversations()
    {
        var account = "user@test.org";
        var options = new XmppClientOptions
        {
            Jid = Jid.Parse(account),
            Password = "pw"
        };
        var transport = new LoopbackTransport();
        var client = new XmppClient(options, transport);
        var mainVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        var remote = Jid.Parse("contact@test.org");
        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Contact",
            remote,
            isGroupChat: false,
            _messageRepo,
            settingsRepo: _settingsRepo);

        mainVm.Conversations.Add(conv);

        // Update setting on MainChatViewModel
        mainVm.EnableMessageMerging = false;
        Assert.False(conv.EnableMessageMerging);

        mainVm.MessageMergeThresholdSeconds = 42;
        Assert.Equal(42, conv.MessageMergeThresholdSeconds);
    }

    [Fact]
    public void MessageBubbleViewModel_MergeMessage_ConcatenatesRawXmlAcrossAllMergedMessages()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = "user@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "user@test.org",
            Body = "First message",
            Direction = MessageDirection.Outbound,
            Timestamp = now,
            RawXml = "<message id='m1'><body>First message</body></message>"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = "user@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "user@test.org",
            Body = "Second message",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(2),
            RawXml = "<message id='m2'><body>Second message</body></message>"
        };
        var msg3 = new ChatMessage
        {
            Id = "m3",
            AccountJid = "user@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "user@test.org",
            Body = "Third message",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(4),
            RawXml = "<message id='m3'><body>Third message</body></message>"
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg1);
        Assert.Equal("<message id='m1'><body>First message</body></message>", bubble.RawXml);

        bubble.MergeMessage(msg2);
        var expectedTwo = "<message id='m1'><body>First message</body></message>\n\n<message id='m2'><body>Second message</body></message>";
        Assert.Equal(expectedTwo, bubble.RawXml);

        bubble.MergeMessage(msg3);
        var expectedThree = "<message id='m1'><body>First message</body></message>\n\n<message id='m2'><body>Second message</body></message>\n\n<message id='m3'><body>Third message</body></message>";
        Assert.Equal(expectedThree, bubble.RawXml);

        // ToggleRawXml ensures RawXml is populated and toggles visibility
        bubble.ToggleRawXml();
        Assert.True(bubble.IsRawXmlVisible);
        Assert.Equal(expectedThree, bubble.RawXml);

        bubble.ToggleRawXml();
        Assert.False(bubble.IsRawXmlVisible);
    }

    [Fact]
    public void MessageBubbleViewModel_RemoveAndContentUpdate_KeepsRawXmlConsistent()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = new ChatMessage
        {
            Id = "m1",
            AccountJid = "user@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "user@test.org",
            Body = "Original 1",
            Direction = MessageDirection.Outbound,
            Timestamp = now,
            RawXml = "<message id='m1'><body>Original 1</body></message>"
        };
        var msg2 = new ChatMessage
        {
            Id = "m2",
            AccountJid = "user@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "user@test.org",
            Body = "Original 2",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(2),
            RawXml = "<message id='m2'><body>Original 2</body></message>"
        };

        var bubble = MessageBubbleViewModel.FromChatMessage(msg1);
        bubble.MergeMessage(msg2);

        // Edit/correct m1
        bubble.UpdateMessageContent("m1", "Corrected 1", "<message id='m1'><replace id='m1'/><body>Corrected 1</body></message>");
        Assert.Equal("Corrected 1\nOriginal 2", bubble.Body);
        var expectedCorrected = "<message id='m1'><replace id='m1'/><body>Corrected 1</body></message>\n\n<message id='m2'><body>Original 2</body></message>";
        Assert.Equal(expectedCorrected, bubble.RawXml);

        // Remove m1 (retraction)
        var removed = bubble.RemoveMessageById("m1");
        Assert.True(removed);
        Assert.Equal("Original 2", bubble.Body);
        Assert.Equal("<message id='m2'><body>Original 2</body></message>", bubble.RawXml);
    }

    [Fact]
    public void MessageBubbleViewModel_MergedTextMessageAndImageMessage_HidesImageUrlInDisplayText()
    {
        var now = DateTimeOffset.UtcNow;
        var textMsg = new ChatMessage
        {
            Id = "msg1",
            AccountJid = "john@squishythoughts.com",
            RemoteJid = "richard@squishythoughts.com",
            SenderJid = "john@squishythoughts.com/Stanza",
            Body = "Daemon fanns inte på storytel, men vem behöver det när man har",
            Direction = MessageDirection.Outbound,
            Timestamp = now
        };

        var imgMsg = new ChatMessage
        {
            Id = "msg2",
            AccountJid = "john@squishythoughts.com",
            RemoteJid = "richard@squishythoughts.com",
            SenderJid = "john@squishythoughts.com/Stanza",
            Body = "https://chat.squishythoughts.com/upload/8419fdbf-fe77-4a92-941e-a913f8fe72b4/image_20260921_124307.png",
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddSeconds(1),
            RawXml = "<message xmlns='jabber:client'><x xmlns='jabber:x:oob'><url>https://chat.squishythoughts.com/upload/8419fdbf-fe77-4a92-941e-a913f8fe72b4/image_20260921_124307.png</url></x><body>https://chat.squishythoughts.com/upload/8419fdbf-fe77-4a92-941e-a913f8fe72b4/image_20260921_124307.png</body></message>"
        };

        // Previews enabled (default)
        MessageBubbleViewModel.ShowInlinePreviews = true;

        var bubble = MessageBubbleViewModel.FromChatMessage(textMsg);
        Assert.Equal("Daemon fanns inte på storytel, men vem behöver det när man har", bubble.DisplayText);
        Assert.False(bubble.HasImage);

        bubble.MergeMessage(imgMsg);

        Assert.True(bubble.HasImage);
        Assert.Equal("https://chat.squishythoughts.com/upload/8419fdbf-fe77-4a92-941e-a913f8fe72b4/image_20260921_124307.png", bubble.ImageUrl);
        Assert.Equal("Daemon fanns inte på storytel, men vem behöver det när man har", bubble.DisplayText);
        Assert.False(bubble.IsOnlyImage);
        Assert.True(bubble.IsPreviewVisible);

        // Toggle previews disabled -> DisplayText should return to full Body including the image URL
        MessageBubbleViewModel.ShowInlinePreviews = false;
        bubble.RefreshPreviewVisibility();

        Assert.Equal("Daemon fanns inte på storytel, men vem behöver det när man har\nhttps://chat.squishythoughts.com/upload/8419fdbf-fe77-4a92-941e-a913f8fe72b4/image_20260921_124307.png", bubble.DisplayText);
        Assert.False(bubble.IsPreviewVisible);

        // Reset static setting
        MessageBubbleViewModel.ShowInlinePreviews = true;
    }

    [Fact]
    public void MergeMessage_ImageArrivesBeforeTextMessage_KeepsImageOnBottom()
    {
        var now = DateTimeOffset.UtcNow;
        var imgMsg = new ChatMessage
        {
            Id = "msg_img_1",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "https://example.com/cat.png",
            Direction = MessageDirection.Inbound,
            Timestamp = now,
            RawXml = "<message xmlns='jabber:client'><x xmlns='jabber:x:oob'><url>https://example.com/cat.png</url></x><body>https://example.com/cat.png</body></message>"
        };

        var textMsg = new ChatMessage
        {
            Id = "msg_txt_2",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "Look at this cute cat!",
            Direction = MessageDirection.Inbound,
            Timestamp = now.AddSeconds(1)
        };

        MessageBubbleViewModel.ShowInlinePreviews = true;

        // Image arrives first
        var bubble = MessageBubbleViewModel.FromChatMessage(imgMsg);
        Assert.True(bubble.HasImage);
        Assert.True(bubble.IsOnlyImage);

        // Text arrives second and merges
        bubble.MergeMessage(textMsg);

        // Image must be kept on the bottom regardless of arrival order
        Assert.True(bubble.HasImage);
        Assert.Equal("https://example.com/cat.png", bubble.ImageUrl);
        Assert.Equal("Look at this cute cat!", bubble.DisplayText);
        Assert.Equal("Look at this cute cat!\nhttps://example.com/cat.png", bubble.Body);
        Assert.False(bubble.IsOnlyImage);
    }

    [Fact]
    public void MergeMessage_TextArrivesBeforeImageMessage_KeepsImageOnBottom()
    {
        var now = DateTimeOffset.UtcNow;
        var textMsg = new ChatMessage
        {
            Id = "msg_txt_1",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "Look at this cute cat!",
            Direction = MessageDirection.Inbound,
            Timestamp = now
        };

        var imgMsg = new ChatMessage
        {
            Id = "msg_img_2",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "https://example.com/cat.png",
            Direction = MessageDirection.Inbound,
            Timestamp = now.AddSeconds(1),
            RawXml = "<message xmlns='jabber:client'><x xmlns='jabber:x:oob'><url>https://example.com/cat.png</url></x><body>https://example.com/cat.png</body></message>"
        };

        MessageBubbleViewModel.ShowInlinePreviews = true;

        // Text arrives first
        var bubble = MessageBubbleViewModel.FromChatMessage(textMsg);
        Assert.False(bubble.HasImage);
        Assert.Equal("Look at this cute cat!", bubble.DisplayText);

        // Image arrives second and merges
        bubble.MergeMessage(imgMsg);

        // Image must be kept on the bottom
        Assert.True(bubble.HasImage);
        Assert.True(bubble.IsPreviewVisible);
        Assert.Equal("https://example.com/cat.png", bubble.ImageUrl);
        Assert.Equal("Look at this cute cat!", bubble.DisplayText);
        Assert.Equal("Look at this cute cat!\nhttps://example.com/cat.png", bubble.Body);
        Assert.False(bubble.IsOnlyImage);
    }

    [Fact]
    public void MergeMessage_MultipleInterleavedTextAndImage_KeepsImageAtBottom()
    {
        var now = DateTimeOffset.UtcNow;
        var textMsg1 = new ChatMessage
        {
            Id = "msg_txt_1",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "First line of text",
            Direction = MessageDirection.Inbound,
            Timestamp = now
        };

        var imgMsg = new ChatMessage
        {
            Id = "msg_img_2",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "https://example.com/photo.jpg",
            Direction = MessageDirection.Inbound,
            Timestamp = now.AddSeconds(1)
        };

        var textMsg2 = new ChatMessage
        {
            Id = "msg_txt_3",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "Second line of text",
            Direction = MessageDirection.Inbound,
            Timestamp = now.AddSeconds(2)
        };

        MessageBubbleViewModel.ShowInlinePreviews = true;

        var bubble = MessageBubbleViewModel.FromChatMessage(textMsg1);
        bubble.MergeMessage(imgMsg);
        bubble.MergeMessage(textMsg2);

        // Both text messages should precede the image URL
        Assert.Equal("First line of text\nSecond line of text\nhttps://example.com/photo.jpg", bubble.Body);
        Assert.Equal("First line of text\nSecond line of text", bubble.DisplayText);
        Assert.Equal("https://example.com/photo.jpg", bubble.ImageUrl);
    }

    [Fact]
    public void MessageBubbleViewModel_FromChatMessage_SingleMessageWithImageOnTop_RearrangesImageToBottom()
    {
        var now = DateTimeOffset.UtcNow;
        var msg = new ChatMessage
        {
            Id = "msg_single",
            AccountJid = "john@example.com",
            RemoteJid = "richard@example.com",
            SenderJid = "richard@example.com",
            Body = "https://example.com/photo.jpg\nCaption text below",
            Direction = MessageDirection.Inbound,
            Timestamp = now
        };

        MessageBubbleViewModel.ShowInlinePreviews = true;

        var bubble = MessageBubbleViewModel.FromChatMessage(msg);

        // Body must ensure image is at the bottom
        Assert.Equal("Caption text below\nhttps://example.com/photo.jpg", bubble.Body);
        Assert.Equal("Caption text below", bubble.DisplayText);
        Assert.Equal("https://example.com/photo.jpg", bubble.ImageUrl);
        Assert.True(bubble.HasImage);
    }

    [AvaloniaFact]
    public async Task MergeMessage_ImagePrecachingAndPreviewVisibility_RendersProperlyOnSend()
    {
        var sampleBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var imageUrl = "https://upload.example.com/share/test_paste.png";

        // Precache the image as would happen when pasting/uploading an image
        Stanza.Gui.Helpers.AsyncImageLoader.PrecacheImage(imageUrl, sampleBytes);

        var now = DateTimeOffset.UtcNow;
        var textMsg = new ChatMessage
        {
            Id = "msg_txt_out",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = "Here is the screenshot",
            Direction = MessageDirection.Outbound,
            Timestamp = now
        };

        var imgMsg = new ChatMessage
        {
            Id = "msg_img_out",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "me@example.com",
            Body = imageUrl,
            Direction = MessageDirection.Outbound,
            Timestamp = now.AddMilliseconds(10)
        };

        MessageBubbleViewModel.ShowInlinePreviews = true;
        MessageBubbleViewModel.AutoDownloadMedia = true;

        // 1. Text message sent first creates the bubble
        var bubble = MessageBubbleViewModel.FromChatMessage(textMsg);
        Assert.False(bubble.HasImage);
        Assert.False(bubble.IsPreviewVisible);
        Assert.Null(bubble.ImageThumbnail);
        Assert.False(bubble.ShowManualDownloadButton);

        // Track property change notifications
        var changedProperties = new List<string>();
        bubble.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        // 2. Image message sent immediately after merges into bubble
        bubble.MergeMessage(imgMsg);

        // Preview should immediately become visible and PropertyChanged raised
        Assert.True(bubble.HasImage);
        Assert.True(bubble.IsPreviewVisible);
        Assert.Contains(nameof(MessageBubbleViewModel.IsPreviewVisible), changedProperties);
        Assert.Equal("Here is the screenshot", bubble.DisplayText);
        Assert.False(bubble.IsOnlyImage);

        // Wait/assert thumbnail is loaded from cache
        if (bubble.ImageThumbnail is null)
        {
            await bubble.LoadThumbnailAsync();
        }
        Assert.NotNull(bubble.ImageThumbnail);
        Assert.False(bubble.ShowManualDownloadButton);
    }

    [AvaloniaFact]
    public void MessageBubbleViewModel_ManualDownloadButtonVisibility_DependsOnThumbnailAndLoadingState()
    {
        MessageBubbleViewModel.ShowInlinePreviews = true;
        MessageBubbleViewModel.AutoDownloadMedia = false;

        var bubble = new MessageBubbleViewModel
        {
            HasImage = true,
            ImageUrl = "https://example.com/test.png"
        };

        // Thumbnail is null, not loading => manual download button should be shown
        Assert.True(bubble.ShowManualDownloadButton);

        // While loading => manual download button should NOT be shown
        bubble.IsLoadingImage = true;
        Assert.False(bubble.ShowManualDownloadButton);

        // Once thumbnail is loaded => manual download button should NOT be shown
        bubble.IsLoadingImage = false;
        var sampleBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        using var ms = new MemoryStream(sampleBytes);
        bubble.ImageThumbnail = new Avalonia.Media.Imaging.Bitmap(ms);
        Assert.False(bubble.ShowManualDownloadButton);
    }
}
