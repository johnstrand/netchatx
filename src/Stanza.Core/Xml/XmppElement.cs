using System.IO;
using System.Text;
using System.Xml;

namespace Stanza.Core.Xml;

/// <summary>
/// A lightweight, mutable, trimmer-safe XML element representation tailored for XMPP stanzas and payloads.
/// </summary>
public sealed class XmppElement
{
    public string Name { get; set; }
    public string? Prefix { get; set; }
    public string? Namespace { get; set; }
    public string? Value { get; set; }
    public string FullName => string.IsNullOrEmpty(Prefix) ? Name : $"{Prefix}:{Name}";

    private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal);
    private readonly List<XmppElement> _children = [];

    public IReadOnlyDictionary<string, string> Attributes => _attributes;
    public IReadOnlyList<XmppElement> Children => _children;

    public XmppElement(string name, string? ns = null, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Element name cannot be null or whitespace.", nameof(name));

        Name = name;
        Namespace = ns;
        Prefix = prefix;
    }

    public XmppElement Attr(string name, string? value)
    {
        if (value is null)
        {
            _attributes.Remove(name);
        }
        else
        {
            _attributes[name] = value;
        }
        return this;
    }

    public string? GetAttr(string name) => _attributes.GetValueOrDefault(name);

    public bool HasAttr(string name) => _attributes.ContainsKey(name);

    public XmppElement Child(XmppElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
        return this;
    }

    public XmppElement Child(string name, string? ns = null, string? text = null)
    {
        var child = new XmppElement(name, ns);
        if (text is not null)
            child.Value = text;
        _children.Add(child);
        return this;
    }

    public XmppElement Text(string? text)
    {
        Value = text;
        return this;
    }

    public XmppElement? Element(string name, string? ns = null)
    {
        foreach (var child in _children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal) &&
                (ns is null || string.Equals(child.Namespace ?? child.GetAttr("xmlns"), ns, StringComparison.Ordinal)))
            {
                return child;
            }
        }
        return null;
    }

    public IEnumerable<XmppElement> Elements(string? name = null, string? ns = null)
    {
        foreach (var child in _children)
        {
            var nameMatch = name is null || string.Equals(child.Name, name, StringComparison.Ordinal);
            var nsMatch = ns is null || string.Equals(child.Namespace ?? child.GetAttr("xmlns"), ns, StringComparison.Ordinal);
            if (nameMatch && nsMatch)
            {
                yield return child;
            }
        }
    }

    public string? ElementValue(string name, string? ns = null) => Element(name, ns)?.Value;

    public void RemoveChild(XmppElement child) => _children.Remove(child);

    public void ClearChildren() => _children.Clear();

    public string ToXmlString(bool indent = false)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = indent,
            ConformanceLevel = ConformanceLevel.Fragment
        };

        using (var stringWriter = new StringWriter(sb))
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            WriteTo(writer);
        }

        return sb.ToString();
    }

    public void WriteTo(XmlWriter writer)
    {
        WriteStartElement(writer);
        WriteAttributes(writer);

        if (!string.IsNullOrEmpty(Value))
        {
            writer.WriteString(Value);
        }

        WriteChildren(writer);

        writer.WriteEndElement();
    }

    private void WriteStartElement(XmlWriter writer)
    {
        var prefix = Prefix;
        var name = Name;
        var ns = Namespace ?? GetAttr("xmlns");

        if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(ns))
        {
            writer.WriteStartElement(prefix, name, ns);
        }
        else if (!string.IsNullOrEmpty(ns))
        {
            writer.WriteStartElement(name, ns);
        }
        else
        {
            writer.WriteStartElement(name);
        }
    }

    private void WriteAttributes(XmlWriter writer)
    {
        foreach (var (key, value) in _attributes)
        {
            if (key == "xmlns")
            {
                continue;
            }

            WriteAttribute(writer, key, value);
        }
    }

    private void WriteAttribute(XmlWriter writer, string key, string value)
    {
        if (key.StartsWith("xmlns:", StringComparison.Ordinal))
        {
            var pref = key.Substring(6);
            writer.WriteAttributeString("xmlns", pref, null, value);
            return;
        }

        var colonIdx = key.IndexOf(':');
        if (colonIdx > 0)
        {
            var pref = key.Substring(0, colonIdx);
            var local = key.Substring(colonIdx + 1);
            var attrNs = pref switch
            {
                "xml" => "http://www.w3.org/XML/1998/namespace",
                "stream" => "http://etherx.jabber.org/streams",
                _ => _attributes.TryGetValue($"xmlns:{pref}", out var dNs) ? dNs : null
            };

            try
            {
                writer.WriteAttributeString(pref, local, attrNs, value);
            }
            catch
            {
                writer.WriteAttributeString(local, value);
            }
        }
        else
        {
            writer.WriteAttributeString(key, value);
        }
    }

    private void WriteChildren(XmlWriter writer)
    {
        foreach (var child in _children)
        {
            child.WriteTo(writer);
        }
    }

    private static readonly XmlParserContext DefaultContext;

    static XmppElement()
    {
        var nt = new NameTable();
        var nsmgr = new XmlNamespaceManager(nt);
        nsmgr.AddNamespace("stream", "http://etherx.jabber.org/streams");
        nsmgr.AddNamespace(string.Empty, "jabber:client");
        nsmgr.AddNamespace("xml", "http://www.w3.org/XML/1998/namespace");
        DefaultContext = new XmlParserContext(nt, nsmgr, null, XmlSpace.None);
    }

    public static XmppElement Parse(string xml, XmlParserContext? context = null)
    {
        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Prohibit
        };

        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings, context ?? DefaultContext);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                return ReadElement(reader);
            }
        }

        throw new FormatException("No root XML element found.");
    }

    public static XmppElement ReadElement(XmlReader reader)
    {
        var element = new XmppElement(reader.LocalName, reader.NamespaceURI, string.IsNullOrEmpty(reader.Prefix) ? null : reader.Prefix);

        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                element.Attr(reader.Name, reader.Value);
            }
            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
        {
            return element;
        }

        var textBuilder = new StringBuilder();
        var initialDepth = reader.Depth;

        while (reader.Read())
        {
            if (reader.Depth <= initialDepth && reader.NodeType == XmlNodeType.EndElement)
            {
                break;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var child = ReadElement(reader);
                    element.Child(child);
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    textBuilder.Append(reader.Value);
                    break;
            }
        }

        if (textBuilder.Length > 0)
        {
            element.Value = textBuilder.ToString();
        }

        return element;
    }
}
