using Stanza.Core.Xml;

namespace Stanza.Core.Client;

/// <summary>
/// Defines a handler for stream management negotiation and stream resumption (XEP-0198).
/// </summary>
public interface IStreamResumptionHandler
{
    /// <summary>
    /// Gets whether a previous stream management session can be resumed.
    /// </summary>
    bool CanResume { get; }

    /// <summary>
    /// Attempts to resume the stream using pre-auth stream features.
    /// </summary>
    /// <returns>True if the stream was successfully resumed; false if resumption failed.</returns>
    Task<bool> TryResumeAsync(
        XmppClient client,
        XmppElement streamFeatures,
        Func<XmppElement, Task> sendRawAsync,
        Func<Task<XmppElement>> readNextElementAsync,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when resumption fails (e.g. server returned &lt;failed/&gt;).
    /// </summary>
    Task OnResumptionFailedAsync(XmppClient client, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked post-auth and post-bind to negotiate or enable stream management if advertised.
    /// </summary>
    Task OnStreamNegotiatedAsync(
        XmppClient client,
        XmppElement streamFeatures,
        Func<XmppElement, Task> sendRawAsync,
        Func<Task<XmppElement>> readNextElementAsync,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when the client intentionally disconnects cleanly.
    /// </summary>
    void OnCleanDisconnect();
}
