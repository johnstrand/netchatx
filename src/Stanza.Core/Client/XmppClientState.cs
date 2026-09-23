namespace Stanza.Core.Client;

public enum XmppClientState
{
    Disconnected,
    Connecting,
    Connected,
    StartingTls,
    Authenticating,
    Authenticated,
    BindingResource,
    Ready,
    Disconnecting
}
