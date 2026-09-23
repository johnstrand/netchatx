using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Stanza.Gui.ViewModels;

public sealed record CodeLanguageItem(string Name, string Identifier)
{
    public override string ToString() => Name;
}

public sealed partial class CodeBlockEditorViewModel : ViewModelBase
{
    public static readonly CodeLanguageItem PlainTextLanguage = new("Plain text", string.Empty);
    public static readonly CodeLanguageItem CustomLanguage = new("Custom...", "custom");

    public static readonly IReadOnlyList<CodeLanguageItem> DefaultLanguages =
    [
        PlainTextLanguage, // Default: no language identifier
        new("C#", "csharp"),
        new("JavaScript", "javascript"),
        new("TypeScript", "typescript"),
        new("Python", "python"),
        new("HTML", "html"),
        new("CSS", "css"),
        new("JSON", "json"),
        new("XML", "xml"),
        new("SQL", "sql"),
        new("Bash / Shell", "bash"),
        new("PowerShell", "powershell"),
        new("C", "c"),
        new("C++", "cpp"),
        new("Rust", "rust"),
        new("Go", "go"),
        new("Java", "java"),
        new("Kotlin", "kotlin"),
        new("Swift", "swift"),
        new("PHP", "php"),
        new("Ruby", "ruby"),
        new("YAML", "yaml"),
        new("Markdown", "markdown"),
        new("Dockerfile", "dockerfile"),
        new("GraphQL", "graphql"),
        new("Lua", "lua"),
        new("R", "r"),
        new("Dart", "dart"),
        CustomLanguage
    ];

    private Action<string>? _onInsert;
    private Action? _onCancel;

    public IReadOnlyList<CodeLanguageItem> AvailableLanguages => DefaultLanguages;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineCount))]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(LineNumbersText))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomLanguage))]
    private CodeLanguageItem _selectedLanguage = PlainTextLanguage;

    [ObservableProperty]
    private string _customLanguageIdentifier = string.Empty;

    public bool IsCustomLanguage => SelectedLanguage == CustomLanguage;

    public int LineCount
    {
        get
        {
            if (string.IsNullOrEmpty(Code)) return 1;
            var count = 1;
            for (int i = 0; i < Code.Length; i++)
            {
                if (Code[i] == '\n') count++;
            }
            return count;
        }
    }

    public int CharacterCount => Code?.Length ?? 0;

    public string StatusText => $"{LineCount} line{(LineCount == 1 ? "" : "s")}, {CharacterCount} character{(CharacterCount == 1 ? "" : "s")}";

    public string LineNumbersText
    {
        get
        {
            var count = LineCount;
            var sb = new StringBuilder();
            for (int i = 1; i <= count; i++)
            {
                if (i > 1) sb.Append('\n');
                sb.Append(i);
            }
            return sb.ToString();
        }
    }

    public void Open(string initialCode = "", string? languageHint = null, Action<string>? onInsert = null, Action? onCancel = null)
    {
        _onInsert = onInsert;
        _onCancel = onCancel;
        Code = initialCode ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(languageHint))
        {
            SetLanguageFromHint(languageHint.Trim());
        }
        else
        {
            SelectedLanguage = PlainTextLanguage;
            CustomLanguageIdentifier = string.Empty;
        }

        IsOpen = true;
    }

    public void SetLanguageFromHint(string hint)
    {
        var normalized = hint.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized is "text" or "plain" or "plaintext" or "none")
        {
            SelectedLanguage = PlainTextLanguage;
            return;
        }

        // Direct match against Identifier or Name
        var match = AvailableLanguages.FirstOrDefault(l =>
            l != CustomLanguage &&
            (l.Identifier.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
             l.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)));

        if (match is not null)
        {
            SelectedLanguage = match;
            return;
        }

        // Common aliases
        match = normalized switch
        {
            "cs" or "c#" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "csharp"),
            "js" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "javascript"),
            "ts" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "typescript"),
            "py" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "python"),
            "sh" or "shell" or "zsh" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "bash"),
            "ps" or "ps1" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "powershell"),
            "yml" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "yaml"),
            "md" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "markdown"),
            "htm" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "html"),
            "rb" => AvailableLanguages.FirstOrDefault(l => l.Identifier == "ruby"),
            _ => null
        };

        if (match is not null)
        {
            SelectedLanguage = match;
            return;
        }

        // Custom language identifier
        SelectedLanguage = CustomLanguage;
        CustomLanguageIdentifier = normalized;
    }

    public string GetEffectiveLanguageIdentifier()
    {
        if (SelectedLanguage == CustomLanguage)
        {
            return CustomLanguageIdentifier.Trim();
        }

        return SelectedLanguage?.Identifier?.Trim() ?? string.Empty;
    }

    public string GenerateMarkdown()
    {
        var id = GetEffectiveLanguageIdentifier();
        var trimmedCode = (Code ?? string.Empty).TrimEnd('\r', '\n');

        if (string.IsNullOrWhiteSpace(id) || id.Equals("text", StringComparison.OrdinalIgnoreCase) || id.Equals("plain text", StringComparison.OrdinalIgnoreCase))
        {
            return $"```\n{trimmedCode}\n```";
        }
        else
        {
            return $"```{id}\n{trimmedCode}\n```";
        }
    }

    [RelayCommand]
    public void Insert()
    {
        var markdown = GenerateMarkdown();
        var callback = _onInsert;
        Close();
        callback?.Invoke(markdown);
    }

    [RelayCommand]
    public void Cancel()
    {
        var callback = _onCancel;
        Close();
        callback?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Code = string.Empty;
        SelectedLanguage = PlainTextLanguage;
        CustomLanguageIdentifier = string.Empty;
        _onInsert = null;
        _onCancel = null;
    }
}
