using System;
using System.Linq;
using System.Threading.Tasks;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Transport;
using Stanza.Core.Xml;
using Stanza.MockServer;
using Stanza.Protocol.Xeps.Registration;
using Xunit;

namespace Stanza.Xeps.Tests;

public sealed class Xep0077RegistrationTests
{
    [Fact]
    public void ParseRegistrationForm_StandardFields_ParsedCorrectly()
    {
        var xml = """
            <query xmlns='jabber:iq:register'>
              <instructions>Please choose a username and password to register.</instructions>
              <username/>
              <password/>
              <email/>
            </query>
            """;
        var queryElem = XmppElement.Parse(xml);
        var form = Xep0077InBandRegistration.ParseRegistrationForm(queryElem);

        Assert.Equal("Please choose a username and password to register.", form.Instructions);
        Assert.True(form.RequiresUsername);
        Assert.True(form.RequiresPassword);
        Assert.True(form.RequiresEmail);
        Assert.False(form.IsRegistered);
        Assert.Null(form.Captcha);
    }

    [Fact]
    public void ParseRegistrationForm_WithRegisteredTag_SetsIsRegistered()
    {
        var xml = """
            <query xmlns='jabber:iq:register'>
              <registered/>
              <username>alice</username>
              <password>secret</password>
            </query>
            """;
        var queryElem = XmppElement.Parse(xml);
        var form = Xep0077InBandRegistration.ParseRegistrationForm(queryElem);

        Assert.True(form.IsRegistered);
        Assert.True(form.RequiresUsername);
        Assert.True(form.RequiresPassword);
    }

    [Fact]
    public void ParseRegistrationForm_WithDataForm_ParsesDataForm()
    {
        var xml = """
            <query xmlns='jabber:iq:register'>
              <instructions>Fill in the form to register.</instructions>
              <x xmlns='jabber:x:data' type='form'>
                <title>Account Registration</title>
                <instructions>Please enter your details</instructions>
                <field type='hidden' var='FORM_TYPE'><value>jabber:iq:register</value></field>
                <field type='text-single' label='Username' var='username'><required/></field>
                <field type='text-private' label='Password' var='password'><required/></field>
                <field type='text-single' label='Email Address' var='email'/>
              </x>
            </query>
            """;
        var queryElem = XmppElement.Parse(xml);
        var form = Xep0077InBandRegistration.ParseRegistrationForm(queryElem);

        Assert.NotNull(form.DataForm);
        Assert.Equal("Account Registration", form.DataForm.Title);
        Assert.Equal("jabber:iq:register", form.DataForm.FormType);
        Assert.True(form.RequiresUsername);
        Assert.True(form.RequiresPassword);
        Assert.True(form.RequiresEmail);
    }

    [Fact]
    public void ParseRegistrationForm_WithCaptcha_ParsesCaptchaChallenge()
    {
        var xml = """
            <query xmlns='jabber:iq:register'>
              <instructions>Solve CAPTCHA</instructions>
              <captcha xmlns='urn:xmpp:captcha'>
                <x xmlns='jabber:x:data' type='form'>
                  <field type='hidden' var='FORM_TYPE'><value>urn:xmpp:captcha</value></field>
                  <field type='hidden' var='challenge'><value>chal-xyz</value></field>
                  <field type='text-single' label='What is 10 + 2?' var='answers'><required/></field>
                </x>
              </captcha>
            </query>
            """;
        var queryElem = XmppElement.Parse(xml);
        var form = Xep0077InBandRegistration.ParseRegistrationForm(queryElem);

        Assert.NotNull(form.Captcha);
        Assert.Equal("chal-xyz", form.Captcha.ChallengeId);
        Assert.Equal("What is 10 + 2?", form.Captcha.QuestionText);
        Assert.Equal("answers", form.Captcha.AnswerFieldVar);
    }

    [Fact]
    public void BuildRegistrationIq_StandardSubmission_GeneratesValidIq()
    {
        var submission = new RegistrationSubmission
        {
            Username = "bob",
            Password = "mypassword",
            Email = "bob@example.com"
        };

        var iq = Xep0077InBandRegistration.BuildRegistrationIq("example.com", submission, iqId: "reg-1");

        Assert.Equal("set", iq.Type);
        Assert.Equal("example.com", iq.To?.Domain);
        Assert.Equal("reg-1", iq.Id);

        var query = iq.RawElement.Element("query", "jabber:iq:register");
        Assert.NotNull(query);
        Assert.Equal("bob", query.Element("username")?.Value);
        Assert.Equal("mypassword", query.Element("password")?.Value);
        Assert.Equal("bob@example.com", query.Element("email")?.Value);
    }

    [Fact]
    public void BuildRegistrationIq_WithCaptchaAnswer_IncludesSubmitDataForm()
    {
        var challenge = new CaptchaChallenge
        {
            ChallengeId = "c-99",
            AnswerFieldVar = "answers",
            HiddenFields =
            [
                new DataFormField { Var = "FORM_TYPE", Type = "hidden", Value = "urn:xmpp:captcha" },
                new DataFormField { Var = "challenge", Type = "hidden", Value = "c-99" }
            ]
        };

        var submission = new RegistrationSubmission
        {
            Username = "carol",
            Password = "password123",
            CaptchaAnswer = "12",
            CaptchaChallenge = challenge
        };

        var iq = Xep0077InBandRegistration.BuildRegistrationIq("example.com", submission, iqId: "reg-2");
        var query = iq.RawElement.Element("query", "jabber:iq:register");
        Assert.NotNull(query);

        var dataForm = query.Element("x", "jabber:x:data");
        Assert.NotNull(dataForm);
        Assert.Equal("submit", dataForm.GetAttr("type"));

        var answerField = dataForm.Elements("field").FirstOrDefault(f => f.GetAttr("var") == "answers");
        Assert.NotNull(answerField);
        Assert.Equal("12", answerField.Element("value")?.Value);
    }

    [Fact]
    public void ParseRegistrationResult_Success_ReturnsSuccessResult()
    {
        var iq = new IqStanza(IqStanza.TypeResult, id: "r1");
        var result = Xep0077InBandRegistration.ParseRegistrationResult(iq);

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.False(result.RequiresCaptcha);
    }

    [Fact]
    public void ParseRegistrationResult_ConflictError_ReturnsConflictError()
    {
        var iq = new IqStanza(IqStanza.TypeError, id: "r2");
        var error = new XmppElement("error").Attr("type", "cancel");
        error.Child(new XmppElement("conflict", "urn:ietf:params:xml:ns:xmpp-stanzas"));
        error.Child(new XmppElement("text", "urn:ietf:params:xml:ns:xmpp-stanzas") { Value = "Username already exists" });
        iq.RawElement.Child(error);

        var result = Xep0077InBandRegistration.ParseRegistrationResult(iq);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.ErrorCondition);
        Assert.Contains("already taken", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRegistrationResult_CaptchaChallengeError_ReturnsCaptchaChallenge()
    {
        var iq = new IqStanza(IqStanza.TypeError, id: "r3");
        var error = new XmppElement("error").Attr("type", "modify").Attr("code", "406");
        error.Child(new XmppElement("not-acceptable", "urn:ietf:params:xml:ns:xmpp-stanzas"));

        var captchaElem = new XmppElement("captcha", "urn:xmpp:captcha");
        var dataFormElem = new XmppElement("x", "jabber:x:data").Attr("type", "form");
        dataFormElem.Child(new XmppElement("field").Attr("var", "FORM_TYPE").Attr("type", "hidden").Child(new XmppElement("value") { Value = "urn:xmpp:captcha" }));
        dataFormElem.Child(new XmppElement("field").Attr("var", "challenge").Attr("type", "hidden").Child(new XmppElement("value") { Value = "chal-123" }));
        dataFormElem.Child(new XmppElement("field").Attr("var", "answers").Attr("type", "text-single").Attr("label", "What is 3 + 4?"));
        captchaElem.Child(dataFormElem);
        error.Child(captchaElem);

        iq.RawElement.Child(error);

        var result = Xep0077InBandRegistration.ParseRegistrationResult(iq);

        Assert.False(result.IsSuccess);
        Assert.True(result.RequiresCaptcha);
        Assert.NotNull(result.CaptchaChallenge);
        Assert.Equal("chal-123", result.CaptchaChallenge.ChallengeId);
        Assert.Equal("What is 3 + 4?", result.CaptchaChallenge.QuestionText);
    }

    [Fact]
    public async Task PreAuth_InBandRegistrationClient_QueryAndRegister_MockServer()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport) { Domain = "mock.test" };
        server.Start();

        await using var client = new InBandRegistrationClient(transport);
        await client.ConnectAsync("mock.test");

        Assert.True(client.SupportsInBandRegistration);

        // 1. Query registration requirements
        var form = await client.GetRegistrationFormAsync();
        Assert.NotNull(form);
        Assert.True(form.RequiresUsername);
        Assert.True(form.RequiresPassword);
        Assert.True(form.RequiresEmail);

        // 2. Submit valid registration
        var submission = new RegistrationSubmission
        {
            Username = "newbie",
            Password = "password123",
            Email = "newbie@mock.test"
        };
        var result = await client.RegisterAsync(submission);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PreAuth_InBandRegistrationClient_RejectsInsecureServer()
    {
        var transport = new LoopbackTransport(isSecureOnConnect: false);
        await using var server = new MockXmppServer(transport) { Domain = "mock.test" };
        server.Start();

        await using var client = new InBandRegistrationClient(transport);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync("mock.test"));

        Assert.Contains("does not offer STARTTLS", exception.Message);
        Assert.False(client.SupportsInBandRegistration);
    }

    [Fact]
    public async Task PreAuth_RegisterAccount_ConflictError_MockServer()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport) { Domain = "mock.test" };
        server.Start();

        await using var client = new InBandRegistrationClient(transport);
        await client.ConnectAsync("mock.test");

        var submission = new RegistrationSubmission
        {
            Username = "conflict_user",
            Password = "password123"
        };
        var result = await client.RegisterAsync(submission);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.ErrorCondition);
        Assert.Contains("already taken", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreAuth_RegisterAccount_CaptchaFlow_MockServer()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport) { Domain = "mock.test" };
        server.Start();

        await using var client = new InBandRegistrationClient(transport);
        await client.ConnectAsync("mock.test");

        // Attempt 1: Without CAPTCHA answer
        var submission = new RegistrationSubmission
        {
            Username = "captcha_user",
            Password = "password123"
        };
        var result1 = await client.RegisterAsync(submission);

        Assert.False(result1.IsSuccess);
        Assert.True(result1.RequiresCaptcha);
        Assert.NotNull(result1.CaptchaChallenge);
        Assert.Equal("What is 5 + 3?", result1.CaptchaChallenge.QuestionText);

        // Attempt 2: With correct CAPTCHA answer
        submission.CaptchaChallenge = result1.CaptchaChallenge;
        submission.CaptchaAnswer = "8";

        var result2 = await client.RegisterAsync(submission);
        Assert.True(result2.IsSuccess);
    }

    [Fact]
    public async Task Authenticated_ChangePassword_MockServer()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport) { Domain = "mock.test" };
        server.Start();

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.test"),
            Password = "password123"
        };

        await using var client = new XmppClient(options, transport);
        var registrationXep = new Xep0077InBandRegistration();
        await registrationXep.AttachAsync(client);

        await client.ConnectAsync();

        // Change password should succeed
        await registrationXep.ChangePasswordAsync("brand_new_secret");
    }

    [Fact]
    public async Task Authenticated_UnregisterAccount_MockServer()
    {
        var transport = new LoopbackTransport();
        await using var server = new MockXmppServer(transport) { Domain = "mock.test" };
        server.Start();

        var options = new XmppClientOptions
        {
            Jid = Jid.Parse("alice@mock.test"),
            Password = "password123"
        };

        await using var client = new XmppClient(options, transport);
        var registrationXep = new Xep0077InBandRegistration();
        await registrationXep.AttachAsync(client);

        await client.ConnectAsync();

        // Unregister should succeed
        await registrationXep.UnregisterAccountAsync();
    }
}
