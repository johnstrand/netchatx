using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Stanza.Gui.Markup;
using Stanza.Gui.Services;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class LocalizationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly SettingsRepository _settingsRepo;

    public LocalizationTests()
    {
        _dbPath = $"test_localization_{Guid.NewGuid():N}.db";
        _dbContext = new DatabaseContext(_dbPath);
        _settingsRepo = new SettingsRepository(_dbContext);

        // Reset LocalizationManager to English for each test
        LocalizationManager.Instance.SetLanguage("en");
    }

    public void Dispose()
    {
        LocalizationManager.Instance.SetLanguage("en");
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void LocalizationManager_DefaultIsEnglish()
    {
        var manager = LocalizationManager.Instance;
        Assert.Equal("en", manager.CurrentLanguage);
        Assert.Equal("Stanza - XMPP Client", manager.GetString("App_Title"));
        Assert.Equal("Stanza - XMPP Client", manager["App_Title"]);
    }

    [Fact]
    public void LocalizationManager_SwitchLanguageToSwedish_Succeeds()
    {
        var manager = LocalizationManager.Instance;
        var propertyChangedFired = false;
        manager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                propertyChangedFired = true;
            }
        };

        manager.SetLanguage("sv");

        Assert.Equal("sv", manager.CurrentLanguage);
        Assert.True(propertyChangedFired);
        Assert.Equal("Stanza - XMPP-klient", manager.GetString("App_Title"));
        Assert.Equal("Avbryt", manager.GetString("Common_Cancel"));
        Assert.Equal("Starta ny chatt", manager.GetString("Dialog_NewChat_Title"));
    }

    [Fact]
    public void LocalizationManager_FormattedStrings_WorkProperly()
    {
        var manager = LocalizationManager.Instance;
        manager.SetLanguage("en");

        var enFormatted = manager.GetString("Search_NoResults", "xyz");
        Assert.Equal("No results for \"xyz\"", enFormatted);

        manager.SetLanguage("sv");
        var svFormatted = manager.GetString("Search_NoResults", "xyz");
        Assert.Equal("Inga resultat för \"xyz\"", svFormatted);
    }

    [Fact]
    public void LocalizationManager_FallbackBehaviors_WorkCorrectly()
    {
        var manager = LocalizationManager.Instance;
        manager.SetLanguage("sv");

        // Non-existent key falls back to the key itself
        var nonExistent = manager.GetString("NonExistent_Key_12345");
        Assert.Equal("NonExistent_Key_12345", nonExistent);
    }

    [Fact]
    public void LocalizationManager_AvailableLanguages_ContainsExpectedLocales()
    {
        var manager = LocalizationManager.Instance;
        var languages = manager.AvailableLanguages;

        Assert.NotNull(languages);
        Assert.Contains(languages, l => l.Code == "en" && l.DisplayName == "English");
        Assert.Contains(languages, l => l.Code == "sv" && l.DisplayName == "Svenska");
    }

    [Fact]
    public void TranslateExtension_CreatesValidDynamicResource()
    {
        var ext = new TranslateExtension("App_Title");
        Assert.Equal("App_Title", ext.Key);

        var dynamicResource = ext.ProvideValue(null!);
        Assert.NotNull(dynamicResource);
    }

    [Fact]
    public async Task SettingsViewModel_LanguageSelectionAndPersistence_WorksCorrectly()
    {
        var account = "user@test.org";
        var languageCallbackInvoked = false;
        string? changedLanguage = null;

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            onLanguageChanged: lang =>
            {
                languageCallbackInvoked = true;
                changedLanguage = lang;
            });

        // Initial default should be English
        Assert.NotNull(vm.SelectedLanguageItem);
        Assert.Equal("en", vm.SelectedLanguageItem.Code);

        // Switch to Swedish
        var svItem = vm.AvailableLanguages.FirstOrDefault(l => l.Code == "sv");
        Assert.NotNull(svItem);

        vm.SelectedLanguageItem = svItem;

        Assert.True(languageCallbackInvoked);
        Assert.Equal("sv", changedLanguage);
        Assert.Equal("sv", LocalizationManager.Instance.CurrentLanguage);

        // Verify persisted to database
        var persistedLang = await _settingsRepo.GetLanguageAsync(account);
        Assert.Equal("sv", persistedLang);

        // New view model loads persisted Swedish language
        var vm2 = new SettingsViewModel(_settingsRepo, account);
        await vm2.LoadSettingsAsync();
        Assert.Equal("sv", vm2.SelectedLanguageItem.Code);

        // Reset defaults restores English
        await vm2.ResetDefaultsAsync();
        Assert.Equal("en", vm2.SelectedLanguageItem.Code);
        Assert.Equal("en", await _settingsRepo.GetLanguageAsync(account));
        Assert.Equal("en", LocalizationManager.Instance.CurrentLanguage);
    }
}
