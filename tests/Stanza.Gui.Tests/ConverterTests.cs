using System;
using System.Globalization;
using Avalonia.Layout;
using Avalonia.Media;
using Stanza.Gui.Converters;
using Stanza.Gui.ViewModels;
using Stanza.Storage.Models;
using Xunit;

namespace Stanza.Gui.Tests;

public class ConverterTests
{
    [Fact]
    public void DirectionToAlignmentConverter_Convert_ReturnsExpectedAlignment()
    {
        var conv = DirectionToAlignmentConverter.Instance;

        Assert.Equal(HorizontalAlignment.Right, conv.Convert(MessageDirection.Outbound, typeof(HorizontalAlignment), null, CultureInfo.InvariantCulture));
        Assert.Equal(HorizontalAlignment.Left, conv.Convert(MessageDirection.Inbound, typeof(HorizontalAlignment), null, CultureInfo.InvariantCulture));
        Assert.Equal(HorizontalAlignment.Left, conv.Convert(null, typeof(HorizontalAlignment), null, CultureInfo.InvariantCulture));
        Assert.Equal(HorizontalAlignment.Left, conv.Convert("invalid", typeof(HorizontalAlignment), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DirectionToAlignmentConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var conv = DirectionToAlignmentConverter.Instance;
        Assert.Throws<NotSupportedException>(() => conv.ConvertBack(HorizontalAlignment.Left, typeof(MessageDirection), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DirectionToBackgroundConverter_Convert_ReturnsExpectedBrushes()
    {
        var conv = DirectionToBackgroundConverter.Instance;

        var outbound = conv.Convert(MessageDirection.Outbound, typeof(IBrush), null, CultureInfo.InvariantCulture);
        var inbound = conv.Convert(MessageDirection.Inbound, typeof(IBrush), null, CultureInfo.InvariantCulture);
        var fallback = conv.Convert(null, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<IBrush>(outbound);
        Assert.IsAssignableFrom<IBrush>(inbound);
        Assert.IsAssignableFrom<IBrush>(fallback);
        Assert.NotEqual(outbound, inbound);
        Assert.Equal(inbound, fallback);
    }

    [Fact]
    public void DirectionToBackgroundConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var conv = DirectionToBackgroundConverter.Instance;
        Assert.Throws<NotSupportedException>(() => conv.ConvertBack(null, typeof(MessageDirection), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DirectionToForegroundConverter_Convert_ReturnsExpectedBrushes()
    {
        var conv = DirectionToForegroundConverter.Instance;

        var outbound = conv.Convert(MessageDirection.Outbound, typeof(IBrush), null, CultureInfo.InvariantCulture);
        var inbound = conv.Convert(MessageDirection.Inbound, typeof(IBrush), null, CultureInfo.InvariantCulture);
        var fallback = conv.Convert("invalid", typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<IBrush>(outbound);
        Assert.IsAssignableFrom<IBrush>(inbound);
        Assert.Equal(inbound, fallback);
    }

    [Fact]
    public void DirectionToForegroundConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var conv = DirectionToForegroundConverter.Instance;
        Assert.Throws<NotSupportedException>(() => conv.ConvertBack(null, typeof(MessageDirection), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PresenceToBrushConverter_Convert_HandlesAllPresenceStates()
    {
        var conv = PresenceToBrushConverter.Instance;

        var greenOnline = conv.Convert("online", typeof(IBrush), null, CultureInfo.InvariantCulture);
        var greenAvailable = conv.Convert("available", typeof(IBrush), null, CultureInfo.InvariantCulture);
        var greenChat = conv.Convert("chat", typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Equal(greenOnline, greenAvailable);
        Assert.Equal(greenOnline, greenChat);

        var amberAway = conv.Convert("away", typeof(IBrush), null, CultureInfo.InvariantCulture);
        var amberXa = conv.Convert("xa", typeof(IBrush), null, CultureInfo.InvariantCulture);
        Assert.Equal(amberAway, amberXa);

        var redDnd = conv.Convert("dnd", typeof(IBrush), null, CultureInfo.InvariantCulture);
        var grayOffline = conv.Convert("offline", typeof(IBrush), null, CultureInfo.InvariantCulture);
        var grayNull = conv.Convert(null, typeof(IBrush), null, CultureInfo.InvariantCulture);
        var grayUnknown = conv.Convert("unknown", typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Equal(grayOffline, grayNull);
        Assert.Equal(grayOffline, grayUnknown);

        Assert.NotEqual(greenOnline, amberAway);
        Assert.NotEqual(amberAway, redDnd);
        Assert.NotEqual(redDnd, grayOffline);
    }

    [Fact]
    public void PresenceToBrushConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var conv = PresenceToBrushConverter.Instance;
        Assert.Throws<NotSupportedException>(() => conv.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BoolToLockIconConverter_Convert_ReturnsExpectedText()
    {
        var conv = BoolToLockIconConverter.Instance;

        Assert.Equal("🔒 OMEMO", conv.Convert(true, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("", conv.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("", conv.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("", conv.Convert("not-bool", typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BoolToLockIconConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var conv = BoolToLockIconConverter.Instance;
        Assert.Throws<NotSupportedException>(() => conv.ConvertBack("", typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CodeLanguageItem_Properties_WorkAsExpected()
    {
        var item = new CodeLanguageItem("C# / .NET", "csharp");

        Assert.Equal("C# / .NET", item.Name);
        Assert.Equal("csharp", item.Identifier);
        Assert.Contains("C# / .NET", item.ToString());
    }
}
