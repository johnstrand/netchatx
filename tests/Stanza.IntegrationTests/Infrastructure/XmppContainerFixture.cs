using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Xunit;

namespace Stanza.IntegrationTests.Infrastructure;

public sealed class XmppContainerFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string Host => _container?.Hostname ?? "127.0.0.1";
    public int C2sPort => _container?.GetMappedPublicPort(5222) ?? 5222;
    public int DirectTlsPort => _container?.GetMappedPublicPort(5223) ?? 5223;
    public int HttpPort => _container?.GetMappedPublicPort(5280) ?? 5280;

    public XmppClientOptions CreateClientOptions(string username, string password = "password", string? resource = null)
    {
        return new XmppClientOptions
        {
            Jid = Jid.Parse($"{username}@localhost"),
            Password = password,
            Host = Host,
            Port = C2sPort,
            Resource = resource ?? "Stanza",
            AllowUntrustedCertificates = true
        };
    }

    public async Task InitializeAsync()
    {
        if (!IntegrationFactAttribute.IsEnabled())
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var dockerDir = Path.Combine(repoRoot, "docker", "xmpp");

        var image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(dockerDir)
            .WithDockerfile("Dockerfile")
            .WithName("stanza-prosody-dev:latest")
            .WithDeleteIfExists(false)
            .Build();

        await image.CreateAsync();

        _container = new ContainerBuilder("stanza-prosody-dev:latest")
            .WithPortBinding(5222, true)
            .WithPortBinding(5223, true)
            .WithPortBinding(5280, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Starting Prosody server...")
                .UntilInternalTcpPortIsAvailable(5222))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Stanza.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root with Stanza.slnx.");
    }
}
