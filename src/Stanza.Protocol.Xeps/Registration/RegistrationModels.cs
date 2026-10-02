using System;
using System.Collections.Generic;
using System.Linq;
using Stanza.Core.Xml;

namespace Stanza.Protocol.Xeps.Registration;

/// <summary>
/// Represents a field in a XEP-0004 Data Form (jabber:x:data).
/// </summary>
public sealed class DataFormField
{
    public string Var { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Label { get; set; }
    public bool IsRequired { get; set; }
    public List<string> Values { get; set; } = [];

    public string? Value
    {
        get => Values.Count > 0 ? Values[0] : null;
        set
        {
            Values.Clear();
            if (value is not null)
            {
                Values.Add(value);
            }
        }
    }

    public string? MediaMimeType { get; set; }
    public byte[]? MediaData { get; set; }
    public string? MediaUri { get; set; }
}

/// <summary>
/// Represents a XEP-0004 Data Form (jabber:x:data).
/// </summary>
public sealed class DataForm
{
    public const string NsData = "jabber:x:data";

    public string Type { get; set; } = "form";
    public string? Title { get; set; }
    public string? Instructions { get; set; }
    public string? FormType { get; set; }
    public List<DataFormField> Fields { get; set; } = [];

    public DataFormField? GetField(string varName) =>
        Fields.FirstOrDefault(f => string.Equals(f.Var, varName, StringComparison.OrdinalIgnoreCase));

    public string? GetValue(string varName) =>
        GetField(varName)?.Value;

    public XmppElement ToElement()
    {
        var elem = new XmppElement("x", NsData).Attr("type", Type);
        if (!string.IsNullOrEmpty(Title))
        {
            elem.Child(new XmppElement("title") { Value = Title });
        }
        if (!string.IsNullOrEmpty(Instructions))
        {
            elem.Child(new XmppElement("instructions") { Value = Instructions });
        }
        if (!string.IsNullOrEmpty(FormType) && GetField("FORM_TYPE") is null)
        {
            var formTypeField = new XmppElement("field")
                .Attr("var", "FORM_TYPE")
                .Attr("type", "hidden");
            formTypeField.Child(new XmppElement("value") { Value = FormType });
            elem.Child(formTypeField);
        }

        foreach (var field in Fields)
        {
            var fieldElem = new XmppElement("field").Attr("var", field.Var);
            if (!string.IsNullOrEmpty(field.Type))
            {
                fieldElem.Attr("type", field.Type);
            }
            if (!string.IsNullOrEmpty(field.Label))
            {
                fieldElem.Attr("label", field.Label);
            }
            if (field.IsRequired)
            {
                fieldElem.Child(new XmppElement("required"));
            }
            foreach (var val in field.Values)
            {
                fieldElem.Child(new XmppElement("value") { Value = val });
            }
            elem.Child(fieldElem);
        }

        return elem;
    }

    public static DataForm FromElement(XmppElement element)
    {
        var form = new DataForm
        {
            Type = element.GetAttr("type") ?? "form",
            Title = element.Element("title")?.Value,
            Instructions = element.Element("instructions")?.Value
        };

        foreach (var fElem in element.Elements("field"))
        {
            var field = new DataFormField
            {
                Var = fElem.GetAttr("var") ?? string.Empty,
                Type = fElem.GetAttr("type"),
                Label = fElem.GetAttr("label"),
                IsRequired = fElem.Element("required") is not null,
                Values = fElem.Elements("value").Select(v => v.Value ?? string.Empty).ToList()
            };

            if (field.Var.Equals("FORM_TYPE", StringComparison.OrdinalIgnoreCase))
            {
                form.FormType = field.Value;
            }

            // Check for media element (XEP-0231 / XEP-0158)
            var mediaElem = fElem.Element("media", "urn:xmpp:media-element");
            if (mediaElem is not null)
            {
                var uriElem = mediaElem.Element("uri");
                if (uriElem is not null)
                {
                    field.MediaMimeType = uriElem.GetAttr("type");
                    var uriVal = uriElem.Value;
                    field.MediaUri = uriVal;

                    if (!string.IsNullOrEmpty(uriVal) && uriVal.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var commaIdx = uriVal.IndexOf(',');
                        if (commaIdx >= 0)
                        {
                            try
                            {
                                var base64 = uriVal[(commaIdx + 1)..];
                                field.MediaData = Convert.FromBase64String(base64);
                            }
                            catch
                            {
                                // Ignore malformed data URI
                            }
                        }
                    }
                }
            }

            form.Fields.Add(field);
        }

        return form;
    }
}

/// <summary>
/// Represents a CAPTCHA challenge received from the server (XEP-0158 / XEP-0077).
/// </summary>
public sealed class CaptchaChallenge
{
    public string? Instructions { get; set; }
    public string? ChallengeId { get; set; }
    public string? QuestionText { get; set; }
    public string AnswerFieldVar { get; set; } = "answers";
    public List<DataFormField> HiddenFields { get; set; } = [];
    public byte[]? ImageData { get; set; }
    public string? ImageMimeType { get; set; }
    public DataForm? SourceForm { get; set; }
}

/// <summary>
/// Represents the registration requirements and fields offered by an XMPP server (XEP-0077).
/// </summary>
public sealed class RegistrationForm
{
    public string? Instructions { get; set; }
    public List<string> Fields { get; set; } = [];
    public bool IsRegistered { get; set; }
    public DataForm? DataForm { get; set; }
    public CaptchaChallenge? Captcha { get; set; }

    public bool RequiresUsername =>
        Fields.Contains("username", StringComparer.OrdinalIgnoreCase) ||
        (DataForm?.Fields.Any(f => f.Var.Equals("username", StringComparison.OrdinalIgnoreCase)) ?? false);

    public bool RequiresPassword =>
        Fields.Contains("password", StringComparer.OrdinalIgnoreCase) ||
        (DataForm?.Fields.Any(f => f.Var.Equals("password", StringComparison.OrdinalIgnoreCase)) ?? false);

    public bool RequiresEmail =>
        Fields.Contains("email", StringComparer.OrdinalIgnoreCase) ||
        (DataForm?.Fields.Any(f => f.Var.Equals("email", StringComparison.OrdinalIgnoreCase)) ?? false);
}

/// <summary>
/// Information submitted by a client to register an account (XEP-0077).
/// </summary>
public sealed class RegistrationSubmission
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public string? Email { get; set; }
    public string? CaptchaAnswer { get; set; }
    public CaptchaChallenge? CaptchaChallenge { get; set; }
    public DataForm? DataForm { get; set; }
    public Dictionary<string, string> AdditionalFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of an in-band registration attempt.
/// </summary>
public sealed class RegistrationResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCondition { get; set; }
    public CaptchaChallenge? CaptchaChallenge { get; set; }
    public bool RequiresCaptcha => CaptchaChallenge is not null;
    public RegistrationForm? Form { get; set; }
}
