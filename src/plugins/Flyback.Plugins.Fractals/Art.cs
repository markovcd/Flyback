using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>The pictures behind the modules, each drawn by its own module and read once out of the assembly.</summary>
/// <remarks>
/// Each PNG is the <c>.fbks</c> beside it, darkened so white labels read over it:
/// <c>flyback-cli render &lt;name&gt;.fbks -o &lt;name&gt;.png --at 0</c> with
/// <c>--size 360x360</c> for a block and <c>--size 240x640</c> for a panel.
/// </remarks>
internal static class Art
{
    /// <summary>A block picture and the panel's, named as the resources are.</summary>
    public static ModuleSkin.Artwork Skin(string name) => new(Bytes($"art.{name}.png"))
    {
        Panel = Bytes($"art.{name}-panel.png"),
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
