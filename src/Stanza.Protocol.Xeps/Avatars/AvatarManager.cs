using System.Security.Cryptography;
using Stanza.Core;
using Stanza.Core.Client;
using Stanza.Protocol.Xeps.Common;

namespace Stanza.Protocol.Xeps.Avatars;

public sealed record AvatarDataResult(byte[] Data, string MimeType, string Hash);

public sealed class AvatarManager : IXepFeature
{
    public string Name => "Avatar Manager (XEP-0084 & XEP-0153)";
    public string FeatureUri => Xep0084UserAvatar.NsMetadata;

    public Xep0084UserAvatar UserAvatarXep { get; } = new();
    public Xep0153VCardAvatar VCardAvatarXep { get; } = new();

    private XmppClient? _client;

    public event Action<AvatarChangedEventArgs>? AvatarUpdated;

    public AvatarManager()
    {
        UserAvatarXep.AvatarMetadataReceived += (jid, meta) =>
        {
            AvatarUpdated?.Invoke(new AvatarChangedEventArgs
            {
                Jid = jid,
                Hash = meta.Id,
                MimeType = meta.MimeType,
                IsCleared = false,
                Source = "XEP-0084"
            });
        };

        UserAvatarXep.AvatarMetadataCleared += jid =>
        {
            AvatarUpdated?.Invoke(new AvatarChangedEventArgs
            {
                Jid = jid,
                IsCleared = true,
                Source = "XEP-0084"
            });
        };

        VCardAvatarXep.AvatarHashReceived += (jid, hash) =>
        {
            if (string.IsNullOrEmpty(hash))
            {
                AvatarUpdated?.Invoke(new AvatarChangedEventArgs
                {
                    Jid = jid,
                    IsCleared = true,
                    Source = "XEP-0153"
                });
            }
            else
            {
                AvatarUpdated?.Invoke(new AvatarChangedEventArgs
                {
                    Jid = jid,
                    Hash = hash,
                    IsCleared = false,
                    Source = "XEP-0153"
                });
            }
        };
    }

    public async ValueTask AttachAsync(XmppClient client, CancellationToken cancellationToken = default)
    {
        _client = client;
        await UserAvatarXep.AttachAsync(client, cancellationToken).ConfigureAwait(false);
        await VCardAvatarXep.AttachAsync(client, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DetachAsync(XmppClient client, CancellationToken cancellationToken = default)
    {
        await UserAvatarXep.DetachAsync(client, cancellationToken).ConfigureAwait(false);
        await VCardAvatarXep.DetachAsync(client, cancellationToken).ConfigureAwait(false);
        _client = null;
    }

    public void SetCurrentAvatarHash(string? hash)
    {
        VCardAvatarXep.CurrentAvatarHash = hash;
    }

    public static string ComputeSha1(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var hashBytes = SHA1.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task<AvatarDataResult?> SyncOwnAvatarAsync(CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("AvatarManager is not attached to an XmppClient.");

        // 1. Query PEP metadata for own avatar (XEP-0084)
        try
        {
            var meta = await UserAvatarXep.FetchAvatarMetadataAsync(null, ct).ConfigureAwait(false);
            if (meta is not null && !string.IsNullOrEmpty(meta.Id))
            {
                var pepData = await UserAvatarXep.FetchAvatarDataAsync(null, meta.Id, ct).ConfigureAwait(false);
                if (pepData is not null && pepData.Length > 0)
                {
                    var hash = ComputeSha1(pepData);
                    SetCurrentAvatarHash(hash);
                    return new AvatarDataResult(pepData, meta.MimeType, hash);
                }
            }
        }
        catch
        {
            // Fall back to vCard
        }

        // 2. Query own vCard (XEP-0153 / XEP-0054)
        try
        {
            var vCardResult = await VCardAvatarXep.FetchVCardAvatarAsync(null, ct).ConfigureAwait(false);
            if (vCardResult.HasValue && vCardResult.Value.Data.Length > 0)
            {
                var hash = ComputeSha1(vCardResult.Value.Data);
                SetCurrentAvatarHash(hash);
                return new AvatarDataResult(vCardResult.Value.Data, vCardResult.Value.MimeType, hash);
            }
        }
        catch
        {
            // Soft failure fetching vCard
        }

        return null;
    }

    public async Task<AvatarDataResult?> FetchAvatarAsync(Jid? contactJid = null, string? knownHash = null, CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("AvatarManager is not attached to an XmppClient.");

        // 1. Try PEP if hash is known
        if (!string.IsNullOrEmpty(knownHash))
        {
            try
            {
                var pepData = await UserAvatarXep.FetchAvatarDataAsync(contactJid, knownHash, ct).ConfigureAwait(false);
                if (pepData is not null && pepData.Length > 0)
                {
                    var hash = ComputeSha1(pepData);
                    return new AvatarDataResult(pepData, "image/png", hash);
                }
            }
            catch
            {
                // Fall back to vCard
            }
        }

        // 2. Fall back to vCard get
        try
        {
            var vCardResult = await VCardAvatarXep.FetchVCardAvatarAsync(contactJid, ct).ConfigureAwait(false);
            if (vCardResult.HasValue && vCardResult.Value.Data.Length > 0)
            {
                var hash = ComputeSha1(vCardResult.Value.Data);
                return new AvatarDataResult(vCardResult.Value.Data, vCardResult.Value.MimeType, hash);
            }
        }
        catch
        {
            // Soft failure fetching vCard
        }

        return null;
    }

    public async Task PublishAvatarAsync(byte[] data, string mimeType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var hash = ComputeSha1(data);

        // 1. Publish to PEP (XEP-0084)
        try
        {
            await UserAvatarXep.PublishAvatarDataAsync(hash, data, ct).ConfigureAwait(false);
            await UserAvatarXep.PublishAvatarMetadataAsync(hash, mimeType, data.Length, ct: ct).ConfigureAwait(false);
        }
        catch
        {
            // PEP might not be supported on all servers, continue with vCard
        }

        // 2. Publish to vCard (XEP-0153 / XEP-0054)
        try
        {
            await VCardAvatarXep.PublishVCardAvatarAsync(data, mimeType, ct).ConfigureAwait(false);
        }
        catch
        {
            // Soft failure on vCard
        }

        // 3. Set current hash so presence broadcasts <photo>{hash}</photo>
        SetCurrentAvatarHash(hash);
    }

    public async Task ClearAvatarAsync(CancellationToken ct = default)
    {
        // 1. Clear PEP metadata
        try
        {
            await UserAvatarXep.ClearAvatarAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Soft failure on PEP
        }

        // 2. Clear vCard
        try
        {
            await VCardAvatarXep.ClearVCardAvatarAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Soft failure on vCard
        }

        // 3. Broadcast empty photo in presence
        SetCurrentAvatarHash(string.Empty);
    }
}
