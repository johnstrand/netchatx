using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using NetChatx.Gui.Helpers;
using NetChatx.Gui.Helpers.Markdown;
using NetChatx.Gui.ViewModels;
using NetChatx.Gui.Views;
using NetChatx.Storage;
using NetChatx.Storage.Models;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

public class HelperAndViewModelCoverageTests
{
    [Fact]
    public void GifDecoder_IsGif_ValidatesMagicBytesCorrectly()
    {
        // Null / empty / too short
        Assert.False(GifDecoder.IsGif(ReadOnlySpan<byte>.Empty));
        Assert.False(GifDecoder.IsGif(new byte[] { (byte)'G', (byte)'I', (byte)'F' }));

        // Valid GIF89a
        byte[] gif89a = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0, 0];
        Assert.True(GifDecoder.IsGif(gif89a));

        // Valid GIF87a
        byte[] gif87a = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7', (byte)'a', 0, 0];
        Assert.True(GifDecoder.IsGif(gif87a));

        // Valid GIF88a
        byte[] gif88a = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'8', (byte)'a', 0, 0];
        Assert.True(GifDecoder.IsGif(gif88a));

        // Invalid versions
        byte[] gif90a = [(byte)'G', (byte)'I', (byte)'F', (byte)'9', (byte)'0', (byte)'a', 0, 0];
        Assert.False(GifDecoder.IsGif(gif90a));

        // PNG header
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];
        Assert.False(GifDecoder.IsGif(pngHeader));
    }

    [Fact]
    public void GifDecoder_DecodeFrames_ReturnsNullForInvalidOrEmptyInput()
    {
        Assert.Null(GifDecoder.DecodeFrames(null!));
        Assert.Null(GifDecoder.DecodeFrames([]));
        Assert.Null(GifDecoder.DecodeFrames([1, 2, 3, 4, 5, 6]));

        byte[] fakeGif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0, 0];
        Assert.Null(GifDecoder.DecodeFrames(fakeGif));
    }

    [Fact]
    public void UrlLauncher_OpenUrl_ValidatesSchemesCorrectly()
    {
        // Empty / Whitespace
        Assert.False(UrlLauncher.OpenUrl(null));
        Assert.False(UrlLauncher.OpenUrl(""));
        Assert.False(UrlLauncher.OpenUrl("   "));

        // Malformed
        Assert.False(UrlLauncher.OpenUrl("not a valid url::::////"));

        // Rejected unsafe schemes
        Assert.False(UrlLauncher.OpenUrl("file:///C:/Windows/System32/calc.exe"));
        Assert.False(UrlLauncher.OpenUrl("javascript:alert(1)"));
        Assert.False(UrlLauncher.OpenUrl("ftp://ftp.example.com/file.zip"));
        Assert.False(UrlLauncher.OpenUrl("data:text/plain;base64,SGVsbG8="));
    }

    [Fact]
    public async Task UrlLauncher_CopyToClipboardAsync_HandlesNullAndEmpty()
    {
        Assert.False(await UrlLauncher.CopyToClipboardAsync(null));
        Assert.False(await UrlLauncher.CopyToClipboardAsync(""));
    }

    [Fact]
    public void MarkdownRenderer_RendersHeadersListsBlockquotesAndCodeBlocks()
    {
        var text = new TextBlock();
        var inlines = text.Inlines!;
        var context = new MarkdownRenderContext(
            FontSize: 13,
            Foreground: Brushes.White,
            MessageDirection: MessageDirection.Inbound);

        // 1. Render Header (Level 1, 2, 3)
        var headerMd = MarkdownParser.Parse("# Header 1\n## Header 2\n### Header 3\n#### Header 4");
        MarkdownRenderer.RenderToInlines(headerMd, inlines, context);
        Assert.NotEmpty(inlines);

        // 2. Render Unordered and Ordered Lists
        var listMd = MarkdownParser.Parse("- Bullet 1\n- Bullet 2\n1. Numbered 1\n2. Numbered 2");
        inlines.Clear();
        MarkdownRenderer.RenderToInlines(listMd, inlines, context);
        Assert.NotEmpty(inlines);
        Assert.Contains(inlines.OfType<Run>(), r => r.Text == "•  ");
        Assert.Contains(inlines.OfType<Run>(), r => r.Text == "1. ");

        // 3. Render Blockquote
        var quoteMd = MarkdownParser.Parse("> Quoted line 1\n> Quoted line 2");
        inlines.Clear();
        MarkdownRenderer.RenderToInlines(quoteMd, inlines, context);
        Assert.NotEmpty(inlines);
        Assert.Contains(inlines, i => i is InlineUIContainer);

        // 4. Render Fenced Code Block with Language
        var codeMd = MarkdownParser.Parse("```csharp\npublic void Test() {}\n```");
        inlines.Clear();
        MarkdownRenderer.RenderToInlines(codeMd, inlines, context);
        Assert.NotEmpty(inlines);
        var container = inlines.OfType<InlineUIContainer>().First();
        var border = Assert.IsType<Border>(container.Child);
        var grid = Assert.IsType<Grid>(border.Child);
        Assert.Equal(2, grid.Children.Count);

        // 5. Render Strikethrough and Bold Italic
        var inlineMd = MarkdownParser.Parse("Text with ~~strikethrough~~ and ***bolditalic*** and `code`");
        inlines.Clear();
        MarkdownRenderer.RenderToInlines(inlineMd, inlines, context);
        Assert.NotEmpty(inlines);
    }

    [Fact]
    public void MarkdownRenderer_ThrowsOnNullArguments()
    {
        var text = new TextBlock();
        var inlines = text.Inlines!;
        var context = new MarkdownRenderContext(12);

        Assert.Throws<ArgumentNullException>(() => MarkdownRenderer.RenderToInlines(null!, inlines, context));
        Assert.Throws<ArgumentNullException>(() => MarkdownRenderer.RenderToInlines(new MarkdownDocument([]), null!, context));
        Assert.Throws<ArgumentNullException>(() => MarkdownRenderer.RenderToInlines(new MarkdownDocument([]), inlines, null!));
    }

    [Fact]
    public async Task OmemoDeviceItemViewModel_PropertiesAndToggleTrust_WorkAsExpected()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"netchatx_omemo_test_{Guid.NewGuid():N}.db");
        var context = new DatabaseContext(dbPath);
        var repo = new OmemoRepository(context);

        var item = new OmemoDeviceItemViewModel(
            repo,
            "me@example.com",
            "peer@example.com",
            12345,
            "AABBCCDDEEFF00112233445566778899",
            OmemoTrustState.Trusted);

        Assert.Equal(12345u, item.DeviceId);
        Assert.Equal("AABBCCDDEEFF00112233445566778899", item.Fingerprint);
        Assert.True(item.IsTrusted);
        Assert.Equal(OmemoTrustState.Trusted, item.TrustState);

        await item.ToggleTrustAsync();
        Assert.False(item.IsTrusted);
        Assert.Equal(OmemoTrustState.Untrusted, item.TrustState);

        await item.ToggleTrustAsync();
        Assert.True(item.IsTrusted);
        Assert.Equal(OmemoTrustState.Trusted, item.TrustState);

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public async Task LoginViewModel_AdvancedScenariosAndErrors_HandledCorrectly()
    {
        // 1. ToggleAdvanced
        var vm = new LoginViewModel(_ => Task.FromResult(true));
        Assert.False(vm.ShowAdvanced);
        vm.ToggleAdvanced();
        Assert.True(vm.ShowAdvanced);
        vm.ToggleAdvanced();
        Assert.False(vm.ShowAdvanced);

        // 2. Invalid port string
        vm.Jid = "alice@example.com";
        vm.Password = "secret";
        vm.Port = "not_a_number";
        await vm.ConnectAsync();
        Assert.Equal("Port must be a valid number.", vm.ErrorMessage);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.StatusMessage);

        // 3. Unsuccessful login (callback returns false)
        vm.Port = "5222";
        AccountProfile? capturedProfile = null;
        var vmFailed = new LoginViewModel(p =>
        {
            capturedProfile = p;
            return Task.FromResult(false);
        })
        {
            Jid = "bob@example.com",
            Password = "pass",
            Host = "  xmpp.example.com  ",
            Port = "5223",
            UseDirectTls = true
        };

        await vmFailed.ConnectAsync();
        Assert.Equal("Failed to connect. Please check credentials or network.", vmFailed.ErrorMessage);
        Assert.False(vmFailed.IsConnecting);
        Assert.Null(vmFailed.StatusMessage);
        Assert.NotNull(capturedProfile);
        Assert.Equal("bob@example.com", capturedProfile.Jid);
        Assert.Equal("pass", capturedProfile.Password);
        Assert.Equal("xmpp.example.com", capturedProfile.Host); // trimmed
        Assert.Equal(5223, capturedProfile.Port);
        Assert.True(capturedProfile.UseDirectTls);

        // 4. Exception thrown during login callback
        var vmException = new LoginViewModel(_ => throw new InvalidOperationException("Connection timeout"))
        {
            Jid = "charlie@example.com",
            Password = "secret",
            Host = "   " // whitespace host should become null
        };

        await vmException.ConnectAsync();
        Assert.Equal("Connection timeout", vmException.ErrorMessage);
        Assert.False(vmException.IsConnecting);
        Assert.Null(vmException.StatusMessage);

        // 5. Successful login resets error message and passes null host for empty/whitespace
        AccountProfile? okProfile = null;
        var vmOk = new LoginViewModel(p =>
        {
            okProfile = p;
            return Task.FromResult(true);
        })
        {
            Jid = "dave@example.com",
            Password = "pass",
            Host = ""
        };

        await vmOk.ConnectAsync();
        Assert.Null(vmOk.ErrorMessage);
        Assert.False(vmOk.IsConnecting);
        Assert.Null(vmOk.StatusMessage);
        Assert.NotNull(okProfile);
        Assert.Null(okProfile.Host);
    }

    [Fact]
    public void OmemoDetailsViewModel_PropertiesAndLoadDevices_Work()
    {
        var vm = new OmemoDetailsViewModel
        {
            LocalDeviceId = 98765u,
            LocalFingerprint = "FEEDFACE12345678",
            ContactJid = "contact@chat.example"
        };

        Assert.Equal(98765u, vm.LocalDeviceId);
        Assert.Equal("FEEDFACE12345678", vm.LocalFingerprint);
        Assert.Equal("contact@chat.example", vm.ContactJid);
        Assert.Empty(vm.Devices);

        string dbPath = Path.Combine(Path.GetTempPath(), $"netchatx_details_test_{Guid.NewGuid():N}.db");
        var context = new DatabaseContext(dbPath);
        var repo = new OmemoRepository(context);

        var dev1 = new OmemoDeviceItemViewModel(repo, "me@ex.com", "contact@chat.example", 1, "FP1", OmemoTrustState.Trusted);
        var dev2 = new OmemoDeviceItemViewModel(repo, "me@ex.com", "contact@chat.example", 2, "FP2", OmemoTrustState.Untrusted);

        vm.LoadDevices([dev1, dev2]);
        Assert.Equal(2, vm.Devices.Count);
        Assert.Same(dev1, vm.Devices[0]);
        Assert.Same(dev2, vm.Devices[1]);

        // Load empty collection clears
        vm.LoadDevices([]);
        Assert.Empty(vm.Devices);

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public void LinkParser_MarkdownLinksAndBrackets_ParseCorrectly()
    {
        // 1. Markdown link format
        string mdText = "Here is [Project Source](https://github.com/johnstrand/netchatx) for review.";
        var segments = LinkParser.Parse(mdText);
        Assert.Equal(3, segments.Count);
        Assert.Equal("Here is ", segments[0].Text);
        Assert.False(segments[0].IsLink);

        Assert.Equal("Project Source", segments[1].Text);
        Assert.True(segments[1].IsLink);
        Assert.Equal("https://github.com/johnstrand/netchatx", segments[1].NavigateUri);

        Assert.Equal(" for review.", segments[2].Text);
        Assert.False(segments[2].IsLink);

        // 2. Markdown link with invalid URL is treated as normal text
        string badMd = "Check [Invalid](not-a-valid-url) here";
        var badSegments = LinkParser.Parse(badMd);
        Assert.Single(badSegments);
        Assert.False(badSegments[0].IsLink);

        // 3. Square and Curly bracket trimming
        string bracketText = "Open [https://example.com/one] and {https://example.com/two} please";
        var bracketSegments = LinkParser.Parse(bracketText);
        var links = bracketSegments.Where(s => s.IsLink).ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/one", links[0].Text);
        Assert.Equal("https://example.com/two", links[1].Text);

        // 4. IsValidUrl tests
        Assert.False(LinkParser.IsValidUrl(null!, out _));
        Assert.False(LinkParser.IsValidUrl("   ", out _));
        Assert.False(LinkParser.IsValidUrl("https://", out _)); // empty host
        Assert.False(LinkParser.IsValidUrl("ftp://files.example.com", out _)); // non-supported scheme
        Assert.True(LinkParser.IsValidUrl("www.example.org", out var normalized));
        Assert.Equal("https://www.example.org/", normalized);
    }

    [Fact]
    public void EmojiData_SearchAndCategories_ValidateThoroughly()
    {
        // 1. Categories existence and sanity
        Assert.NotEmpty(EmojiData.Categories);
        Assert.Equal(6, EmojiData.Categories.Count);
        Assert.Contains(EmojiData.Categories, c => c.Name == "Smileys");
        Assert.Contains(EmojiData.Categories, c => c.Name == "Food");

        // 2. Default quick emojis
        Assert.Equal(["👍", "❤️", "😂", "😮", "😢", "🎉"], EmojiData.DefaultQuickEmojis);

        // 3. Empty or whitespace query returns all emojis
        var all1 = EmojiData.SearchEmojis("");
        var all2 = EmojiData.SearchEmojis("   ");
        Assert.NotEmpty(all1);
        Assert.Equal(all1.Count, all2.Count);

        // 4. Search by Category Name
        var foodEmojis = EmojiData.SearchEmojis("Food");
        Assert.Contains("🍔", foodEmojis);
        Assert.Contains("🍕", foodEmojis);

        // 5. Search by exact emoji
        var exact = EmojiData.SearchEmojis("🎉");
        Assert.Contains("🎉", exact);

        // 6. Search by keyword
        var heartSearch = EmojiData.SearchEmojis("heart");
        Assert.Contains("❤️", heartSearch);

        var partySearch = EmojiData.SearchEmojis("celebration");
        Assert.Contains("🎉", partySearch);
    }


    [AvaloniaFact]
    public void LoginView_ConnectButton_AlignmentIsCentered()
    {
        var loginView = new LoginView();
        var connectBtn = loginView.FindControl<Button>("ConnectButton");
        Assert.NotNull(connectBtn);
        Assert.Equal(HorizontalAlignment.Center, connectBtn.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, connectBtn.VerticalContentAlignment);
        Assert.Equal(new Thickness(0), connectBtn.Padding);
    }

    [AvaloniaFact]
    public void LoginView_EnterInUsernameField_HandledAndFocusesPassword()
    {
        var loginView = new LoginView();
        Assert.NotNull(loginView.JidTextBoxControl);
        Assert.NotNull(loginView.PasswordTextBoxControl);

        // Key that is not Enter should not be handled
        var nonEnterArgs = new KeyEventArgs { Key = Key.Tab, RoutedEvent = InputElement.KeyDownEvent };
        loginView.OnJidKeyDown(loginView.JidTextBoxControl, nonEnterArgs);
        Assert.False(nonEnterArgs.Handled);

        // Enter key should be handled
        var enterArgs = new KeyEventArgs { Key = Key.Enter, RoutedEvent = InputElement.KeyDownEvent };
        loginView.OnJidKeyDown(loginView.JidTextBoxControl, enterArgs);
        Assert.True(enterArgs.Handled);
    }

    [AvaloniaFact]
    public void LoginView_EnterInPasswordField_TriggersConnect()
    {
        bool loginInvoked = false;
        var vm = new LoginViewModel(profile =>
        {
            loginInvoked = true;
            return Task.FromResult(true);
        })
        {
            Jid = "alice@xmpp.org",
            Password = "secretpassword"
        };

        var loginView = new LoginView { DataContext = vm };
        Assert.NotNull(loginView.PasswordTextBoxControl);

        // Non-enter key should not trigger connect
        var nonEnterArgs = new KeyEventArgs { Key = Key.Space, RoutedEvent = InputElement.KeyDownEvent };
        loginView.OnPasswordKeyDown(loginView.PasswordTextBoxControl, nonEnterArgs);
        Assert.False(nonEnterArgs.Handled);
        Assert.False(loginInvoked);

        // Enter key should trigger connect
        var enterArgs = new KeyEventArgs { Key = Key.Enter, RoutedEvent = InputElement.KeyDownEvent };
        loginView.OnPasswordKeyDown(loginView.PasswordTextBoxControl, enterArgs);
        Assert.True(enterArgs.Handled);
        Assert.True(loginInvoked);
    }

    [Fact]
    public void AppVersionHelper_ResolvesValidVersion()
    {
        try
        {
            AppVersionHelper.SetVersionOverrideForTesting(null);
            var version = AppVersionHelper.Version;
            Assert.NotNull(version);
            Assert.StartsWith("v", version);

            var display = AppVersionHelper.DisplayString;
            Assert.StartsWith("NetChatx v", display);
        }
        finally
        {
            AppVersionHelper.SetVersionOverrideForTesting(null);
        }
    }

    [Fact]
    public void AppVersionHelper_HandlesOverridesAndFormatting()
    {
        try
        {
            AppVersionHelper.SetVersionOverrideForTesting("1.2.3");
            Assert.Equal("v1.2.3", AppVersionHelper.Version);
            Assert.Equal("NetChatx v1.2.3", AppVersionHelper.DisplayString);

            AppVersionHelper.SetVersionOverrideForTesting("v2.0.0");
            Assert.Equal("v2.0.0", AppVersionHelper.Version);
            Assert.Equal("NetChatx v2.0.0", AppVersionHelper.DisplayString);

            AppVersionHelper.SetVersionOverrideForTesting("0.3.0.0");
            Assert.Equal("v0.3.0", AppVersionHelper.Version);
            Assert.Equal("NetChatx v0.3.0", AppVersionHelper.DisplayString);

            AppVersionHelper.SetVersionOverrideForTesting("v0.3.0.0");
            Assert.Equal("v0.3.0", AppVersionHelper.Version);

            AppVersionHelper.SetVersionOverrideForTesting("0.0.0.0");
            Assert.Equal("v0.0.0", AppVersionHelper.Version);
            Assert.Equal("NetChatx v0.0.0", AppVersionHelper.DisplayString);
        }
        finally
        {
            AppVersionHelper.SetVersionOverrideForTesting(null);
        }
    }

    [Fact]
    public void MainWindowViewModel_ExposesDynamicAppVersion()
    {
        try
        {
            AppVersionHelper.SetVersionOverrideForTesting("1.5.0");
            var vm = new MainWindowViewModel();
            Assert.Equal("v1.5.0", vm.AppVersion);
            Assert.Equal("NetChatx v1.5.0", vm.AppVersionDisplay);
        }
        finally
        {
            AppVersionHelper.SetVersionOverrideForTesting(null);
        }
    }
}
