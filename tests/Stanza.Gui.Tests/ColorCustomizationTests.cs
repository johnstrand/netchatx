using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Stanza.Gui.Converters;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class ColorCustomizationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly SettingsRepository _settingsRepo;

    public ColorCustomizationTests()
    {
        _dbPath = $"test_colors_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _settingsRepo = new SettingsRepository(_dbContext);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void NormalizeHex_WithVariousInputs_NormalizesCorrectly()
    {
        // Null and invalid inputs fallback
        Assert.Equal("#00F0FF", ThemeManager.NormalizeHex(null));
        Assert.Equal("#00F0FF", ThemeManager.NormalizeHex(""));
        Assert.Equal("#00F0FF", ThemeManager.NormalizeHex("   "));
        Assert.Equal("#00F0FF", ThemeManager.NormalizeHex("not-a-color"));

        // 3-char hex with and without #
        Assert.Equal("#112233", ThemeManager.NormalizeHex("#123"));
        Assert.Equal("#AABBCC", ThemeManager.NormalizeHex("abc"));

        // 6-char hex with and without #
        Assert.Equal("#FF5500", ThemeManager.NormalizeHex("#FF5500"));
        Assert.Equal("#10B981", ThemeManager.NormalizeHex("10B981"));

        // 8-char ARGB hex strips alpha
        Assert.Equal("#112233", ThemeManager.NormalizeHex("#FF112233"));
    }

    [AvaloniaFact]
    public void ApplyAccentPalette_WithCuratedAndCustomColors_UpdatesResources()
    {
        var app = Application.Current!;

        // Test each curated preset
        foreach (var hex in ThemeManager.CuratedAccents)
        {
            ThemeManager.ApplyAccentPalette(hex);
            var brush = app.Resources["StanzaCyanBrush"] as SolidColorBrush;
            Assert.NotNull(brush);
            Assert.Equal(Color.Parse(hex), brush.Color);
            Assert.True(app.Resources.ContainsKey("StanzaCyberGradient"));
            Assert.True(app.Resources.ContainsKey("StanzaCyberGradientHover"));
        }

        // Test arbitrary custom hex
        var customHex = "#9333EA";
        ThemeManager.ApplyAccentPalette(customHex);
        var customBrush = app.Resources["StanzaCyanBrush"] as SolidColorBrush;
        Assert.NotNull(customBrush);
        Assert.Equal(Color.Parse(customHex), customBrush.Color);
        Assert.True(app.Resources.ContainsKey("StanzaCyberGradient"));
        Assert.True(app.Resources.ContainsKey("StanzaCyberGradientHover"));
    }

    [Fact]
    public async Task SettingsRepository_BubbleTextColor_DefaultsAndPersistenceWork()
    {
        var account = "user@test.org";

        // Defaults
        var defaultOut = await _settingsRepo.GetOutboundBubbleTextColorAsync(account);
        var defaultIn = await _settingsRepo.GetInboundBubbleTextColorAsync(account);
        Assert.Equal(SettingsRepository.DefaultOutboundBubbleTextColor, defaultOut);
        Assert.Equal(SettingsRepository.DefaultInboundBubbleTextColor, defaultIn);

        // Update outbound text color
        await _settingsRepo.SetOutboundBubbleTextColorAsync(account, "#0F172A");
        Assert.Equal("#0F172A", await _settingsRepo.GetOutboundBubbleTextColorAsync(account));

        // Update inbound text color
        await _settingsRepo.SetInboundBubbleTextColorAsync(account, "#FFFFFF");
        Assert.Equal("#FFFFFF", await _settingsRepo.GetInboundBubbleTextColorAsync(account));
    }

    [Fact]
    public async Task SettingsViewModel_ColorCustomization_PropertiesAndCommandsWork()
    {
        var account = "testuser@domain.com";

        var vm = new SettingsViewModel(_settingsRepo, account);

        await vm.LoadSettingsAsync();

        // 1. Curated Accent Selection
        vm.SelectAccentCommand.Execute(ThemeManager.AccentPurple);
        Assert.Equal(ThemeManager.AccentPurple, vm.AccentColor);
        Assert.True(vm.IsPurpleSelected);
        Assert.False(vm.IsCyanSelected);
        Assert.False(vm.IsCustomAccentSelected);
        Assert.Equal("Neon Purple (#A855F7)", vm.SelectedAccentName);

        // 2. Custom Accent Selection
        vm.SelectAccentCommand.Execute("#123456");
        Assert.Equal("#123456", vm.AccentColor);
        Assert.True(vm.IsCustomAccentSelected);
        Assert.Equal("Custom Accent (#123456)", vm.SelectedAccentName);

        // 3. Match Accent to Outbound Bubble
        vm.MatchAccentBubbleColorCommand.Execute(null);
        Assert.Equal("#123456", vm.OutboundBubbleColor);

        // 4. Outbound Text Color
        vm.SelectOutboundBubbleTextColorCommand.Execute("#0F172A");
        Assert.Equal("#0F172A", vm.OutboundBubbleTextColor);
        Assert.True(vm.IsOutboundTextDarkSlateSelected);
        Assert.False(vm.IsOutboundTextWhiteSelected);
        Assert.NotNull(vm.PreviewOutboundTextBrush);

        // 5. Inbound Text Color
        vm.SelectInboundBubbleTextColorCommand.Execute("#FFFFFF");
        Assert.Equal("#FFFFFF", vm.InboundBubbleTextColor);
        Assert.True(vm.IsInboundTextWhiteSelected);
        Assert.False(vm.IsInboundTextCrispLightSelected);
        Assert.NotNull(vm.PreviewInboundTextBrush);

        // 6. Reset Defaults restores defaults
        await vm.ResetDefaultsAsync();
        Assert.Equal(SettingsRepository.DefaultOutboundBubbleTextColor, vm.OutboundBubbleTextColor);
        Assert.Equal(SettingsRepository.DefaultInboundBubbleTextColor, vm.InboundBubbleTextColor);
        Assert.True(vm.IsOutboundTextWhiteSelected);
        Assert.True(vm.IsInboundTextCrispLightSelected);
    }

    [Fact]
    public void DirectionToForegroundConverter_SetColors_UpdatesBrushesProperly()
    {
        DirectionToForegroundConverter.SetColors("#AABBCC", "#112233");

        var outBrush = DirectionToForegroundConverter.OutboundBrush as SolidColorBrush;
        var inBrush = DirectionToForegroundConverter.InboundBrush as SolidColorBrush;

        Assert.NotNull(outBrush);
        Assert.NotNull(inBrush);
        Assert.Equal(Color.Parse("#AABBCC"), outBrush.Color);
        Assert.Equal(Color.Parse("#112233"), inBrush.Color);

        var conv = DirectionToForegroundConverter.Instance;
        var convertedOut = conv.Convert(MessageDirection.Outbound, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture) as SolidColorBrush;
        var convertedIn = conv.Convert(MessageDirection.Inbound, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture) as SolidColorBrush;

        Assert.Equal(outBrush.Color, convertedOut?.Color);
        Assert.Equal(inBrush.Color, convertedIn?.Color);
    }
}
