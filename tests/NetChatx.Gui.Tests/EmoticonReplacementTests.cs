using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NetChatx.Gui.Helpers;
using NetChatx.Gui.ViewModels;
using NetChatx.Storage;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

public class EmoticonReplacementTests
{
    [Fact]
    public void EmoticonReplacer_ReplacesStandardEmoticons()
    {
        var mappings = SettingsRepository.DefaultEmoticonMappings;

        var (result1, replaced1) = EmoticonReplacer.ReplaceEmoticons("Hello :-)", mappings);
        Assert.True(replaced1);
        Assert.Equal("Hello 🙂", result1);

        var (result2, replaced2) = EmoticonReplacer.ReplaceEmoticons("Good morning :) and :D!", mappings);
        Assert.True(replaced2);
        Assert.Equal("Good morning 🙂 and 😃!", result2);

        var (result3, replaced3) = EmoticonReplacer.ReplaceEmoticons("I <3 NetChatx!", mappings);
        Assert.True(replaced3);
        Assert.Equal("I ❤️ NetChatx!", result3);
    }

    [Fact]
    public void EmoticonReplacer_RespectsOrderAndLongerShortcutsFirst()
    {
        var mappings = new List<EmoticonMapping>
        {
            new(":)", "🙂"),
            new(":-)", "😊")
        };

        var (result, replaced) = EmoticonReplacer.ReplaceEmoticons("Hello :-)", mappings);
        Assert.True(replaced);
        Assert.Equal("Hello 😊", result);
    }

    [Fact]
    public void EmoticonReplacer_NoMatchReturnsOriginalText()
    {
        var mappings = SettingsRepository.DefaultEmoticonMappings;
        var (result, replaced) = EmoticonReplacer.ReplaceEmoticons("Hello world", mappings);
        Assert.False(replaced);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public async Task SettingsRepository_PersistsAndRetrievesEmoticonMappings()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"netchatx_emoticon_test_{Guid.NewGuid():N}.db");
        try
        {
            var dbContext = new DatabaseContext(dbPath);
            var repo = new SettingsRepository(dbContext);
            const string accountJid = "user@example.com";

            // Default
            var defaults = await repo.GetEmoticonMappingsAsync(accountJid);
            Assert.NotEmpty(defaults);

            // Set custom mappings
            var custom = new List<EmoticonMapping>
            {
                new(":rocket:", "🚀"),
                new(":-)", "🙂")
            };

            await repo.SetEmoticonMappingsAsync(accountJid, custom);

            var saved = await repo.GetEmoticonMappingsAsync(accountJid);
            Assert.Equal(2, saved.Count);
            Assert.Equal(":rocket:", saved[0].Shortcut);
            Assert.Equal("🚀", saved[0].Emoji);

            // AutoReplace toggle
            Assert.True(await repo.GetAutoReplaceEmoticonsAsync(accountJid));
            await repo.SetAutoReplaceEmoticonsAsync(accountJid, false);
            Assert.False(await repo.GetAutoReplaceEmoticonsAsync(accountJid));

            // Banner dismissed toggle
            Assert.False(await repo.GetEmoticonBannerDismissedAsync(accountJid));
            await repo.SetEmoticonBannerDismissedAsync(accountJid, true);
            Assert.True(await repo.GetEmoticonBannerDismissedAsync(accountJid));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task SettingsViewModel_AddRemoveResetEmoticonMappings()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"netchatx_settings_emoticon_{Guid.NewGuid():N}.db");
        try
        {
            var dbContext = new DatabaseContext(dbPath);
            var repo = new SettingsRepository(dbContext);
            const string accountJid = "test@example.com";

            var vm = new SettingsViewModel(repo, accountJid);
            await vm.LoadSettingsAsync();

            Assert.True(vm.AutoReplaceEmoticons);
            Assert.NotEmpty(vm.EmoticonMappings);

            // Add custom
            vm.NewShortcut = ":party:";
            vm.NewEmoji = "🎉";
            await vm.AddEmoticonMappingAsync();

            Assert.Contains(vm.EmoticonMappings, m => m.Shortcut == ":party:" && m.Emoji == "🎉");
            Assert.Equal(string.Empty, vm.NewShortcut);
            Assert.Equal(string.Empty, vm.NewEmoji);

            // Remove
            var target = vm.EmoticonMappings[0];
            await vm.RemoveEmoticonMappingAsync(target);
            Assert.DoesNotContain(vm.EmoticonMappings, m => m == target);

            // Reset
            await vm.ResetEmoticonMappingsAsync();
            Assert.Equal(SettingsRepository.DefaultEmoticonMappings.Count, vm.EmoticonMappings.Count);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
