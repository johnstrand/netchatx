using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class GifSupportTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly MessageRepository _messageRepo;

    // Standard GIF magic bytes: "GIF89a"
    private static readonly byte[] Gif89aHeader = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x0A, 0x00, 0x0A, 0x00];
    // Standard GIF magic bytes: "GIF87a"
    private static readonly byte[] Gif87aHeader = [0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0x0A, 0x00, 0x0A, 0x00];
    // Standard PNG magic bytes
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public GifSupportTests()
    {
        _dbPath = $"testgif_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _messageRepo = new MessageRepository(_dbContext);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void GifDecoder_IsGif_DetectsValidGifHeaders()
    {
        Assert.True(GifDecoder.IsGif(Gif89aHeader));
        Assert.True(GifDecoder.IsGif(Gif87aHeader));

        Assert.False(GifDecoder.IsGif(PngHeader));
        Assert.False(GifDecoder.IsGif([]));
        Assert.False(GifDecoder.IsGif([0x47, 0x49, 0x46])); // Too short
        Assert.False(GifDecoder.IsGif("Hello World"u8.ToArray()));
    }

    [Theory]
    [InlineData(@"<img src=""https://media.tenor.com/m/abc123_tenor.gif"">", "https://media.tenor.com/m/abc123_tenor.gif")]
    [InlineData(@"<img src='https://c.tenor.com/xyz789.gif' width='200' />", "https://c.tenor.com/xyz789.gif")]
    [InlineData(@"<IMG SRC=https://media1.giphy.com/media/test/giphy.gif>", "https://media1.giphy.com/media/test/giphy.gif")]
    [InlineData(@"<img src=""https://example.com/test.gif?a=1&amp;b=2"">", "https://example.com/test.gif?a=1&b=2")]
    public void ClipboardImageHelper_ExtractImageSourceFromHtml_ExtractsCorrectUrl(string html, string expectedUrl)
    {
        var result = ClipboardImageHelper.ExtractImageSourceFromHtml(html);
        Assert.Equal(expectedUrl, result);
    }

    [Fact]
    public void ClipboardImageHelper_ExtractImageSourceFromHtml_ReturnsNullForInvalidHtml()
    {
        Assert.Null(ClipboardImageHelper.ExtractImageSourceFromHtml(string.Empty));
        Assert.Null(ClipboardImageHelper.ExtractImageSourceFromHtml("<span>No image here</span>"));
        Assert.Null(ClipboardImageHelper.ExtractImageSourceFromHtml("<div><p>Just text</p></div>"));
    }

    [Theory]
    [InlineData("https://media.tenor.com/abc123_tenor.gif", true)]
    [InlineData("https://c.tenor.com/xyz789", true)]
    [InlineData("https://media1.giphy.com/media/test/giphy.gif", true)]
    [InlineData("https://example.com/funny.gif", true)]
    [InlineData("https://example.com/photo.png", true)]
    [InlineData("https://example.com/not-an-image", false)]
    [InlineData("random string", false)]
    public void ClipboardImageHelper_IsImageUrl_RecognizesGifAndImageUrls(string url, bool expected)
    {
        Assert.Equal(expected, ClipboardImageHelper.IsImageUrl(url));
    }

    [Theory]
    [InlineData("Check this out: https://media.tenor.com/abc123_tenor.gif wow", true, "https://media.tenor.com/abc123_tenor.gif")]
    [InlineData("https://c.tenor.com/xyz_foo", true, "https://c.tenor.com/xyz_foo")]
    [InlineData("https://media.giphy.com/media/abc/giphy.gif", true, "https://media.giphy.com/media/abc/giphy.gif")]
    [InlineData("file:///C:/Stanza/media/anim.gif", false, null)]
    [InlineData("https://example.com/animated.gif?v=2", true, "https://example.com/animated.gif?v=2")]
    [InlineData("Just plain text without any image", false, null)]
    public void MessageBubbleViewModel_ImageUrlRegex_MatchesGifs(string text, bool shouldMatch, string? expectedUrl)
    {
        var match = MessageBubbleViewModel.ImageUrlRegex.Match(text);
        Assert.Equal(shouldMatch, match.Success);
        if (shouldMatch)
        {
            Assert.Equal(expectedUrl, match.Value);
        }
    }

    [Fact]
    public void MessageBubbleViewModel_ExtractImageUrl_SetsIsGifAndHasImage()
    {
        var bubble = new MessageBubbleViewModel();
        bubble.Body = "Check this GIF https://media.tenor.com/m/example.gif";

        Assert.True(bubble.HasImage);
        Assert.True(bubble.IsGif);
        Assert.Equal("https://media.tenor.com/m/example.gif", bubble.ImageUrl);
        Assert.False(bubble.IsOnlyImage);

        // Body with only GIF URL
        bubble.Body = "https://media.tenor.com/m/example.gif";
        Assert.True(bubble.HasImage);
        Assert.True(bubble.IsGif);
        Assert.True(bubble.IsOnlyImage);
    }

    [Fact]
    public void ChatConversationViewModel_StageImageAttachment_DetectsGifBytesAndSetsHeader()
    {
        var remote = Jid.Parse("peer@test.org");
        var conv = new ChatConversationViewModel(
            "user@test.org",
            remote.ToString(),
            "Peer",
            remote,
            isGroupChat: false,
            _messageRepo);

        // Stage GIF bytes
        conv.StageImageAttachment(Gif89aHeader, "test_animation");

        Assert.True(conv.HasPendingImage);
        Assert.True(conv.IsPendingImageGif);
        Assert.Equal("🎞️ GIF ready to send", conv.PendingImageHeader);
        Assert.EndsWith(".gif", conv.PendingImageFileName, StringComparison.OrdinalIgnoreCase);

        // Clear pending image
        conv.ClearPendingImage();
        Assert.False(conv.HasPendingImage);
        Assert.False(conv.IsPendingImageGif);
        Assert.Equal("📷 Image ready to send", conv.PendingImageHeader);

        // Stage PNG bytes
        conv.StageImageAttachment(PngHeader, "photo.png");
        Assert.True(conv.HasPendingImage);
        Assert.False(conv.IsPendingImageGif);
        Assert.Equal("📷 Image ready to send", conv.PendingImageHeader);
        Assert.Equal("photo.png", conv.PendingImageFileName);
    }

    [Fact]
    public async Task ChatConversationViewModel_SendMessageAsync_WithPendingGif_SendsAndAttachesOob()
    {
        var account = "alice@example.com";
        var remote = Jid.Parse("bob@example.com");

        var transport = new LoopbackTransport();
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse($"{account}/desktop"),
            Password = "pass"
        }, transport);

        var conv = new ChatConversationViewModel(
            account,
            remote.ToString(),
            "Bob",
            remote,
            isGroupChat: false,
            _messageRepo,
            client: client);

        conv.StageImageAttachment(Gif89aHeader, "funny.gif");
        conv.InputText = "LOL look at this";

        await conv.SendMessageAsync();

        Assert.False(conv.HasPendingImage);
        Assert.Empty(conv.InputText);

        Assert.Single(conv.Messages);
        var sentMsg = conv.Messages[0];
        Assert.Contains(".gif", sentMsg.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOL look at this", sentMsg.Body);
        Assert.StartsWith("file:///", sentMsg.Body);
        // file:// URIs are not auto-loaded as remote image previews for security reasons
        Assert.False(sentMsg.HasImage);

        // Remote HTTP/HTTPS GIF messages are loaded with preview and GIF flag
        var httpBubble = new MessageBubbleViewModel();
        httpBubble.Body = "https://media.tenor.com/sample.gif\nLOL look at this";
        Assert.True(httpBubble.HasImage);
        Assert.True(httpBubble.IsGif);
    }
}
