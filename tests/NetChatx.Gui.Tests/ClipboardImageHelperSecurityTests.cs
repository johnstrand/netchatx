using System;
using System.IO;
using System.Threading.Tasks;
using NetChatx.Gui.Helpers;
using Xunit;

namespace NetChatx.Gui.Tests;

public class ClipboardImageHelperSecurityTests
{
    [Fact]
    public async Task FetchImageBytesAsync_WithHttpUrl_WhenAllowHttpIsFalse_ReturnsNull()
    {
        // Cleartext HTTP URL should return null by default when allowHttp is false
        var result = await ClipboardImageHelper.FetchImageBytesAsync("http://example.com/test.png", allowHttp: false);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchImageBytesAsync_WithDataUri_ReturnsDecodedBytes()
    {
        // 1x1 transparent PNG base64
        string base64Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        var bytes = await ClipboardImageHelper.FetchImageBytesAsync(base64Png);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task FetchImageBytesAsync_WithFileUriOrPath_ReturnsNull()
    {
        // Create a temp file on disk
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "dummy file content");

            // Direct path should return null
            var resultPath = await ClipboardImageHelper.FetchImageBytesAsync(tempFile);
            Assert.Null(resultPath);

            // file:// URI should return null
            var fileUri = new Uri(tempFile).AbsoluteUri;
            var resultUri = await ClipboardImageHelper.FetchImageBytesAsync(fileUri);
            Assert.Null(resultUri);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
