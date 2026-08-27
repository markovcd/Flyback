using Flyback.Core.Graph;

namespace Flyback.Plugins.Noise;

/// <summary>
/// The two noises everything organic is made of, neither of which the catalogue
/// had.
/// </summary>
/// <remarks>
/// What shipped was one octave of value noise: a field of smooth blobs, all the
/// same size, which is a texture nothing in the world has. Every generative
/// picture that looks like weather, stone, smoke, terrain, rust or skin is built
/// out of one of two things this adds — the same noise summed at several sizes,
/// or the distance to a set of scattered points — and a patch could assemble
/// neither. The first would have taken eight Noise modules and thirty wires; the
/// second is not assemblable at all, because it needs the same field read at
/// nine places that depend on where you are.
/// <para>
/// Both are arithmetic over ops the engine already has, so both cost the same at
/// either sink and survive to the shader — which for a video plugin is the gate
/// that matters, since a program the GLSL backend cannot draw takes the preview
/// back to the processor for as long as it is loaded. Neither reaches for a
/// table, a cell or a delay line.
/// </para>
/// <para>
/// The one thing here that is not a socket is the Fractal's octave count, and
/// that is the interesting decision: it settles how long the program is rather
/// than what it computes, so it is carried on the node the way a Quantiser's
/// scale is ([0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)) and drawn
/// from what it declares rather than from a control this ships
/// ([0055](0055-a-plugins-extra-declares-its-editor.md)). It is the first
/// shipped plugin to carry anything at all.
/// </para>
/// <para>
/// What is deliberately not here is a domain-warp module. Warping a fractal by
/// another fractal is the third famous thing in this subject and needs nothing
/// new: it is a Fractal into the Warp the catalogue has had all along, into a
/// second Fractal. The <c>Marble</c> preset is that patch, and it is three wires.
/// </para>
/// </remarks>
public sealed class NoisePlugin : IFlybackPlugin
{
    internal static ModuleProvider Provider { get; } = new("flyback.noise", "Noise");

    public PluginInfo Info { get; } = new(
        "flyback.noise",
        "Noise",
        "Fractal noise and cellular noise: the two fields everything organic is made of.");

    public void Register(IPluginRegistry registry)
    {
        registry.AddModules(Provider, [FractalModule.Definition, CellsModule.Definition]);
        registry.AddPresets([new PatchPreset(MarblePreset.Name, MarblePreset.Build)]);
    }
}
