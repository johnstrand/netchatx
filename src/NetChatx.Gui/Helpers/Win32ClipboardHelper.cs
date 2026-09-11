using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace NetChatx.Gui.Helpers;

internal static class Win32ClipboardHelper
{
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint uFormat);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_DIB = 8;

    public static byte[]? GetImageBytesFromClipboard()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!OpenClipboard(IntPtr.Zero)) return null;

        try
        {
            // 1. Try registered PNG format (used by Windows 10/11 Snipping Tool, browsers)
            uint pngFormat = RegisterClipboardFormat("PNG");
            if (pngFormat != 0 && IsClipboardFormatAvailable(pngFormat))
            {
                var hData = GetClipboardData(pngFormat);
                if (hData != IntPtr.Zero)
                {
                    var ptr = GlobalLock(hData);
                    if (ptr != IntPtr.Zero)
                    {
                        try
                        {
                            int size = (int)GlobalSize(hData);
                            if (size > 0)
                            {
                                byte[] bytes = new byte[size];
                                Marshal.Copy(ptr, bytes, 0, size);
                                return bytes;
                            }
                        }
                        finally
                        {
                            GlobalUnlock(hData);
                        }
                    }
                }
            }

            // 2. Try CF_DIB (standard Windows Device-Independent Bitmap)
            if (IsClipboardFormatAvailable(CF_DIB))
            {
                var hData = GetClipboardData(CF_DIB);
                if (hData != IntPtr.Zero)
                {
                    var ptr = GlobalLock(hData);
                    if (ptr != IntPtr.Zero)
                    {
                        try
                        {
                            int dibSize = (int)GlobalSize(hData);
                            if (dibSize > 40)
                            {
                                byte[] dibData = new byte[dibSize];
                                Marshal.Copy(ptr, dibData, 0, dibSize);
                                return ConvertDibToPngBytes(dibData);
                            }
                        }
                        finally
                        {
                            GlobalUnlock(hData);
                        }
                    }
                }
            }
        }
        catch
        {
            // Soft failure on native clipboard read
        }
        finally
        {
            CloseClipboard();
        }

        return null;
    }

    public static byte[]? GetGifBytesFromClipboard()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!OpenClipboard(IntPtr.Zero)) return null;

        try
        {
            uint gifFormat = RegisterClipboardFormat("GIF");
            if (gifFormat != 0 && IsClipboardFormatAvailable(gifFormat))
            {
                var bytes = ReadClipboardBytes(gifFormat);
                if (bytes is not null && bytes.Length > 0 && GifDecoder.IsGif(bytes)) return bytes;
            }

            uint imageGifFormat = RegisterClipboardFormat("image/gif");
            if (imageGifFormat != 0 && IsClipboardFormatAvailable(imageGifFormat))
            {
                var bytes = ReadClipboardBytes(imageGifFormat);
                if (bytes is not null && bytes.Length > 0 && GifDecoder.IsGif(bytes)) return bytes;
            }
        }
        catch
        {
            // Soft failure
        }
        finally
        {
            CloseClipboard();
        }

        return null;
    }

    public static string? GetHtmlFromClipboard()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!OpenClipboard(IntPtr.Zero)) return null;

        try
        {
            uint htmlFormat = RegisterClipboardFormat("HTML Format");
            if (htmlFormat != 0 && IsClipboardFormatAvailable(htmlFormat))
            {
                var bytes = ReadClipboardBytes(htmlFormat);
                if (bytes is not null && bytes.Length > 0)
                {
                    int nullIndex = Array.IndexOf(bytes, (byte)0);
                    int length = nullIndex >= 0 ? nullIndex : bytes.Length;
                    return System.Text.Encoding.UTF8.GetString(bytes, 0, length);
                }
            }
        }
        catch
        {
            // Soft failure
        }
        finally
        {
            CloseClipboard();
        }

        return null;
    }

    private static byte[]? ReadClipboardBytes(uint format)
    {
        var hData = GetClipboardData(format);
        if (hData == IntPtr.Zero) return null;

        var ptr = GlobalLock(hData);
        if (ptr == IntPtr.Zero) return null;

        try
        {
            int size = (int)GlobalSize(hData);
            if (size > 0)
            {
                byte[] bytes = new byte[size];
                Marshal.Copy(ptr, bytes, 0, size);
                return bytes;
            }
        }
        finally
        {
            GlobalUnlock(hData);
        }

        return null;
    }

    public static byte[]? ConvertDibToPngBytes(byte[] dibData)
    {
        try
        {
            int headerSize = BitConverter.ToInt32(dibData, 0);
            short bpp = BitConverter.ToInt16(dibData, 14);
            int compression = BitConverter.ToInt32(dibData, 16);
            int clrUsed = BitConverter.ToInt32(dibData, 32);

            int paletteColors = clrUsed;
            if (paletteColors == 0 && bpp <= 8)
            {
                paletteColors = 1 << bpp;
            }

            int paletteSize = paletteColors * 4;
            if (compression == 3 /* BI_BITFIELDS */ && headerSize == 40)
            {
                paletteSize = 12; // 3 color masks
            }

            int offBits = 14 + headerSize + paletteSize;
            int fileSize = 14 + dibData.Length;

            using var ms = new MemoryStream(fileSize);
            using var bw = new BinaryWriter(ms);

            // Write BITMAPFILEHEADER (14 bytes)
            bw.Write((byte)'B');
            bw.Write((byte)'M');
            bw.Write(fileSize);
            bw.Write((short)0); // reserved1
            bw.Write((short)0); // reserved2
            bw.Write(offBits);

            // Write DIB bytes
            bw.Write(dibData);
            bw.Flush();

            ms.Position = 0;
            using var bmp = new Bitmap(ms);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs);
            return pngMs.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
