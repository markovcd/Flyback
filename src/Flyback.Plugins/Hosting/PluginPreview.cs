namespace Flyback.Plugins.Hosting;

/// <summary>The one image a plugin shows itself with.</summary>
/// <param name="MediaType"><c>image/png</c> or <c>image/webp</c>.</param>
internal sealed record PluginPreview(string MediaType, byte[] Bytes)
{
    /// <summary>The one preview an assembly embeds, or null for none.</summary>
    /// <exception cref="InvalidDataException">Where there are two, or it is too large, or it is not the image its name says.</exception>
    public static PluginPreview? Of(IReadOnlyList<EmbeddedPreview> previews)
    {
        if (previews.Count == 0) return null;

        if (previews.Count > 1)
            throw new InvalidDataException($"Its plugin assembly embeds {previews.Count} previews, where a plugin has one preview.");

        var (name, length, bytes) = previews[0];

        if (bytes is null)
            throw new InvalidDataException($"Its preview, {name}, is {length >> 10} KB, larger than the {AssemblyFacts.PreviewLimit >> 20} MB a preview may be.");

        var type = AssemblyFacts.PreviewNames[name];

        if (!Looks(type, bytes))
            throw new InvalidDataException($"Its preview, {name}, is not a {(type == "image/png" ? "PNG" : "WebP")} image.");

        return new PluginPreview(type, bytes);
    }

    /// <summary>Whether <paramref name="bytes"/> start the way an image of <paramref name="type"/> does.</summary>
    private static bool Looks(string type, byte[] bytes) => type == "image/png"
        ? bytes.AsSpan().StartsWith(PngSignature)
        : bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}