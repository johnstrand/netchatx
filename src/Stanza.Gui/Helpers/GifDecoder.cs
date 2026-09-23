using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Stanza.Gui.Helpers;

public static class GifDecoder
{
    public static bool IsGif(ReadOnlySpan<byte> data)
    {
        return data.Length >= 6 &&
               data[0] == (byte)'G' &&
               data[1] == (byte)'I' &&
               data[2] == (byte)'F' &&
               data[3] == (byte)'8' &&
               (data[4] == (byte)'7' || data[4] == (byte)'8' || data[4] == (byte)'9') &&
               data[5] == (byte)'a';
    }

    public static List<(Bitmap Bitmap, int DurationMs)>? DecodeFrames(byte[] gifBytes, int maxFrames = 100)
    {
        if (gifBytes is null || gifBytes.Length == 0 || !IsGif(gifBytes))
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(gifBytes);
            using var codec = SKCodec.Create(ms);
            if (codec is null || codec.FrameCount <= 1)
            {
                return null;
            }

            var frameCount = Math.Min(codec.FrameCount, maxFrames);
            var frames = new List<(Bitmap Bitmap, int DurationMs)>(frameCount);

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvasBitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(canvasBitmap);

            for (int i = 0; i < frameCount; i++)
            {
                var frameInfo = codec.FrameInfo[i];
                var duration = frameInfo.Duration;
                if (duration <= 10) duration = 100; // 100ms default for 0 or sub-10ms frame delays

                using var frameBitmap = new SKBitmap(info);
                var options = new SKCodecOptions(i);
                var result = codec.GetPixels(info, frameBitmap.GetPixels(), options);

                if (result == SKCodecResult.Success)
                {
                    canvas.DrawBitmap(frameBitmap, 0, 0);

                    using var image = SKImage.FromBitmap(canvasBitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var frameStream = new MemoryStream(data.ToArray());
                    var avaloniaBmp = new Bitmap(frameStream);
                    frames.Add((avaloniaBmp, duration));
                }
                else
                {
                    using var image = SKImage.FromBitmap(frameBitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var frameStream = new MemoryStream(data.ToArray());
                    var avaloniaBmp = new Bitmap(frameStream);
                    frames.Add((avaloniaBmp, duration));
                }
            }

            return frames.Count > 1 ? frames : null;
        }
        catch
        {
            return null;
        }
    }
}
