using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>The pictures behind the modules, read once out of the assembly.</summary>
internal static class Art
{
    /// <summary>A block picture and the panel's, named as the resources are.</summary>
    public static ModuleSkin.Artwork Skin(string name) => new(Bytes($"art.{name}.svg"))
    {
        Panel = Bytes($"art.{name}-panel.svg"),
    };

    private static byte[] Bytes(string resource)
    {
        using var stream = typeof(Art).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} is not embedded in the plugin.");
        using var memory = new MemoryStream();

        stream.CopyTo(memory);

        return memory.ToArray();
    }
}
