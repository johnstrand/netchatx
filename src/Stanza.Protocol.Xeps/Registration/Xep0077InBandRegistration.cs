using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Registration;

/// <summary>
/// Implements XEP-0077: In-Band Registration and XEP-0158 CAPTCHA handling.
/// Provides both pre-authentication registration flows and authenticated password change / account removal.
/// </summary>
public sealed class Xep0077InBandRegistration : XepFeatureBase
{
    public const string NsRegister = "jabber:iq:register";
    public const string FeatureRegister = "http://jabber.org/features/iq-register";
    public const string NsData = "jabber:x:data";
    public const string NsCaptcha = "urn:xmpp:captcha";
    public const string NsMedia = "urn:xmpp:media-element";
    public const string NsTls = "urn:ietf:params:xml:ns:xmpp-tls";
    public const string NsStanzas = "urn:ietf:params:xml:ns:xmpp-stanzas";

    public override string Name => "XEP-0077: In-Band Registration";
    public override string FeatureUri => NsRegister;

    // --- Authenticated operations ---

    public async Task ChangePasswordAsync(string newPassword, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");
        if (string.IsNullOrEmpty(newPassword)) throw new ArgumentException("Password cannot be empty.", nameof(newPassword));

        var domain = Client.BoundJid.Domain;
        var username = Client.BoundJid.LocalPart ?? Client.BoundJid.Domain;

        var iq = new IqStanza(IqStanza.TypeSet, to: new Jid(null, domain));
        var query = new XmppElement("query", NsRegister)
            .Child(new XmppElement("username") { Value = username })
            .Child(new XmppElement("password") { Value = newPassword });
        iq.RawElement.Child(query);

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            var err = ExtractErrorMessage(response);
            throw new InvalidOperationException($"Password change failed: {err}");
        }
    }

    public async Task UnregisterAccountAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var domain = Client.BoundJid.Domain;
        var iq = new IqStanza(IqStanza.TypeSet, to: new Jid(null, domain));
        var query = new XmppElement("query", NsRegister)
            .Child(new XmppElement("remove"));
        iq.RawElement.Child(query);

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            var err = ExtractErrorMessage(response);
            throw new InvalidOperationException($"Account deletion failed: {err}");
        }
    }

    public async Task<RegistrationForm> QueryCurrentRegistrationAsync(CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        var domain = Client.BoundJid.Domain;
        var iq = new IqStanza(IqStanza.TypeGet, to: new Jid(null, domain));
        iq.RawElement.Child(new XmppElement("query", NsRegister));

        var response = await Client.SendIqAsync(iq, cancellationToken: ct).ConfigureAwait(false);
        if (response.IsError)
        {
            var err = ExtractErrorMessage(response);
            throw new InvalidOperationException($"Querying registration failed: {err}");
        }

        var query = response.RawElement.Element("query", NsRegister);
        return ParseRegistrationForm(query);
    }

    // --- Parsing and Building Helpers ---

    public static RegistrationForm ParseRegistrationForm(XmppElement? queryElem)
    {
        var form = new RegistrationForm();
        if (queryElem is null) return form;

        form.Instructions = queryElem.Element("instructions")?.Value;
        form.IsRegistered = queryElem.Element("registered") is not null;

        foreach (var child in queryElem.Children)
        {
            if (child.Name is not "instructions" and not "registered" and not "x" and not "key")
            {
                form.Fields.Add(child.Name);
            }
        }

        var dataFormElem = queryElem.Element("x", NsData);
        if (dataFormElem is not null)
        {
            form.DataForm = DataForm.FromElement(dataFormElem);

            // Check if form is a CAPTCHA challenge
            if (form.DataForm.FormType == NsCaptcha || queryElem.Element("captcha", NsCaptcha) is not null)
            {
                form.Captcha = ParseCaptchaFromDataForm(form.DataForm);
            }
        }

        var captchaElem = queryElem.Element("captcha", NsCaptcha);
        if (captchaElem is not null && form.Captcha is null)
        {
            form.Captcha = ParseCaptchaElement(captchaElem);
        }

        return form;
    }

    public static CaptchaChallenge? ParseCaptchaElement(XmppElement captchaElem)
    {
        var formElem = captchaElem.Element("x", NsData);
        if (formElem is not null)
        {
            var df = DataForm.FromElement(formElem);
            return ParseCaptchaFromDataForm(df);
        }

        return null;
    }

    public static CaptchaChallenge ParseCaptchaFromDataForm(DataForm dataForm)
    {
        var challenge = new CaptchaChallenge
        {
            Instructions = dataForm.Instructions,
            SourceForm = dataForm
        };

        foreach (var field in dataForm.Fields)
        {
            if (field.Type == "hidden")
            {
                challenge.HiddenFields.Add(field);
                if (field.Var.Equals("challenge", StringComparison.OrdinalIgnoreCase) ||
                    field.Var.Equals("sid", StringComparison.OrdinalIgnoreCase))
                {
                    challenge.ChallengeId = field.Value;
                }
            }
            else
            {
                // Visible input field (e.g. question/answer field)
                challenge.AnswerFieldVar = field.Var;
                challenge.QuestionText = field.Label ?? field.Value;
                if (field.MediaData is not null)
                {
                    challenge.ImageData = field.MediaData;
                    challenge.ImageMimeType = field.MediaMimeType;
                }
            }
        }

        return challenge;
    }

    public static IqStanza BuildRegistrationIq(string domain, RegistrationSubmission submission, string? iqId = null)
    {
        var iq = new IqStanza(IqStanza.TypeSet, id: iqId ?? Guid.NewGuid().ToString("N"), to: new Jid(null, domain));
        var query = new XmppElement("query", NsRegister);

        // Standard fields
        query.Child(new XmppElement("username") { Value = submission.Username });
        query.Child(new XmppElement("password") { Value = submission.Password });

        if (!string.IsNullOrEmpty(submission.Email))
        {
            query.Child(new XmppElement("email") { Value = submission.Email });
        }

        foreach (var (k, v) in submission.AdditionalFields)
        {
            query.Child(new XmppElement(k) { Value = v });
        }

        // CAPTCHA / Data Form submission
        if (submission.CaptchaChallenge is not null)
        {
            var submitForm = new XmppElement("x", NsData).Attr("type", "submit");
            foreach (var hidden in submission.CaptchaChallenge.HiddenFields)
            {
                var hf = new XmppElement("field").Attr("var", hidden.Var);
                foreach (var val in hidden.Values)
                {
                    hf.Child(new XmppElement("value") { Value = val });
                }
                submitForm.Child(hf);
            }

            if (!string.IsNullOrEmpty(submission.CaptchaAnswer))
            {
                var ansField = new XmppElement("field").Attr("var", submission.CaptchaChallenge.AnswerFieldVar);
                ansField.Child(new XmppElement("value") { Value = submission.CaptchaAnswer });
                submitForm.Child(ansField);
            }

            query.Child(submitForm);
        }
        else if (submission.DataForm is not null)
        {
            var submitForm = new XmppElement("x", NsData).Attr("type", "submit");
            if (!string.IsNullOrEmpty(submission.DataForm.FormType))
            {
                var formTypeField = new XmppElement("field").Attr("var", "FORM_TYPE");
                formTypeField.Child(new XmppElement("value") { Value = submission.DataForm.FormType });
                submitForm.Child(formTypeField);
            }

            var userField = new XmppElement("field").Attr("var", "username");
            userField.Child(new XmppElement("value") { Value = submission.Username });
            submitForm.Child(userField);

            var passField = new XmppElement("field").Attr("var", "password");
            passField.Child(new XmppElement("value") { Value = submission.Password });
            submitForm.Child(passField);

            if (!string.IsNullOrEmpty(submission.Email))
            {
                var emailField = new XmppElement("field").Attr("var", "email");
                emailField.Child(new XmppElement("value") { Value = submission.Email });
                submitForm.Child(emailField);
            }

            query.Child(submitForm);
        }

        iq.RawElement.Child(query);
        return iq;
    }

    public static RegistrationResult ParseRegistrationResult(IqStanza response)
    {
        if (response.IsResult)
        {
            return new RegistrationResult { IsSuccess = true };
        }

        var errorElem = response.RawElement.Element("error");
        var conditionElem = errorElem?.Children.FirstOrDefault(c => c.Namespace == NsStanzas);
        var condition = conditionElem?.Name ?? "unknown-error";
        var text = errorElem?.Element("text", NsStanzas)?.Value ?? errorElem?.Element("text")?.Value;

        // Check for CAPTCHA challenge in error (XEP-0158 section 3)
        var captchaElem = errorElem?.Element("captcha", NsCaptcha) ?? response.RawElement.Element("query")?.Element("captcha", NsCaptcha);
        CaptchaChallenge? captcha = null;
        if (captchaElem is not null)
        {
            captcha = ParseCaptchaElement(captchaElem);
        }
        else
        {
            var dataFormElem = errorElem?.Element("x", NsData) ?? response.RawElement.Element("query")?.Element("x", NsData);
            if (dataFormElem is not null)
            {
                var df = DataForm.FromElement(dataFormElem);
                if (df.FormType == NsCaptcha || df.Fields.Any(f => f.Var.Equals("answers", StringComparison.OrdinalIgnoreCase)))
                {
                    captcha = ParseCaptchaFromDataForm(df);
                }
            }
        }

        string userMessage = condition switch
        {
            "conflict" => "The desired username is already taken on this server.",
            "not-acceptable" when captcha is not null => text ?? "Security verification (CAPTCHA) required.",
            "not-acceptable" => text ?? "Required registration information was missing or not acceptable.",
            "not-allowed" => "In-band registration is not allowed on this server.",
            "forbidden" => "Registration was forbidden by server policy.",
            "resource-constraint" => "Server resource limit reached. Please try again later.",
            _ => text ?? $"Registration error: {condition}"
        };

        return new RegistrationResult
        {
            IsSuccess = false,
            ErrorMessage = userMessage,
            ErrorCondition = condition,
            CaptchaChallenge = captcha
        };
    }

    private static string ExtractErrorMessage(IqStanza response)
    {
        var error = response.RawElement.Element("error");
        if (error is null) return "Unknown error.";
        var text = error.Element("text", NsStanzas)?.Value ?? error.Element("text")?.Value;
        if (!string.IsNullOrEmpty(text)) return text;
        var cond = error.Children.FirstOrDefault(c => c.Namespace == NsStanzas)?.Name;
        return cond ?? error.GetAttr("code") ?? "Unknown error.";
    }

    // --- Standalone In-Band Registration Client ---

    /// <summary>
    /// Connects to the server, queries registration requirements, and returns the form.
    /// </summary>
    public static async Task<RegistrationForm> QueryRegistrationRequirementsAsync(
        string domain,
        string? host = null,
        int port = 5222,
        bool useDirectTls = false,
        bool allowUntrustedCertificates = false,
        IXmppTransport? transport = null,
        CancellationToken ct = default)
    {
        await using var client = new InBandRegistrationClient(transport, allowUntrustedCertificates);
        await client.ConnectAsync(domain, host, port, useDirectTls, ct).ConfigureAwait(false);
        return await client.GetRegistrationFormAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to the server, submits registration credentials, and returns the result.
    /// </summary>
    public static async Task<RegistrationResult> RegisterAccountAsync(
        string domain,
        RegistrationSubmission submission,
        string? host = null,
        int port = 5222,
        bool useDirectTls = false,
        bool allowUntrustedCertificates = false,
        IXmppTransport? transport = null,
        CancellationToken ct = default)
    {
        await using var client = new InBandRegistrationClient(transport, allowUntrustedCertificates);
        await client.ConnectAsync(domain, host, port, useDirectTls, ct).ConfigureAwait(false);
        return await client.RegisterAsync(submission, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Light-weight pre-authentication XMPP client specifically for XEP-0077 registration.
/// Handles TCP connection, TLS negotiation, stream header exchange, and registration IQs.
/// </summary>
public sealed class InBandRegistrationClient : IAsyncDisposable
{
    private readonly IXmppTransport _transport;
    private readonly bool _ownsTransport;
    private readonly XmppStreamParser _parser = new();
    private string _domain = string.Empty;

    public bool SupportsInBandRegistration { get; private set; }
    public XmppElement? StreamFeatures { get; private set; }

    public InBandRegistrationClient(IXmppTransport? transport = null, bool allowUntrustedCertificates = false)
    {
        if (transport is not null)
        {
            _transport = transport;
            _ownsTransport = false;
        }
        else
        {
            _transport = new TcpTlsTransport(allowUntrustedCertificates);
            _ownsTransport = true;
        }
    }

    public async Task ConnectAsync(
        string domain,
        string? host = null,
        int port = 5222,
        bool useDirectTls = false,
        CancellationToken ct = default)
    {
        _domain = domain;
        var targetHost = !string.IsNullOrWhiteSpace(host) ? host : domain;

        await _transport.ConnectAsync(targetHost, port, ct).ConfigureAwait(false);

        if (useDirectTls && !_transport.IsSecure)
        {
            await _transport.UpgradeToTlsAsync(targetHost, ct).ConfigureAwait(false);
        }

        // Open initial stream
        _parser.Reset();
        await SendStreamHeaderAsync(domain, ct).ConfigureAwait(false);
        _ = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false); // stream header
        var features = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false); // features
        StreamFeatures = features;

        // Upgrade with StartTLS if available and not direct TLS
        if (!_transport.IsSecure)
        {
            var startTls = features.Element("starttls", Xep0077InBandRegistration.NsTls);
            if (startTls is null)
                throw new InvalidOperationException("The server does not offer STARTTLS; refusing registration over an insecure connection.");

            var startTlsElem = new XmppElement("starttls", Xep0077InBandRegistration.NsTls);
            await SendElementRawAsync(startTlsElem, ct).ConfigureAwait(false);
            var proceed = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false);
            if (proceed.Name != "proceed")
                throw new InvalidOperationException($"StartTLS failed: {proceed.ToXmlString()}");

            await _transport.UpgradeToTlsAsync(targetHost, ct).ConfigureAwait(false);
            _parser.Reset();
            await SendStreamHeaderAsync(domain, ct).ConfigureAwait(false);
            _ = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false); // stream header
            features = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false); // features
            StreamFeatures = features;
        }

        if (!_transport.IsSecure)
            throw new InvalidOperationException("TLS is required before XMPP registration can continue.");

        if (StreamFeatures?.Element("register", Xep0077InBandRegistration.FeatureRegister) is not null)
        {
            SupportsInBandRegistration = true;
        }
    }

    public async Task<RegistrationForm> GetRegistrationFormAsync(CancellationToken ct = default)
    {
        var iq = new IqStanza(IqStanza.TypeGet, id: Guid.NewGuid().ToString("N"), to: new Jid(null, _domain));
        iq.RawElement.Child(new XmppElement("query", Xep0077InBandRegistration.NsRegister));

        await SendElementRawAsync(iq.RawElement, ct).ConfigureAwait(false);

        var responseElem = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false);
        var response = new IqStanza(responseElem);

        if (response.IsError)
        {
            var res = Xep0077InBandRegistration.ParseRegistrationResult(response);
            throw new InvalidOperationException(res.ErrorMessage ?? "Server rejected registration query.");
        }

        var query = response.RawElement.Element("query", Xep0077InBandRegistration.NsRegister);
        return Xep0077InBandRegistration.ParseRegistrationForm(query);
    }

    public async Task<RegistrationResult> RegisterAsync(RegistrationSubmission submission, CancellationToken ct = default)
    {
        var iq = Xep0077InBandRegistration.BuildRegistrationIq(_domain, submission);
        await SendElementRawAsync(iq.RawElement, ct).ConfigureAwait(false);

        var responseElem = await _parser.ReadElementAsync(_transport.Input, ct).ConfigureAwait(false);
        var response = new IqStanza(responseElem);
        return Xep0077InBandRegistration.ParseRegistrationResult(response);
    }

    private async Task SendStreamHeaderAsync(string domain, CancellationToken ct)
    {
        var header = $"<?xml version='1.0'?><stream:stream to='{domain}' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' version='1.0'>";
        var bytes = Encoding.UTF8.GetBytes(header);
        await _transport.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        await _transport.Output.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task SendElementRawAsync(XmppElement element, CancellationToken ct)
    {
        var xml = element.ToXmlString();
        var bytes = Encoding.UTF8.GetBytes(xml);
        await _transport.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        await _transport.Output.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        try
        {
            var closeTag = "</stream:stream>"u8.ToArray();
            await _transport.Output.WriteAsync(closeTag).ConfigureAwait(false);
            await _transport.Output.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore error sending stream close
        }

        await _transport.CloseAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        if (_ownsTransport)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
