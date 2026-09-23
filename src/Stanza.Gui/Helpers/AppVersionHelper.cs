using System;
using System.Reflection;

namespace Stanza.Gui.Helpers;

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
                var overrideVal = _versionOverride.Trim();
                var hasV = overrideVal.StartsWith('v') || overrideVal.StartsWith('V');
                var v = hasV ? overrideVal[1..] : overrideVal;

                if (v == "0.0.0.0")
                {
                    v = "0.0.0";
                }
                else if (System.Version.TryParse(v, out var parsed) && parsed.Revision == 0 && v.Split('.').Length == 4)
                {
                    v = $"{parsed.Major}.{parsed.Minor}.{Math.Max(0, parsed.Build)}";
                }

                return $"v{v}";
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
            if (string.IsNullOrWhiteSpace(rawVersion) || rawVersion == "0.0.0" || rawVersion == "0.0.0.0")
            {
                var fileAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (!string.IsNullOrWhiteSpace(fileAttr?.Version) && fileAttr.Version.Trim() != "0.0.0.0" && fileAttr.Version.Trim() != "0.0.0")
                {
                    rawVersion = fileAttr.Version.Trim();
                }
                else
                {
                    var asmVersion = assembly.GetName().Version;
                    if (asmVersion is not null && asmVersion.ToString() != "0.0.0.0")
                    {
                        rawVersion = $"{asmVersion.Major}.{asmVersion.Minor}.{Math.Max(0, asmVersion.Build)}";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawVersion) || rawVersion == "0.0.0.0")
            {
                rawVersion = "0.0.0";
            }
            else if (System.Version.TryParse(rawVersion, out var parsedVer) && parsedVer.Revision == 0 && rawVersion.Split('.').Length == 4)
            {
                rawVersion = $"{parsedVer.Major}.{parsedVer.Minor}.{Math.Max(0, parsedVer.Build)}";
            }

            _cachedVersion = rawVersion.StartsWith('v') || rawVersion.StartsWith('V')
                ? rawVersion
                : $"v{rawVersion}";

            return _cachedVersion;
        }
    }

    /// <summary>
    /// Returns the formatted product name with version, e.g. "Stanza v1.0.0".
    /// </summary>
    public static string DisplayString => $"Stanza {Version}";
}
