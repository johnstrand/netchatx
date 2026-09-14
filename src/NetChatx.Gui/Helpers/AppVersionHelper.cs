using System;
using System.Reflection;

namespace NetChatx.Gui.Helpers;

public static class AppVersionHelper
{
    private static string? _cachedVersion;
    private static string? _versionOverride;

    internal static void SetVersionOverrideForTesting(string? version)
    {
        _versionOverride = version;
        _cachedVersion = null;
    }

    /// <summary>
    /// Gets the application release version with a leading 'v' prefix, e.g. "v1.0.0" or "v0.0.0".
    /// Dynamically reflects the build/release version set via Directory.Build.props or dotnet publish.
    /// </summary>
    public static string Version
    {
        get
        {
            if (_versionOverride is not null)
            {
                return _versionOverride.StartsWith('v') || _versionOverride.StartsWith('V')
                    ? _versionOverride
                    : $"v{_versionOverride}";
            }

            if (_cachedVersion is not null)
                return _cachedVersion;

            string? rawVersion = null;
            var assembly = typeof(AppVersionHelper).Assembly;

            // 1. Try AssemblyInformationalVersion (set by Directory.Build.props / Release pipeline)
            var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
            {
                // Strip git commit hash metadata if present (e.g., "1.0.0+abc1234" -> "1.0.0")
                rawVersion = infoAttr.InformationalVersion.Split('+')[0].Trim();
            }

            // 2. Fall back to AssemblyFileVersion or AssemblyVersion
            if (string.IsNullOrWhiteSpace(rawVersion) || rawVersion == "0.0.0")
            {
                var fileAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (!string.IsNullOrWhiteSpace(fileAttr?.Version))
                {
                    rawVersion = fileAttr.Version.Trim();
                }
                else
                {
                    var asmVersion = assembly.GetName().Version;
                    if (asmVersion is not null)
                    {
                        rawVersion = $"{asmVersion.Major}.{asmVersion.Minor}.{Math.Max(0, asmVersion.Build)}";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                rawVersion = "0.0.0";
            }

            _cachedVersion = rawVersion.StartsWith('v') || rawVersion.StartsWith('V')
                ? rawVersion
                : $"v{rawVersion}";

            return _cachedVersion;
        }
    }

    /// <summary>
    /// Returns the formatted product name with version, e.g. "NetChatx v1.0.0".
    /// </summary>
    public static string DisplayString => $"NetChatx {Version}";
}
