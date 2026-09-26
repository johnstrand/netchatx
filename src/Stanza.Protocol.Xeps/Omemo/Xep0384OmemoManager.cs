using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Core.Stanzas;
using Stanza.Core.Xml;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Omemo;

public sealed class DecryptedOmemoMessage
{
    public required MessageStanza OriginalStanza { get; init; }
    public required string PlaintextBody { get; init; }
    public required int SenderDeviceId { get; init; }
    public required Jid SenderJid { get; init; }
}

public sealed class Xep0384OmemoManager : XepFeatureBase
{
    public const string NsOmemo2 = "urn:xmpp:omemo:2";
    public const string NsSce0 = "urn:xmpp:sce:0";

    public override string Name => "XEP-0384: OMEMO Encryption";
    public override string FeatureUri => NsOmemo2;

    public int LocalDeviceId { get; }
    public KeyPairData IdentityKey { get; }

    private readonly ConcurrentDictionary<string, DoubleRatchetSession> _sessions = new();

    public event Action<DecryptedOmemoMessage>? MessageDecrypted;

    public Xep0384OmemoManager(int? deviceId = null, KeyPairData? identityKey = null)
    {
        LocalDeviceId = deviceId ?? RandomNumberGenerator.GetInt32(100000, 999999);
        IdentityKey = identityKey ?? OmemoCrypto.GenerateX25519KeyPair();
    }

    public string GetFingerprint(byte[]? publicKey = null)
    {
        var key = publicKey ?? IdentityKey.PublicKey;
        var hash = SHA256.HashData(key);
        return BitConverter.ToString(hash).Replace("-", " ");
    }

    public DoubleRatchetSession GetOrCreateSession(Jid remoteJid, int remoteDeviceId, byte[]? remotePublicKey = null, bool isInitiator = true)
    {
        var key = $"{remoteJid.BareJid}:{remoteDeviceId}";
        return _sessions.GetOrAdd(key, _ =>
        {
            var rootKey = new byte[32];
            var localDHPair = isInitiator ? OmemoCrypto.GenerateX25519KeyPair() : IdentityKey;
            return new DoubleRatchetSession(rootKey, localDHPair, remotePublicKey, isInitiator);
        });
    }

    public async Task<MessageStanza> EncryptMessageAsync(Jid to, string plaintext, int remoteDeviceId, byte[]? remotePublicKey = null, CancellationToken ct = default)
    {
        if (Client is null) throw new InvalidOperationException("Client not attached.");

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var session = GetOrCreateSession(to, remoteDeviceId, remotePublicKey, isInitiator: true);

            // 1. Envelope plaintext using SCE (XEP-0420)
            var envelope = new XmppElement("envelope", NsSce0)
                .Child(new XmppElement("content")
                    .Child(new XmppElement("body", "jabber:client") { Value = plaintext }));

            var envelopeBytes = Encoding.UTF8.GetBytes(envelope.ToXmlString());

            // 2. Generate random message encryption key + IV
            var payloadKey = RandomNumberGenerator.GetBytes(16);
            var payloadIv = RandomNumberGenerator.GetBytes(12);

            // 3. Encrypt envelope with AES-GCM
            var encryptedPayload = OmemoCrypto.EncryptAesGcm(payloadKey, payloadIv, envelopeBytes);

            // 4. Encrypt payloadKey with Double Ratchet session
            var (ratchetKey, ratchetIv, dhPub, msgNum) = session.RatchetEncrypt();
            var encryptedPayloadKey = OmemoCrypto.EncryptAesGcm(ratchetKey, ratchetIv, payloadKey);

            // 5. Construct OMEMO XML stanza
            var encryptedElem = new XmppElement("encrypted", NsOmemo2);
            var headerElem = new XmppElement("header").Attr("sid", LocalDeviceId.ToString());

            var keysElem = new XmppElement("keys").Attr("jid", to.BareJid.ToString());
            var keyElem = new XmppElement("key")
                .Attr("rid", remoteDeviceId.ToString())
                .Attr("kex", "false");
            keyElem.Value = Convert.ToBase64String(encryptedPayloadKey);
            keysElem.Child(keyElem);

            headerElem.Child(keysElem);
            headerElem.Child(new XmppElement("iv") { Value = Convert.ToBase64String(payloadIv) });
            headerElem.Child(new XmppElement("dh") { Value = Convert.ToBase64String(dhPub) });

            encryptedElem.Child(headerElem);
            encryptedElem.Child(new XmppElement("payload") { Value = Convert.ToBase64String(encryptedPayload) });

            var msg = new MessageStanza(to: to, type: MessageStanza.TypeChat);
            msg.Body = "I sent you an OMEMO encrypted message."; // Fallback body
            msg.RawElement.Child(encryptedElem);

            return msg;
        }, ct).ConfigureAwait(false);
    }

    public override ValueTask<bool> OnIncomingElementAsync(XmppClient client, XmppElement element, CancellationToken cancellationToken = default)
    {
        if (element.Name == "message")
        {
            var encElem = element.Element("encrypted", NsOmemo2);
            if (encElem is null)
            {
                var receivedCarbon = element.Element("received", NsOmemo2) ?? element.Element("received", "urn:xmpp:carbons:2");
                var sentCarbon = element.Element("sent", NsOmemo2) ?? element.Element("sent", "urn:xmpp:carbons:2");
                var carbonWrapper = receivedCarbon ?? sentCarbon;
                if (carbonWrapper is not null)
                {
                    var inner = carbonWrapper.Element("forwarded", "urn:xmpp:forward:0")?.Element("message");
                    if (inner is not null)
                    {
                        element = inner;
                        encElem = element.Element("encrypted", NsOmemo2);
                    }
                }
            }

            if (encElem is not null)
            {
                try
                {
                    var header = encElem.Element("header");
                    var sidStr = header?.GetAttr("sid");
                    var payloadB64 = encElem.Element("payload")?.Value;

                    if (int.TryParse(sidStr, out int senderDeviceId) && !string.IsNullOrEmpty(payloadB64))
                    {
                        var fromStr = element.GetAttr("from");
                        if (!string.IsNullOrEmpty(fromStr) && Jid.TryParse(fromStr, out var fromJid))
                        {
                            // Find our key in header
                            foreach (var keys in header!.Elements("keys"))
                            {
                                foreach (var k in keys.Elements("key"))
                                {
                                    if (k.GetAttr("rid") == LocalDeviceId.ToString())
                                    {
                                        var encPayloadKey = Convert.FromBase64String(k.Value ?? "");
                                        var payloadIv = Convert.FromBase64String(header.Element("iv")?.Value ?? "");
                                        var dhPub = Convert.FromBase64String(header.Element("dh")?.Value ?? "");

                                        var session = GetOrCreateSession(fromJid.BareJid, senderDeviceId, dhPub, isInitiator: false);
                                        var (rKey, rIv) = session.RatchetDecrypt(dhPub, 0);

                                        var payloadKey = OmemoCrypto.DecryptAesGcm(rKey, rIv, encPayloadKey);
                                        var encPayload = Convert.FromBase64String(payloadB64);
                                        var envelopeBytes = OmemoCrypto.DecryptAesGcm(payloadKey, payloadIv, encPayload);

                                        var envelopeXml = Encoding.UTF8.GetString(envelopeBytes);
                                        var envelope = XmppElement.Parse(envelopeXml);
                                        var body = envelope.Element("content")?.Element("body")?.Value;

                                        if (!string.IsNullOrEmpty(body))
                                        {
                                            MessageDecrypted?.Invoke(new DecryptedOmemoMessage
                                            {
                                                OriginalStanza = new MessageStanza(element),
                                                PlaintextBody = body,
                                                SenderDeviceId = senderDeviceId,
                                                SenderJid = fromJid
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OMEMO decryption error: {ex}");
                }
            }
        }

        return ValueTask.FromResult(true);
    }
}
