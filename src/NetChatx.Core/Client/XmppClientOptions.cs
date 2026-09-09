namespace NetChatx.Core.Client;

public sealed class XmppClientOptions
{
    public required Jid Jid { get; init; }
    public required string Password { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; } = 5222;
    public bool UseDirectTls { get; init; } = false;
    public string Resource { get; init; } = "NetChatx";
    public bool AutoReconnect { get; init; } = true;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
