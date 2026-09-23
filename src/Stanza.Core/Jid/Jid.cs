using System.Diagnostics.CodeAnalysis;

namespace Stanza.Core;

/// <summary>
/// Represents an RFC 7622 compliant Extensible Messaging and Presence Protocol (XMPP) Jabber Identifier (JID).
/// A JID is of the form [localpart@]domainpart[/resourcepart].
/// </summary>
public sealed class Jid : IEquatable<Jid>, IComparable<Jid>
{
    public string? LocalPart { get; }
    public string Domain { get; }
    public string? Resource { get; }

    public bool IsBare => Resource is null;
    public bool IsFull => Resource is not null;
    public bool IsDomainOnly => LocalPart is null && Resource is null;

    private readonly string _fullString;
    private readonly string _bareString;

    public Jid(string? localPart, string domain, string? resource = null)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be null or whitespace.", nameof(domain));

        LocalPart = string.IsNullOrWhiteSpace(localPart) ? null : localPart.Trim();
        Domain = domain.Trim().ToLowerInvariant();
        Resource = string.IsNullOrWhiteSpace(resource) ? null : resource.Trim();

        _bareString = LocalPart is not null ? $"{LocalPart}@{Domain}" : Domain;
        _fullString = Resource is not null ? $"{_bareString}/{Resource}" : _bareString;
    }

    /// <summary>
    /// Returns the bare JID (localpart@domainpart or domainpart) without resource.
    /// </summary>
    public Jid BareJid => IsBare ? this : new Jid(LocalPart, Domain, null);

    /// <summary>
    /// Returns a new JID with the specified resource part.
    /// </summary>
    public Jid WithResource(string? resource) => new Jid(LocalPart, Domain, resource);

    public static Jid Parse(string jidString)
    {
        if (string.IsNullOrWhiteSpace(jidString))
            throw new ArgumentException("JID string cannot be null or whitespace.", nameof(jidString));

        if (!TryParse(jidString, out var result))
            throw new FormatException($"Invalid XMPP JID format: '{jidString}'.");

        return result;
    }

    public static bool TryParse(string? jidString, [NotNullWhen(true)] out Jid? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(jidString))
            return false;

        var span = jidString.AsSpan().Trim();
        if (span.IsEmpty)
            return false;

        string? local = null;
        string domain;
        string? resource = null;

        var atIndex = span.IndexOf('@');
        var slashIndex = span.IndexOf('/');

        // If '@' is after '/', it is part of the resource, not a localpart separator
        if (atIndex >= 0 && slashIndex >= 0 && atIndex > slashIndex)
        {
            atIndex = -1;
        }

        var remaining = span;

        if (atIndex >= 0)
        {
            var localSpan = remaining[..atIndex];
            if (localSpan.IsEmpty || localSpan.Length > 1023)
                return false;

            local = localSpan.ToString();
            remaining = remaining[(atIndex + 1)..];
        }

        slashIndex = remaining.IndexOf('/');
        if (slashIndex >= 0)
        {
            var domainSpan = remaining[..slashIndex];
            if (domainSpan.IsEmpty || domainSpan.Length > 1023 || domainSpan.IndexOf('@') >= 0)
                return false;

            domain = domainSpan.ToString();
            var resourceSpan = remaining[(slashIndex + 1)..];
            if (resourceSpan.IsEmpty || resourceSpan.Length > 1023)
                return false;

            resource = resourceSpan.ToString();
        }
        else
        {
            if (remaining.IsEmpty || remaining.Length > 1023 || remaining.IndexOf('@') >= 0)
                return false;

            domain = remaining.ToString();
        }

        try
        {
            result = new Jid(local, domain, resource);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Equals(Jid? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Domain, other.Domain, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(LocalPart, other.LocalPart, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Resource, other.Resource, StringComparison.Ordinal);
    }

    public bool EqualsBare(Jid? other)
    {
        if (other is null) return false;
        return string.Equals(Domain, other.Domain, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(LocalPart, other.LocalPart, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is Jid other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Domain),
            LocalPart is not null ? StringComparer.OrdinalIgnoreCase.GetHashCode(LocalPart) : 0,
            Resource is not null ? StringComparer.Ordinal.GetHashCode(Resource) : 0);

    public int CompareTo(Jid? other)
    {
        if (other is null) return 1;
        return string.Compare(_fullString, other._fullString, StringComparison.Ordinal);
    }

    public override string ToString() => _fullString;

    public string ToBareString() => _bareString;

    public static implicit operator string(Jid jid) => jid.ToString();
    public static explicit operator Jid(string jidString) => Parse(jidString);

    public static bool operator ==(Jid? left, Jid? right) => Equals(left, right);
    public static bool operator !=(Jid? left, Jid? right) => !Equals(left, right);
}
