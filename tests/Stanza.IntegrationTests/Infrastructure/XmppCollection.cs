using Xunit;

namespace Stanza.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public class XmppCollection : ICollectionFixture<XmppContainerFixture>
{
    public const string Name = "XmppContainer";
}
