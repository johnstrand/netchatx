using Xunit;

namespace Stanza.IntegrationTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public const string EnvironmentVariableName = "STANZA_INTEGRATION_TESTS";

    public IntegrationFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = $"Integration tests are skipped by default. Set {EnvironmentVariableName}=1 (or true) to enable.";
        }
    }

    public static bool IsEnabled()
    {
        var val = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.Equals(val, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }
}
