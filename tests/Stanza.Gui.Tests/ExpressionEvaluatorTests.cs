using System;
using System.IO;
using System.Threading.Tasks;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;
using Stanza.Storage;
using Stanza.Storage.Repositories;
using Xunit;

namespace Stanza.Gui.Tests;

public class ExpressionEvaluatorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _dbContext;
    private readonly SettingsRepository _settingsRepo;

    public ExpressionEvaluatorTests()
    {
        _dbPath = $"test_expr_{Guid.NewGuid():N}.db";
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
    public void EvaluateText_BasicArithmetic_EvaluatesCorrectly()
    {
        Assert.Equal("4", ExpressionEvaluator.EvaluateText("$(1+3)"));
        Assert.Equal("4", ExpressionEvaluator.EvaluateText("$(1 + 3)"));
        Assert.Equal("6", ExpressionEvaluator.EvaluateText("$(10 - 4)"));
        Assert.Equal("6", ExpressionEvaluator.EvaluateText("$(2 * 3)"));
        Assert.Equal("5", ExpressionEvaluator.EvaluateText("$(10 / 2)"));
        Assert.Equal("3", ExpressionEvaluator.EvaluateText("$(7 % 4)"));
        Assert.Equal("1", ExpressionEvaluator.EvaluateText("$(.5 + .5)"));
    }

    [Fact]
    public void EvaluateText_Exponentiation_EvaluatesCorrectly()
    {
        Assert.Equal("8", ExpressionEvaluator.EvaluateText("$(2 ^ 3)"));
        Assert.Equal("8", ExpressionEvaluator.EvaluateText("$(2 ** 3)"));
        // Right-associative exponentiation: 2 ^ 3 ^ 2 = 2 ^ 9 = 512
        Assert.Equal("512", ExpressionEvaluator.EvaluateText("$(2 ^ 3 ^ 2)"));
    }

    [Fact]
    public void EvaluateText_PrecedenceAndParentheses_EvaluatesCorrectly()
    {
        Assert.Equal("14", ExpressionEvaluator.EvaluateText("$(2 + 3 * 4)"));
        Assert.Equal("20", ExpressionEvaluator.EvaluateText("$((2 + 3) * 4)"));
        Assert.Equal("22", ExpressionEvaluator.EvaluateText("$(2 * (3 + (4 * 2)))"));
    }

    [Fact]
    public void EvaluateText_UnaryOperatorsAndNegatives_EvaluatesCorrectly()
    {
        Assert.Equal("5", ExpressionEvaluator.EvaluateText("$(-5 + 10)"));
        Assert.Equal("7", ExpressionEvaluator.EvaluateText("$(10 + -3)"));
        Assert.Equal("-4", ExpressionEvaluator.EvaluateText("$(-sqrt(16))"));
        Assert.Equal("3", ExpressionEvaluator.EvaluateText("$(+3)"));
    }

    [Fact]
    public void EvaluateText_MathFunctions_EvaluatesCorrectly()
    {
        Assert.Equal("4", ExpressionEvaluator.EvaluateText("$(sqrt(16))"));
        Assert.Equal("42", ExpressionEvaluator.EvaluateText("$(abs(-42))"));
        Assert.Equal("3.14", ExpressionEvaluator.EvaluateText("$(round(3.14159, 2))"));
        Assert.Equal("4", ExpressionEvaluator.EvaluateText("$(round(3.7))"));
        Assert.Equal("3", ExpressionEvaluator.EvaluateText("$(floor(3.9))"));
        Assert.Equal("4", ExpressionEvaluator.EvaluateText("$(ceil(3.1))"));
        Assert.Equal("4", ExpressionEvaluator.EvaluateText("$(ceiling(3.1))"));
        Assert.Equal("5", ExpressionEvaluator.EvaluateText("$(min(10, 5))"));
        Assert.Equal("10", ExpressionEvaluator.EvaluateText("$(max(10, 5))"));
        Assert.Equal("8", ExpressionEvaluator.EvaluateText("$(pow(2, 3))"));
        Assert.Equal("0", ExpressionEvaluator.EvaluateText("$(sin(0))"));
        Assert.Equal("1", ExpressionEvaluator.EvaluateText("$(cos(0))"));
        Assert.Equal("0", ExpressionEvaluator.EvaluateText("$(tan(0))"));
        Assert.Equal("1", ExpressionEvaluator.EvaluateText("$(exp(0))"));
        Assert.Equal("1", ExpressionEvaluator.EvaluateText("$(log(e))"));
        Assert.Equal("2", ExpressionEvaluator.EvaluateText("$(log10(100))"));
    }

    [Fact]
    public void EvaluateText_Constants_EvaluatesCorrectly()
    {
        Assert.StartsWith("3.14159265", ExpressionEvaluator.EvaluateText("$(pi)"));
        Assert.StartsWith("2.71828182", ExpressionEvaluator.EvaluateText("$(e)"));
        Assert.StartsWith("6.2831853", ExpressionEvaluator.EvaluateText("$(2 * pi)"));
    }

    [Fact]
    public void EvaluateText_Escaping_PreventsEvaluation()
    {
        // \$ prevents evaluation and turns into $
        Assert.Equal("$(1+3)", ExpressionEvaluator.EvaluateText(@"\$(1+3)"));
        Assert.Equal("Price: $100 and sum: $(2+2)", ExpressionEvaluator.EvaluateText(@"Price: \$100 and sum: \$(2+2)"));

        // Double backslash: escaped backslash becomes literal \, and $ is evaluated
        Assert.Equal(@"\4", ExpressionEvaluator.EvaluateText(@"\\$(1+3)"));

        // Mixed escaped and unescaped
        Assert.Equal("The answer is 4 and not $(1+3)", ExpressionEvaluator.EvaluateText(@"The answer is $(1+3) and not \$(1+3)"));
    }

    [Fact]
    public void EvaluateText_MalformedOrErrorExpressions_RemainUntouched()
    {
        Assert.Equal("$(invalid)", ExpressionEvaluator.EvaluateText("$(invalid)"));
        Assert.Equal("$(1/0)", ExpressionEvaluator.EvaluateText("$(1/0)"));
        Assert.Equal("$(7 % 0)", ExpressionEvaluator.EvaluateText("$(7 % 0)"));
        Assert.Equal("$(sqrt(-4))", ExpressionEvaluator.EvaluateText("$(sqrt(-4))"));
        Assert.Equal("$(1 + )", ExpressionEvaluator.EvaluateText("$(1 + )"));
        Assert.Equal("$(unknown(5))", ExpressionEvaluator.EvaluateText("$(unknown(5))"));
        Assert.Equal("$()", ExpressionEvaluator.EvaluateText("$()"));
        Assert.Equal("$(1 + 2 trailing)", ExpressionEvaluator.EvaluateText("$(1 + 2 trailing)"));
    }

    [Fact]
    public void EvaluateText_InTextReplacement_WorksCorrectly()
    {
        var input = "Total: $(10 + 20) and Tax: $(30 * 0.1) dollars!";
        var expected = "Total: 30 and Tax: 3 dollars!";
        Assert.Equal(expected, ExpressionEvaluator.EvaluateText(input));

        Assert.Equal("", ExpressionEvaluator.EvaluateText(""));
        Assert.Equal("", ExpressionEvaluator.EvaluateText(null));
        Assert.Equal("Plain chat text with no expressions", ExpressionEvaluator.EvaluateText("Plain chat text with no expressions"));
    }

    [Fact]
    public void TryEvaluatePreview_DetectsExpressionsCorrectly()
    {
        Assert.True(ExpressionEvaluator.TryEvaluatePreview("Check $(1+3) out", out var preview1));
        Assert.Equal("Check 4 out", preview1);

        // Untouched expressions return false
        Assert.False(ExpressionEvaluator.TryEvaluatePreview("Check $(invalid) out", out _));

        // Escaped expressions return false
        Assert.False(ExpressionEvaluator.TryEvaluatePreview(@"Check \$(1+3) out", out _));

        // Plain text returns false
        Assert.False(ExpressionEvaluator.TryEvaluatePreview("Hello world", out _));
        Assert.False(ExpressionEvaluator.TryEvaluatePreview("", out _));
    }

    [Fact]
    public async Task SettingsRepository_EvaluateExpressions_DefaultsAndPersistenceWork()
    {
        var account = "user@test.org";

        // Default is true
        var isDefault = await _settingsRepo.GetEvaluateExpressionsAsync(account);
        Assert.True(isDefault);

        // Toggle to false
        await _settingsRepo.SetEvaluateExpressionsAsync(account, false);
        Assert.False(await _settingsRepo.GetEvaluateExpressionsAsync(account));

        // Toggle back to true
        await _settingsRepo.SetEvaluateExpressionsAsync(account, true);
        Assert.True(await _settingsRepo.GetEvaluateExpressionsAsync(account));
    }

    [Fact]
    public async Task SettingsViewModel_EvaluateExpressions_PropertiesAndResetWork()
    {
        var account = "testuser@domain.com";
        bool? callbackValue = null;

        var vm = new SettingsViewModel(
            _settingsRepo,
            account,
            onEvaluateExpressionsChanged: val => callbackValue = val);

        await vm.LoadSettingsAsync();

        Assert.True(vm.EvaluateExpressions);

        // Change setting
        vm.EvaluateExpressions = false;
        Assert.False(await _settingsRepo.GetEvaluateExpressionsAsync(account));
        Assert.False(callbackValue);

        // Reset to defaults
        await vm.ResetDefaultsAsync();
        Assert.True(vm.EvaluateExpressions);
        Assert.True(await _settingsRepo.GetEvaluateExpressionsAsync(account));
        Assert.True(callbackValue);
    }
}
