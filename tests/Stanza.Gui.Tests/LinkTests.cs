using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Stanza.Gui.Controls;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.Storage.Models;
using Xunit;

namespace Stanza.Gui.Tests;

public class LinkTests
{
    [Fact]
    public void LinkParser_PlainText_ReturnsSingleNonLinkSegment()
    {
        var text = "Hello, this is a plain message with no links!";
        var segments = LinkParser.Parse(text);

        Assert.Single(segments);
        Assert.False(segments[0].IsLink);
        Assert.Equal(text, segments[0].Text);
    }

    [Fact]
    public void LinkParser_SingleHttpAndHttpsUrls_ExtractedProperly()
    {
        var text = "Check out https://github.com/johnstrand/stanza for code";
        var segments = LinkParser.Parse(text);

        Assert.Equal(3, segments.Count);
        Assert.Equal("Check out ", segments[0].Text);
        Assert.False(segments[0].IsLink);

        Assert.Equal("https://github.com/johnstrand/stanza", segments[1].Text);
        Assert.True(segments[1].IsLink);
        Assert.Equal("https://github.com/johnstrand/stanza", segments[1].NavigateUri);

        Assert.Equal(" for code", segments[2].Text);
        Assert.False(segments[2].IsLink);
    }

    [Fact]
    public void LinkParser_TrailingPunctuation_ExcludedFromUrl()
    {
        var text = "Visit https://example.com/test. Have you seen https://another.com/path, right?";
        var segments = LinkParser.Parse(text);

        var links = segments.Where(s => s.IsLink).ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/test", links[0].Text);
        Assert.Equal("https://another.com/path", links[1].Text);

        // Invariant: concatenated segment texts equal original string
        var reconstructed = string.Join("", segments.Select(s => s.Text));
        Assert.Equal(text, reconstructed);
    }

    [Fact]
    public void LinkParser_ParenthesesInUrl_BalancedKept_UnbalancedExcluded()
    {
        // 1. Unbalanced: (https://example.com)
        var text1 = "Look at this (https://example.com)!";
        var segments1 = LinkParser.Parse(text1);
        var link1 = segments1.First(s => s.IsLink);
        Assert.Equal("https://example.com", link1.Text);
        Assert.Equal(text1, string.Join("", segments1.Select(s => s.Text)));

        // 2. Balanced in Wikipedia: https://en.wikipedia.org/wiki/C_(programming_language)
        var text2 = "Read https://en.wikipedia.org/wiki/C_(programming_language) here.";
        var segments2 = LinkParser.Parse(text2);
        var link2 = segments2.First(s => s.IsLink);
        Assert.Equal("https://en.wikipedia.org/wiki/C_(programming_language)", link2.Text);
        Assert.Equal(text2, string.Join("", segments2.Select(s => s.Text)));
    }

    [Fact]
    public void LinkParser_WwwPrefix_NormalizesToHttps()
    {
        var text = "Search on www.google.com today";
        var segments = LinkParser.Parse(text);

        Assert.Equal(3, segments.Count);
        Assert.Equal("www.google.com", segments[1].Text);
        Assert.True(segments[1].IsLink);
        Assert.Equal("https://www.google.com/", segments[1].NavigateUri);
    }

    [Fact]
    public void LinkParser_MailtoAndXmpp_ExtractedProperly()
    {
        var text = "Email mailto:support@stanza.im or join xmpp:team@conference.stanza.im?join";
        var segments = LinkParser.Parse(text);

        var links = segments.Where(s => s.IsLink).ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("mailto:support@stanza.im", links[0].Text);
        Assert.Equal("xmpp:team@conference.stanza.im?join", links[1].Text);
    }

    [Fact]
    public void LinkParser_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(LinkParser.Parse(null));
        Assert.Empty(LinkParser.Parse(string.Empty));
    }

    [Fact]
    public void HyperlinkTextBlock_WithoutLinks_InlinesCleared()
    {
        var control = new HyperlinkTextBlock
        {
            Text = "Plain text without any link."
        };

        Assert.True(control.Inlines == null || control.Inlines.Count == 0);
    }

    [Fact]
    public void HyperlinkTextBlock_WithLinks_PopulatesInlinesAndButtons()
    {
        string clickedUrl = null!;
        var control = new HyperlinkTextBlock
        {
            OpenUrlAction = url => clickedUrl = url,
            MessageDirection = MessageDirection.Inbound,
            Text = "Check https://github.com/johnstrand/stanza please!"
        };
        Assert.NotNull(control.Inlines);
        Assert.Equal(3, control.Inlines.Count);

        // 1st inline: Run "Check "
        Assert.IsType<Run>(control.Inlines[0]);
        Assert.Equal("Check ", ((Run)control.Inlines[0]).Text);

        // 2nd inline: InlineUIContainer with Button
        var container = Assert.IsType<InlineUIContainer>(control.Inlines[1]);
        var button = Assert.IsType<Button>(container.Child);
        Assert.Contains("inline-link", button.Classes);

        // Simulating click
        var tbContent = Assert.IsType<TextBlock>(button.Content);
        Assert.Equal("https://github.com/johnstrand/stanza", tbContent.Text);

        // Perform click programmatically
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://github.com/johnstrand/stanza", clickedUrl);

        // 3rd inline: Run " please!"
        Assert.IsType<Run>(control.Inlines[2]);
        Assert.Equal(" please!", ((Run)control.Inlines[2]).Text);
    }

    [Fact]
    public void HyperlinkTextBlock_DirectionChange_UpdatesLinkBrush()
    {
        var control = new HyperlinkTextBlock
        {
            MessageDirection = MessageDirection.Outbound,
            Text = "Link: https://example.com"
        };

        Assert.NotNull(control.Inlines);
        var container = Assert.IsType<InlineUIContainer>(control.Inlines[1]);
        var button = Assert.IsType<Button>(container.Child);
        var linkTb = Assert.IsType<TextBlock>(button.Content);
        Assert.NotNull(linkTb.Foreground);

        // Switch to inbound
        control.MessageDirection = MessageDirection.Inbound;
        container = Assert.IsType<InlineUIContainer>(control.Inlines[1]);
        button = Assert.IsType<Button>(container.Child);
        linkTb = Assert.IsType<TextBlock>(button.Content);
        Assert.NotNull(linkTb.Foreground);
    }

    [Fact]
    public void UrlLauncher_Validation_RejectsInvalidOrMaliciousUrls()
    {
        Assert.False(UrlLauncher.OpenUrl(null));
        Assert.False(UrlLauncher.OpenUrl(string.Empty));
        Assert.False(UrlLauncher.OpenUrl("not-a-url"));
        Assert.False(UrlLauncher.OpenUrl("file:///c:/secret.txt")); // only http, https, mailto, xmpp allowed
        Assert.False(UrlLauncher.OpenUrl("javascript:alert(1)"));
    }

    [Fact]
    public void MessageBubbleViewModel_ExtractLinks_DetectsUrlsAndSetsProperties()
    {
        var msg = new ChatMessage
        {
            Id = "msg_link_1",
            AccountJid = "me@test.org",
            RemoteJid = "peer@test.org",
            SenderJid = "peer@test.org",
            Body = "Check out https://github.com/johnstrand/stanza and www.google.com for more info.",
            Timestamp = DateTimeOffset.UtcNow,
            Direction = MessageDirection.Inbound
        };

        var vm = MessageBubbleViewModel.FromChatMessage(msg);

        Assert.True(vm.HasLinks);
        Assert.Equal(2, vm.Links.Count);
        Assert.Equal("https://github.com/johnstrand/stanza", vm.Links[0]);
        Assert.Equal("https://www.google.com/", vm.Links[1]);
    }

    [Fact]
    public void MessageBubbleViewModel_OnBodyChanged_UpdatesLinksAutomatically()
    {
        var vm = new MessageBubbleViewModel
        {
            Body = "Initial message with no links"
        };

        Assert.False(vm.HasLinks);
        Assert.Empty(vm.Links);

        // Edit message (e.g. via XEP-0308 Last Message Correction)
        vm.Body = "Updated message with link: https://avaloniaui.net";

        Assert.True(vm.HasLinks);
        Assert.Single(vm.Links);
        Assert.Equal("https://avaloniaui.net/", vm.Links[0]);
    }

    [Fact]
    public void HyperlinkTextBlock_ContextMenu_HasOpenAndCopyActions()
    {
        string? clickedUrl = null;
        string? copiedUrl = null;

        var control = new HyperlinkTextBlock
        {
            OpenUrlAction = url => clickedUrl = url,
            CopyUrlAction = url => { copiedUrl = url; return Task.CompletedTask; },
            Text = "Click https://github.com now"
        };

        var container = Assert.IsType<InlineUIContainer>(control.Inlines![1]);
        var button = Assert.IsType<Button>(container.Child);
        var flyout = Assert.IsType<MenuFlyout>(button.ContextFlyout);

        Assert.Equal(2, flyout.Items.Count);
        var openItem = Assert.IsType<MenuItem>(flyout.Items[0]);
        var copyItem = Assert.IsType<MenuItem>(flyout.Items[1]);

        Assert.Equal("🌐 Open Link", openItem.Header);
        Assert.Equal("📋 Copy Link Address", copyItem.Header);

        // Test clicking open menu item
        openItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("https://github.com/", clickedUrl);

        // Test clicking copy menu item
        copyItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("https://github.com/", copiedUrl);
    }

    [Fact]
    public void HyperlinkTextBlock_MultipleLinks_PopulatesAllInlines()
    {
        var control = new HyperlinkTextBlock
        {
            Text = "One https://link1.com two https://link2.com three"
        };

        Assert.NotNull(control.Inlines);
        Assert.Equal(5, control.Inlines.Count);

        Assert.IsType<Run>(control.Inlines[0]);
        Assert.Equal("One ", ((Run)control.Inlines[0]).Text);

        var c1 = Assert.IsType<InlineUIContainer>(control.Inlines[1]);
        var b1 = Assert.IsType<Button>(c1.Child);
        var tb1 = Assert.IsType<TextBlock>(b1.Content);
        Assert.Equal("https://link1.com", tb1.Text);

        Assert.IsType<Run>(control.Inlines[2]);
        Assert.Equal(" two ", ((Run)control.Inlines[2]).Text);

        var c2 = Assert.IsType<InlineUIContainer>(control.Inlines[3]);
        var b2 = Assert.IsType<Button>(c2.Child);
        var tb2 = Assert.IsType<TextBlock>(b2.Content);
        Assert.Equal("https://link2.com", tb2.Text);

        Assert.IsType<Run>(control.Inlines[4]);
        Assert.Equal(" three", ((Run)control.Inlines[4]).Text);
    }
}
