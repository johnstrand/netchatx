using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Transport;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Xunit;

namespace Stanza.Gui.Tests;

public class HelpAndAboutTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;

    public HelpAndAboutTests()
    {
        _dbPath = $"test_help_about_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void HelpViewModel_DefaultStateAndCommands_WorkCorrectly()
    {
        var vm = new HelpViewModel();

        Assert.False(vm.IsOpen);

        vm.Open();
        Assert.True(vm.IsOpen);

        vm.Close();
        Assert.False(vm.IsOpen);

        vm.OpenCommand.Execute(null);
        Assert.True(vm.IsOpen);

        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void AboutViewModel_PropertiesAndFormatting_ArePopulatedCorrectly()
    {
        var vm = new AboutViewModel();

        Assert.False(vm.IsOpen);
        Assert.False(vm.IsCopiedNoticeVisible);

        Assert.Equal("Stanza", vm.AppName);
        Assert.Equal(AppVersionHelper.Version, vm.Version);
        Assert.Equal(AppVersionHelper.DisplayString, vm.DisplayVersion);
        Assert.Equal("Modern Cross-Platform XMPP Client", vm.Tagline);
        Assert.Contains("John Strand", vm.Copyright);
        Assert.Equal("MIT License", vm.License);
        Assert.Equal("https://github.com/johnstrand/netchatx", vm.RepositoryUrl);

        Assert.False(string.IsNullOrWhiteSpace(vm.RuntimeDescription));
        Assert.False(string.IsNullOrWhiteSpace(vm.OsDescription));
        Assert.False(string.IsNullOrWhiteSpace(vm.Architecture));
        Assert.False(string.IsNullOrWhiteSpace(vm.CompilationMode));

        // Core libraries
        Assert.NotEmpty(vm.Libraries);
        Assert.Contains(vm.Libraries, lib => lib.Name == "Avalonia UI" && lib.Version == "11.3.0");
        Assert.Contains(vm.Libraries, lib => lib.Name == "CommunityToolkit.Mvvm" && lib.Version == "8.4.0");
        Assert.Contains(vm.Libraries, lib => lib.Name == "Microsoft.Data.Sqlite" && lib.Version == "10.0.12");
        Assert.Contains(vm.Libraries, lib => lib.Name == "BouncyCastle.Cryptography" && lib.Version == "2.7.0");
        Assert.Contains(vm.Libraries, lib => lib.Name == "Tmds.DBus.Protocol" && lib.Version == "0.21.3");

        // Formatted system info
        var systemInfo = vm.GetFormattedSystemInfo();
        Assert.Contains(vm.DisplayVersion, systemInfo);
        Assert.Contains(vm.OsDescription, systemInfo);
        Assert.Contains(vm.RuntimeDescription, systemInfo);
        Assert.Contains("Avalonia UI", systemInfo);
        Assert.Contains("BouncyCastle.Cryptography", systemInfo);
    }

    [Fact]
    public async Task AboutViewModel_OpenCloseAndCopy_WorkCorrectly()
    {
        var vm = new AboutViewModel();

        vm.Open();
        Assert.True(vm.IsOpen);
        Assert.False(vm.IsCopiedNoticeVisible);

        await vm.CopySystemInfoCommand.ExecuteAsync(null);
        Assert.True(vm.IsCopiedNoticeVisible);

        vm.Close();
        Assert.False(vm.IsOpen);
        Assert.False(vm.IsCopiedNoticeVisible);
    }

    [Fact]
    public void MainChatViewModel_HelpAndAboutCommands_ToggleAndCloseOtherModals()
    {
        var client = new XmppClient(new XmppClientOptions
        {
            Jid = Jid.Parse("testuser@example.com"),
            Password = "password123"
        }, new LoopbackTransport());
        var chatVm = new MainChatViewModel(client, _dbContext, () => Task.CompletedTask);

        Assert.NotNull(chatVm.Help);
        Assert.NotNull(chatVm.About);
        Assert.False(chatVm.Help.IsOpen);
        Assert.False(chatVm.About.IsOpen);

        // Open Help
        chatVm.OpenHelpCommand.Execute(null);
        Assert.True(chatVm.Help.IsOpen);
        Assert.False(chatVm.About.IsOpen);
        Assert.False(chatVm.Settings.IsOpen);

        // Open About (should close Help)
        chatVm.OpenAboutCommand.Execute(null);
        Assert.False(chatVm.Help.IsOpen);
        Assert.True(chatVm.About.IsOpen);
        Assert.False(chatVm.Settings.IsOpen);

        // Close About
        chatVm.CloseAboutCommand.Execute(null);
        Assert.False(chatVm.About.IsOpen);

        // Open Help then open Settings (should close Help)
        chatVm.OpenHelp();
        Assert.True(chatVm.Help.IsOpen);
        chatVm.OpenChatSettings();
        Assert.True(chatVm.Settings.IsOpen);
        Assert.False(chatVm.Help.IsOpen);

        // Open About then open New Chat (should close About)
        chatVm.OpenAbout();
        Assert.True(chatVm.About.IsOpen);
        chatVm.OpenNewChatDialog();
        Assert.True(chatVm.IsNewChatDialogOpen);
        Assert.False(chatVm.About.IsOpen);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AboutView_And_HelpView_Buttons_HaveSufficientDimensionsAndDoNotClip()
    {
        var aboutView = new Stanza.Gui.Views.AboutView { DataContext = new AboutViewModel() };
        var window = new Avalonia.Controls.Window
        {
            Width = 1050,
            Height = 700,
            Content = aboutView
        };
        window.Show();
        aboutView.Measure(new Avalonia.Size(1050, 700));
        aboutView.Arrange(new Avalonia.Rect(0, 0, 1050, 700));

        var buttons = aboutView.GetVisualDescendants().OfType<Avalonia.Controls.Button>().ToList();

        // 1. Header close button is 32x32
        var closeHeaderBtn = buttons.First(b => b.Content?.ToString() == "✕");
        Assert.Equal(32, closeHeaderBtn.Bounds.Width);
        Assert.Equal(32, closeHeaderBtn.Bounds.Height);

        // 2. GitHub repository button has sufficient width and does not clip
        var gitHubBtn = buttons.First(b => b.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == "GitHub"));
        Assert.True(gitHubBtn.Bounds.Width >= 80, $"GitHub button width ({gitHubBtn.Bounds.Width}) should be >= 80px");
        Assert.True(gitHubBtn.Bounds.Height >= 28, $"GitHub button height ({gitHubBtn.Bounds.Height}) should be >= 28px");

        // 3. Copy System Info button has sufficient width
        var copyBtn = buttons.First(b => b.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == "Copy System Info"));
        Assert.True(copyBtn.Bounds.Width >= 140, $"Copy button width ({copyBtn.Bounds.Width}) should be >= 140px");

        // 4. Footer close button has sufficient width
        var closeFooterBtn = buttons.First(b => b.Content?.ToString() == "Close");
        Assert.True(closeFooterBtn.Bounds.Width >= 60, $"Close button width ({closeFooterBtn.Bounds.Width}) should be >= 60px");

        // 5. Verify HelpView "Got it" button also has sufficient width
        var helpView = new Stanza.Gui.Views.HelpView { DataContext = new HelpViewModel() };
        window.Content = helpView;
        helpView.Measure(new Avalonia.Size(1050, 700));
        helpView.Arrange(new Avalonia.Rect(0, 0, 1050, 700));

        var helpButtons = helpView.GetVisualDescendants().OfType<Avalonia.Controls.Button>().ToList();
        var gotItBtn = helpButtons.First(b => b.Content?.ToString() == "Got it");
        Assert.True(gotItBtn.Bounds.Width >= 60, $"Got it button width ({gotItBtn.Bounds.Width}) should be >= 60px");
    }
}
