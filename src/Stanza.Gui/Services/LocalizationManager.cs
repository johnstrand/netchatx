using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Threading;

namespace Stanza.Gui.Services;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class I18nJsonContext : JsonSerializerContext
{
}

public sealed record LanguageItem(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    public static readonly IReadOnlyList<LanguageItem> SupportedLanguages =
    [
        new("en", "English"),
        new("sv", "Svenska")
    ];

    public IReadOnlyList<LanguageItem> AvailableLanguages => SupportedLanguages;

    private readonly Dictionary<string, Dictionary<string, string>> _locales = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _currentDictionary = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _fallbackDictionary = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguage { get; private set; } = "en";

    public event EventHandler<string>? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => GetString(key);

    public LocalizationManager()
    {
        LoadAllLocales();
        SetLanguage("en", notify: false);
    }

    private void LoadAllLocales()
    {
        var assembly = typeof(LocalizationManager).Assembly;
        foreach (var lang in SupportedLanguages)
        {
            var dict = LoadLocaleFromAssembly(assembly, lang.Code);
            _locales[lang.Code] = dict;
        }

        if (_locales.TryGetValue("en", out var enDict))
        {
            _fallbackDictionary = enDict;
        }
    }

    private static Dictionary<string, string> LoadLocaleFromAssembly(Assembly assembly, string code)
    {
        try
        {
            // First attempt exact expected resource name: Stanza.Assets.Locales.{code}.json
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"Locales.{code}.json", StringComparison.OrdinalIgnoreCase) ||
                                     n.EndsWith($".{code}.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is not null)
                {
                    var parsed = JsonSerializer.Deserialize(stream, I18nJsonContext.Default.DictionaryStringString);
                    if (parsed is not null)
                    {
                        return new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch
        {
            // Ignore failure, fall back to empty dictionary
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void SetLanguage(string languageCode, bool notify = true)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = "en";

        var normalized = SupportedLanguages.FirstOrDefault(l => l.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";

        if (!_locales.TryGetValue(normalized, out var dict))
        {
            dict = _fallbackDictionary;
            normalized = "en";
        }

        CurrentLanguage = normalized;
        _currentDictionary = dict;

        SyncApplicationResources();

        if (notify)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, CurrentLanguage);
        }
    }

    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (!_currentDictionary.TryGetValue(key, out var text) || string.IsNullOrEmpty(text))
        {
            if (!_fallbackDictionary.TryGetValue(key, out text) || string.IsNullOrEmpty(text))
            {
                text = key;
            }
        }

        if (args is { Length: > 0 })
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, text, args);
            }
            catch
            {
                return text;
            }
        }

        return text;
    }

    public void ApplyToApplication(Application? app)
    {
        if (app is null) return;

        void Update()
        {
            foreach (var kvp in _fallbackDictionary)
            {
                app.Resources[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in _currentDictionary)
            {
                app.Resources[kvp.Key] = kvp.Value;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            Dispatcher.UIThread.Post(Update);
        }
    }

    private void SyncApplicationResources()
    {
        ApplyToApplication(Application.Current);
    }

    internal int GetLoadedCount(string code) => _locales.TryGetValue(code, out var d) ? d.Count : 0;
}
