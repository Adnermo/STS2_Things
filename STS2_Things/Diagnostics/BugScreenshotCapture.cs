using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Godot;

namespace STS2_Things.Diagnostics;

/// <summary>Uses the original F2 screenshot, taken before opening the feedback UI.</summary>
internal static class BugScreenshotCapture
{
    internal static JsonObject Capture(Stream source)
    {
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + count > 4_000_000) return Unavailable("source_too_large");
                buffer.Write(chunk, 0, count);
            }
            using var image = new Image();
            if (image.LoadPngFromBuffer(buffer.ToArray()) != Error.Ok || image.IsEmpty()) return Unavailable("no_native_screenshot");
            float scale = Math.Min(1f, 960f / image.GetWidth());
            image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Bilinear);
            byte[] bytes = image.SaveJpgToBuffer(.72f);
            if (bytes.Length > 60_000)
            {
                image.Resize(Math.Max(1, image.GetWidth() * 3 / 4), Math.Max(1, image.GetHeight() * 3 / 4), Image.Interpolation.Bilinear);
                bytes = image.SaveJpgToBuffer(.55f);
            }
            if (bytes.Length > 60_000) return Unavailable("thumbnail_too_large");
            return new JsonObject { ["available"] = true, ["format"] = "things-screenshot", ["encoding"] = "jpeg-base64",
                ["width"] = image.GetWidth(), ["height"] = image.GetHeight(), ["bytes"] = bytes.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ["data"] = Convert.ToBase64String(bytes) };
        }
        catch (Exception) { return Unavailable("capture_failed"); }
    }

    private static JsonObject Unavailable(string reason) => new() { ["available"] = false, ["reason"] = reason };
}
