using System;
using System.IO;
using System.Linq;
using NetChatx.Core;
using NetChatx.Core.Client;
using NetChatx.Core.Transport;
using NetChatx.Gui.ViewModels;
using NetChatx.Gui.Views;
using NetChatx.Storage;
using NetChatx.Storage.Repositories;
using Xunit;

namespace NetChatx.Gui.Tests;

public class CodeBlockEditorTests
{
    [Fact]
    public void DefaultLanguage_IsPlainText_WithEmptyIdentifier()
    {
        var vm = new CodeBlockEditorViewModel();

        Assert.Equal("Plain text", vm.SelectedLanguage.Name);
        Assert.Equal(string.Empty, vm.SelectedLanguage.Identifier);
        Assert.False(vm.IsCustomLanguage);
    }

    [Fact]
    public void GenerateMarkdown_WithPlainText_DoesNotIncludeLanguageIdentifier()
    {
        var vm = new CodeBlockEditorViewModel
        {
            Code = "Console.WriteLine(\"Hello, World!\");"
        };

        string markdown = vm.GenerateMarkdown();

        Assert.Equal("```\nConsole.WriteLine(\"Hello, World!\");\n```", markdown);
    }

    [Fact]
    public void GenerateMarkdown_WithCSharp_IncludesCSharpIdentifier()
    {
        var vm = new CodeBlockEditorViewModel();
        vm.SetLanguageFromHint("csharp");
        vm.Code = "int x = 42;";

        string markdown = vm.GenerateMarkdown();

        Assert.Equal("```csharp\nint x = 42;\n```", markdown);
    }

    [Fact]
    public void GenerateMarkdown_WithPython_IncludesPythonIdentifier()
    {
        var vm = new CodeBlockEditorViewModel();
        vm.SetLanguageFromHint("python");
        vm.Code = "print(\"hello\")";

        string markdown = vm.GenerateMarkdown();

        Assert.Equal("```python\nprint(\"hello\")\n```", markdown);
    }

    [Fact]
    public void GenerateMarkdown_WithCustomLanguage_IncludesCustomIdentifier()
    {
        var vm = new CodeBlockEditorViewModel();
        vm.SelectedLanguage = CodeBlockEditorViewModel.CustomLanguage;
        vm.CustomLanguageIdentifier = "zig";
        vm.Code = "pub fn main() void {}";

        string markdown = vm.GenerateMarkdown();

        Assert.Equal("```zig\npub fn main() void {}\n```", markdown);
    }

    [Fact]
    public void GenerateMarkdown_TrimsTrailingNewlinesFromCode()
    {
        var vm = new CodeBlockEditorViewModel
        {
            Code = "line1\nline2\r\n\n"
        };

        string markdown = vm.GenerateMarkdown();

        Assert.Equal("```\nline1\nline2\n```", markdown);
    }

    [Theory]
    [InlineData("csharp", "csharp")]
    [InlineData("cs", "csharp")]
    [InlineData("py", "python")]
    [InlineData("python", "python")]
    [InlineData("js", "javascript")]
    [InlineData("javascript", "javascript")]
    [InlineData("ts", "typescript")]
    [InlineData("sh", "bash")]
    [InlineData("bash", "bash")]
    [InlineData("powershell", "powershell")]
    [InlineData("ps1", "powershell")]
    [InlineData("html", "html")]
    [InlineData("json", "json")]
    [InlineData("rust", "rust")]
    public void SetLanguageFromHint_MatchesKnownLanguagesAndAliases(string hint, string expectedIdentifier)
    {
        var vm = new CodeBlockEditorViewModel();
        vm.SetLanguageFromHint(hint);

        Assert.Equal(expectedIdentifier, vm.SelectedLanguage.Identifier);
        Assert.False(vm.IsCustomLanguage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("text")]
    [InlineData("plain")]
    [InlineData("plaintext")]
    [InlineData("none")]
    public void SetLanguageFromHint_DefaultsToPlainText(string hint)
    {
        var vm = new CodeBlockEditorViewModel();
        vm.SetLanguageFromHint(hint);

        Assert.Equal(string.Empty, vm.SelectedLanguage.Identifier);
        Assert.Equal("Plain text", vm.SelectedLanguage.Name);
    }

    [Fact]
    public void SetLanguageFromHint_WithUnknownHint_SetsCustomLanguage()
    {
        var vm = new CodeBlockEditorViewModel();
        vm.SetLanguageFromHint("elixir");

        Assert.True(vm.IsCustomLanguage);
        Assert.Equal("elixir", vm.CustomLanguageIdentifier);
        Assert.Equal("elixir", vm.GetEffectiveLanguageIdentifier());
    }

    [Fact]
    public void Open_InitializesState_AndSelectsLanguageFromHint()
    {
        var vm = new CodeBlockEditorViewModel();
        vm.Open("initial snippet", "python");

        Assert.True(vm.IsOpen);
        Assert.Equal("initial snippet", vm.Code);
        Assert.Equal("python", vm.SelectedLanguage.Identifier);
    }

    [Fact]
    public void InsertCommand_InvokesCallback_WithMarkdown_AndCloses()
    {
        var vm = new CodeBlockEditorViewModel();
        string? insertedMarkdown = null;

        vm.Open("const a = 10;", "js", md => insertedMarkdown = md);
        vm.InsertCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.Equal("```javascript\nconst a = 10;\n```", insertedMarkdown);
    }

    [Fact]
    public void CancelCommand_ClosesWithoutInvokingCallback()
    {
        var vm = new CodeBlockEditorViewModel();
        bool callbackInvoked = false;

        vm.Open("some code", null, _ => callbackInvoked = true);
        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void LineCount_CharacterCount_AndStatusText_CalculateProperly()
    {
        var vm = new CodeBlockEditorViewModel
        {
            Code = "alpha\nbeta\ngamma"
        };

        Assert.Equal(3, vm.LineCount);
        Assert.Equal(16, vm.CharacterCount);
        Assert.Equal("3 lines, 16 characters", vm.StatusText);
        Assert.Equal("1\n2\n3", vm.LineNumbersText);

        vm.Code = "single";
        Assert.Equal(1, vm.LineCount);
        Assert.Equal(6, vm.CharacterCount);
        Assert.Equal("1 line, 6 characters", vm.StatusText);
        Assert.Equal("1", vm.LineNumbersText);
    }

    private static ChatConversationViewModel CreateTestConversation(string inputText = "")
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_cb_{Guid.NewGuid():N}.db");
        var db = new DatabaseContext(dbPath);
        var repo = new MessageRepository(db);

        return new ChatConversationViewModel(
            "user@example.com",
            "chat-1",
            "Chat",
            Jid.Parse("peer@example.com"),
            false,
            repo)
        {
            InputText = inputText
        };
    }

    [Fact]
    public void ChatConversationViewModel_InjectCodeBlock_EmptyInputText()
    {
        var conv = CreateTestConversation();

        conv.InjectCodeBlock("```\nvar x = 1;\n```");

        Assert.Equal("```\nvar x = 1;\n```", conv.InputText);
    }

    [Fact]
    public void ChatConversationViewModel_InjectCodeBlock_ExistingText_AppendsWithNewline()
    {
        var conv = CreateTestConversation("Here is the implementation:");

        conv.InjectCodeBlock("```csharp\nreturn true;\n```");

        Assert.Equal("Here is the implementation:\n```csharp\nreturn true;\n```", conv.InputText);
    }

    [Fact]
    public void ChatConversationViewModel_InjectCodeBlock_MidTextInsertion_InsertsProperly()
    {
        var conv = CreateTestConversation("Prefix Suffix");

        conv.InjectCodeBlock("```\nhello\n```", insertionIndex: 7);

        Assert.Equal("Prefix \n```\nhello\n```\nSuffix", conv.InputText);
    }

    private static MainChatViewModel CreateTestMainChatViewModel()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_cb_main_{Guid.NewGuid():N}.db");
        var db = new DatabaseContext(dbPath);
        var options = new XmppClientOptions { Jid = Jid.Parse("user@test.org"), Password = "pw" };
        var client = new XmppClient(options, new LoopbackTransport());
        return new MainChatViewModel(client, db, () => System.Threading.Tasks.Task.CompletedTask);
    }

    [Fact]
    public void MainChatViewModel_OpenCodeBlockEditor_OpensAndInjectsIntoActiveConversation()
    {
        var mainVm = CreateTestMainChatViewModel();

        var conv = CreateTestConversation();
        mainVm.ActiveConversation = conv;

        mainVm.OpenCodeBlockEditor("int a = 1;", "csharp");

        Assert.True(mainVm.CodeBlockEditor.IsOpen);
        Assert.Equal("int a = 1;", mainVm.CodeBlockEditor.Code);
        Assert.Equal("csharp", mainVm.CodeBlockEditor.SelectedLanguage.Identifier);

        mainVm.CodeBlockEditor.InsertCommand.Execute(null);

        Assert.False(mainVm.CodeBlockEditor.IsOpen);
        Assert.Equal("```csharp\nint a = 1;\n```", conv.InputText);
    }

    [Fact]
    public void MainChatViewModel_OpenCodeBlockEditor_DefaultsToPlainText_WithoutLanguageIdentifier()
    {
        var mainVm = CreateTestMainChatViewModel();

        var conv = CreateTestConversation();
        mainVm.ActiveConversation = conv;

        mainVm.OpenCodeBlockEditor("plain snippet");

        Assert.True(mainVm.CodeBlockEditor.IsOpen);
        Assert.Equal("Plain text", mainVm.CodeBlockEditor.SelectedLanguage.Name);
        Assert.Equal(string.Empty, mainVm.CodeBlockEditor.SelectedLanguage.Identifier);

        mainVm.CodeBlockEditor.InsertCommand.Execute(null);

        Assert.False(mainVm.CodeBlockEditor.IsOpen);
        Assert.Equal("```\nplain snippet\n```", conv.InputText);
    }

    [Fact]
    public void MainChatViewModel_ChangingActiveConversation_CancelsCodeBlockEditor()
    {
        var mainVm = CreateTestMainChatViewModel();

        var conv1 = CreateTestConversation();
        var conv2 = CreateTestConversation();
        mainVm.ActiveConversation = conv1;

        mainVm.OpenCodeBlockEditor("draft");
        Assert.True(mainVm.CodeBlockEditor.IsOpen);

        mainVm.ActiveConversation = conv2;
        Assert.False(mainVm.CodeBlockEditor.IsOpen);
    }

    [Fact]
    public void CodeBlockEditorView_Instantiates_Successfully()
    {
        var view = new CodeBlockEditorView();
        Assert.NotNull(view);
    }
}
