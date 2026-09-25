using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Gui.Helpers;

namespace Stanza.Gui.ViewModels;

public sealed record LibraryInfo(string Name, string Version, string Purpose, string ProjectUrl);

public sealed partial class AboutViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isCopiedNoticeVisible;

    public string AppName => "Stanza";
    public string Version => AppVersionHelper.Version;
    public string DisplayVersion => AppVersionHelper.DisplayString;
    public string Tagline => "Modern Cross-Platform XMPP Client";
    public string Copyright => "Copyright © 2026 John Strand and Stanza Contributors";
    public string License => "MIT License";
    public string RepositoryUrl => "https://github.com/johnstrand/netchatx";

    public string RuntimeDescription => RuntimeInformation.FrameworkDescription;
    public string OsDescription => RuntimeInformation.OSDescription;
    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
    public string CompilationMode => RuntimeFeature.IsDynamicCodeCompiled ? ".NET JIT" : ".NET Native AOT";

    public IReadOnlyList<LibraryInfo> Libraries { get; } =
    [
        new("Avalonia UI", "11.3.0", "Cross-platform UI rendering engine & XAML framework", "https://avaloniaui.net"),
        new("CommunityToolkit.Mvvm", "8.4.0", "Compile-time MVVM source generators & observable framework", "https://github.com/CommunityToolkit/dotnet"),
        new("Microsoft.Data.Sqlite", "10.0.12", "High-performance SQLite database engine & persistence", "https://github.com/dotnet/efcore"),
        new("BouncyCastle.Cryptography", "2.7.0", "Cryptographic primitives for OMEMO (XEP-0384 / Signal Protocol)", "https://www.bouncycastle.org/csharp/"),
        new("Tmds.DBus.Protocol", "0.21.3", "Native D-Bus IPC & notifications for Linux desktop environments", "https://github.com/tmds/Tmds.DBus")
    ];

    [RelayCommand]
    public void Open()
    {
        IsCopiedNoticeVisible = false;
        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        IsCopiedNoticeVisible = false;
    }

    [RelayCommand]
    public async Task CopySystemInfoAsync()
    {
        var text = GetFormattedSystemInfo();
        await UrlLauncher.CopyToClipboardAsync(text);
        IsCopiedNoticeVisible = true;
    }

    public string GetFormattedSystemInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {DisplayVersion}");
        sb.AppendLine($"- **OS**: {OsDescription} ({Architecture})");
        sb.AppendLine($"- **Runtime**: {RuntimeDescription}");
        sb.AppendLine($"- **Compilation Mode**: {CompilationMode}");
        sb.AppendLine($"- **License**: {License}");
        sb.AppendLine($"- **Repository**: {RepositoryUrl}");
        sb.AppendLine("- **Core Libraries**:");
        foreach (var lib in Libraries)
        {
            sb.AppendLine($"  - {lib.Name} {lib.Version} ({lib.Purpose})");
        }
        return sb.ToString().TrimEnd();
    }

    [RelayCommand]
    public void OpenRepository()
    {
        UrlLauncher.OpenUrl(RepositoryUrl);
    }
}
