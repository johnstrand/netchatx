using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Stanza.Gui.Controls;
using Stanza.Gui.Helpers.Markdown;
using Stanza.Gui.ViewModels;
using Stanza.Protocol.Xeps.Core;
using Stanza.Protocol.Xeps.Messaging;
using Stanza.Storage.Models;
using Xunit;

namespace Stanza.Gui.Tests;

public class MarkdownTests
{
    [Fact]
    public void MarkdownParser_BoldFormatting_ParsesCorrectly()
    {
        // Double asterisk
        var doc1 = MarkdownParser.Parse("Hello **bold world**!");
        Assert.Single(doc1.Blocks);
        var p1 = Assert.IsType<MarkdownParagraph>(doc1.Blocks[0]);
        Assert.Equal(3, p1.Inlines.Count);
        Assert.Equal("Hello ", Assert.IsType<MarkdownText>(p1.Inlines[0]).Text);
        var bold1 = Assert.IsType<MarkdownBold>(p1.Inlines[1]);
        Assert.Equal("bold world", Assert.IsType<MarkdownText>(bold1.Inlines[0]).Text);
        Assert.Equal("!", Assert.IsType<MarkdownText>(p1.Inlines[2]).Text);

        // Single asterisk (XEP-0393)
        var doc2 = MarkdownParser.Parse("This is *strongly emphasized* text");
        var p2 = Assert.IsType<MarkdownParagraph>(doc2.Blocks[0]);
        Assert.Equal(3, p2.Inlines.Count);
        var bold2 = Assert.IsType<MarkdownBold>(p2.Inlines[1]);
        Assert.Equal("strongly emphasized", Assert.IsType<MarkdownText>(bold2.Inlines[0]).Text);
    }

    [Fact]
    public void MarkdownParser_ItalicFormatting_ParsesCorrectly()
    {
        var doc = MarkdownParser.Parse("This is _emphasized italic_ text");
        var p = Assert.IsType<MarkdownParagraph>(doc.Blocks[0]);
        Assert.Equal(3, p.Inlines.Count);
        Assert.Equal("This is ", Assert.IsType<MarkdownText>(p.Inlines[0]).Text);
        var it = Assert.IsType<MarkdownItalic>(p.Inlines[1]);
        Assert.Equal("emphasized italic", Assert.IsType<MarkdownText>(it.Inlines[0]).Text);
        Assert.Equal(" text", Assert.IsType<MarkdownText>(p.Inlines[2]).Text);
    }

    [Fact]
    public void MarkdownParser_BoldItalicFormatting_ParsesCorrectly()
    {
        var doc = MarkdownParser.Parse("This is ***bold and italic*** together");
        var p = Assert.IsType<MarkdownParagraph>(doc.Blocks[0]);
        Assert.Equal(3, p.Inlines.Count);
        var bi = Assert.IsType<MarkdownBoldItalic>(p.Inlines[1]);
        Assert.Equal("bold and italic", Assert.IsType<MarkdownText>(bi.Inlines[0]).Text);
    }

    [Fact]
    public void MarkdownParser_StrikethroughFormatting_ParsesCorrectly()
    {
        // Double tilde
        var doc1 = MarkdownParser.Parse("Old ~~deprecated~~ value");
        var p1 = Assert.IsType<MarkdownParagraph>(doc1.Blocks[0]);
        var strike1 = Assert.IsType<MarkdownStrikethrough>(p1.Inlines[1]);
        Assert.Equal("deprecated", Assert.IsType<MarkdownText>(strike1.Inlines[0]).Text);

        // Single tilde (XEP-0393)
        var doc2 = MarkdownParser.Parse("Old ~deprecated~ value");
        var p2 = Assert.IsType<MarkdownParagraph>(doc2.Blocks[0]);
        var strike2 = Assert.IsType<MarkdownStrikethrough>(p2.Inlines[1]);
        Assert.Equal("deprecated", Assert.IsType<MarkdownText>(strike2.Inlines[0]).Text);
    }

    [Fact]
    public void MarkdownParser_InlineCode_PreservesLiteralContent()
    {
        var doc = MarkdownParser.Parse("Run `git checkout -b feature/*` now");
        var p = Assert.IsType<MarkdownParagraph>(doc.Blocks[0]);
        Assert.Equal(3, p.Inlines.Count);
        var code = Assert.IsType<MarkdownInlineCode>(p.Inlines[1]);
        Assert.Equal("git checkout -b feature/*", code.Code);
    }

    [Fact]
    public void MarkdownParser_MarkdownLink_ExtractedProperly()
    {
        var doc = MarkdownParser.Parse("Visit [Stanza Repo](https://github.com/johnstrand/stanza) today!");
        var p = Assert.IsType<MarkdownParagraph>(doc.Blocks[0]);
        Assert.Equal(3, p.Inlines.Count);
        var link = Assert.IsType<MarkdownLink>(p.Inlines[1]);
        Assert.Equal("Stanza Repo", link.Text);
        Assert.Equal("https://github.com/johnstrand/stanza", link.Url);
    }

    [Fact]
    public void MarkdownParser_CodeBlock_ExtractsLanguageAndCode()
    {
        var text = "```csharp\npublic void SayHello()\n{\n    Console.WriteLine(\"Hi\");\n}\n```";
        var doc = MarkdownParser.Parse(text);

        Assert.Single(doc.Blocks);
        var cb = Assert.IsType<MarkdownCodeBlock>(doc.Blocks[0]);
        Assert.Equal("csharp", cb.Language);
        Assert.Equal("public void SayHello()\n{\n    Console.WriteLine(\"Hi\");\n}", cb.Code);
    }

    [Fact]
    public void MarkdownParser_Blockquote_ParsedProperly()
    {
        var text = "> Line 1 of quote\n> Line 2 with *bold* text";
        var doc = MarkdownParser.Parse(text);

        Assert.Single(doc.Blocks);
        var bq = Assert.IsType<MarkdownBlockquote>(doc.Blocks[0]);
        Assert.NotEmpty(bq.Blocks);
        var p = Assert.IsType<MarkdownParagraph>(bq.Blocks[0]);
        var bold = p.Inlines.OfType<MarkdownBold>().FirstOrDefault();
        Assert.NotNull(bold);
        Assert.Equal("bold", Assert.IsType<MarkdownText>(bold.Inlines[0]).Text);
    }

    [Fact]
    public void MarkdownParser_Headers_ParsedProperly()
    {
        var text = "# Top Header\n## Sub Header\nRegular paragraph";
        var doc = MarkdownParser.Parse(text);

        Assert.Equal(3, doc.Blocks.Count);
        var h1 = Assert.IsType<MarkdownHeader>(doc.Blocks[0]);
        Assert.Equal(1, h1.Level);
        Assert.Equal("Top Header", Assert.IsType<MarkdownText>(h1.Inlines[0]).Text);

        var h2 = Assert.IsType<MarkdownHeader>(doc.Blocks[1]);
        Assert.Equal(2, h2.Level);
        Assert.Equal("Sub Header", Assert.IsType<MarkdownText>(h2.Inlines[0]).Text);

        var p = Assert.IsType<MarkdownParagraph>(doc.Blocks[2]);
        Assert.Equal("Regular paragraph", Assert.IsType<MarkdownText>(p.Inlines[0]).Text);
    }

    [Fact]
    public void MarkdownParser_UnorderedAndOrderedLists_ParsedProperly()
    {
        var text = "- First item\n- Second item\n\n1. Step one\n2. Step two";
        var doc = MarkdownParser.Parse(text);

        Assert.Equal(2, doc.Blocks.Count);

        var list1 = Assert.IsType<MarkdownList>(doc.Blocks[0]);
        Assert.False(list1.IsOrdered);
        Assert.Equal(2, list1.Items.Count);
        Assert.Equal("First item", Assert.IsType<MarkdownText>(list1.Items[0].Inlines[0]).Text);

        var list2 = Assert.IsType<MarkdownList>(doc.Blocks[1]);
        Assert.True(list2.IsOrdered);
        Assert.Equal(2, list2.Items.Count);
        Assert.Equal(1, list2.Items[0].Number);
        Assert.Equal("Step one", Assert.IsType<MarkdownText>(list2.Items[0].Inlines[0]).Text);
        Assert.Equal(2, list2.Items[1].Number);
        Assert.Equal("Step two", Assert.IsType<MarkdownText>(list2.Items[1].Inlines[0]).Text);
    }

    [Fact]
    public void MarkdownParser_BoundaryRules_IgnoresSnakeCaseAndArithmetic()
    {
        // snake_case should NOT be italic
        var doc1 = MarkdownParser.Parse("The variable user_name_field should remain unformatted");
        var p1 = Assert.IsType<MarkdownParagraph>(doc1.Blocks[0]);
        Assert.Empty(p1.Inlines.OfType<MarkdownItalic>());

        // 2 * 3 = 6 should NOT be bold
        var doc2 = MarkdownParser.Parse("Calculate 2 * 3 * 4 = 24");
        var p2 = Assert.IsType<MarkdownParagraph>(doc2.Blocks[0]);
        Assert.Empty(p2.Inlines.OfType<MarkdownBold>());
    }

    [Fact]
    public void HyperlinkTextBlock_MarkdownFormatting_RendersInlinesCorrectly()
    {
        var control = new HyperlinkTextBlock
        {
            Text = "Here is **bold text** and `inline_code`!"
        };

        Assert.NotNull(control.Inlines);
        Assert.Equal(5, control.Inlines.Count);

        // 1st: Run "Here is "
        var r1 = Assert.IsType<Run>(control.Inlines[0]);
        Assert.Equal("Here is ", r1.Text);

        // 2nd: Run "bold text" with bold weight
        var r2 = Assert.IsType<Run>(control.Inlines[1]);
        Assert.Equal("bold text", r2.Text);
        Assert.Equal(FontWeight.Bold, r2.FontWeight);

        // 3rd: Run " and "
        var r3 = Assert.IsType<Run>(control.Inlines[2]);
        Assert.Equal(" and ", r3.Text);

        // 4th: InlineUIContainer with Border for inline code
        var c1 = Assert.IsType<InlineUIContainer>(control.Inlines[3]);
        var border = Assert.IsType<Border>(c1.Child);
        var codeTb = Assert.IsType<TextBlock>(border.Child);
        Assert.Equal("inline_code", codeTb.Text);

        // 5th: Run "!"
        var r4 = Assert.IsType<Run>(control.Inlines[4]);
        Assert.Equal("!", r4.Text);
    }

    [Fact]
    public void HyperlinkTextBlock_MarkdownLink_RendersClickableButton()
    {
        string? clicked = null;
        var control = new HyperlinkTextBlock
        {
            OpenUrlAction = url => clicked = url,
            Text = "Go to [Google](https://www.google.com) now"
        };

        Assert.NotNull(control.Inlines);
        Assert.Equal(3, control.Inlines.Count);

        var container = Assert.IsType<InlineUIContainer>(control.Inlines[1]);
        var button = Assert.IsType<Button>(container.Child);
        Assert.Contains("inline-link", button.Classes);

        var tb = Assert.IsType<TextBlock>(button.Content);
        Assert.Equal("Google", tb.Text);

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://www.google.com/", clicked);
    }

    [Fact]
    public void HyperlinkTextBlock_IsMarkdownEnabledFalse_DisablesMarkdownFormatting()
    {
        var control = new HyperlinkTextBlock
        {
            IsMarkdownEnabled = false,
            Text = "Plain text with *no* **markdown** here"
        };

        // When markdown is disabled and no links exist, Inlines remains empty
        Assert.True(control.Inlines == null || control.Inlines.Count == 0);
    }

    [Fact]
    public void Xep0393_MessageStyling_DiscoveryAndUnstyledHelpers()
    {
        // Feature URI
        var styling = new Xep0393MessageStyling();
        Assert.Equal("urn:xmpp:styling:0", styling.FeatureUri);

        // Service discovery registration
        var disco = new Xep0030ServiceDiscovery();
        Assert.Contains("urn:xmpp:styling:0", disco.SupportedFeatures);

        // Unstyled message handling
        var msg = new Stanza.Core.Stanzas.MessageStanza(to: Stanza.Core.Jid.Parse("peer@example.com"), body: "> _ <");
        Assert.False(Xep0393MessageStyling.IsUnstyled(msg.RawElement));

        Xep0393MessageStyling.AttachUnstyled(msg.RawElement);
        Assert.True(Xep0393MessageStyling.IsUnstyled(msg.RawElement));

        // MessageBubbleViewModel parsing
        var chatMsg = new ChatMessage
        {
            Id = "msg_unstyled_1",
            AccountJid = "me@example.com",
            RemoteJid = "peer@example.com",
            SenderJid = "peer@example.com",
            Body = "> _ <",
            RawXml = msg.ToXmlString()
        };
        var vm = MessageBubbleViewModel.FromChatMessage(chatMsg);
        Assert.True(vm.IsUnstyled);
    }
}
