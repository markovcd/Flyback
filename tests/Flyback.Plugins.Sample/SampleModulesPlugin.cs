using Flyback.Core.Compile;
using Flyback.Core.Graph;

// Every module Register adds, so it can be listed before the plugin runs.
[assembly: Flyback.Plugins.FlybackModule("flyback.sample.ripple", "Ripple")]
[assembly: Flyback.Plugins.FlybackModule("flyback.sample.halve", "Halve")]

namespace Flyback.Plugins.Sample;

/// <summary>
/// A plugin that adds modules rather than a device. It exists to be loaded off
/// disk by the tests, and doubles as the worked example: this is the whole of
/// what adding a module from outside the engine takes.
/// </summary>
public sealed class SampleModulesPlugin : IFlybackPlugin
{
    private const float Tau = 6.283185307179586f;

    /// <summary>
    /// Every module below is named <c>flyback.sample.…</c> after this id. The
    /// catalog enforces that, and it is what lets a saved patch say which
    /// plugin a module came from without having the plugin to ask.
    /// </summary>
    private static readonly ModuleProvider Provider = new("flyback.sample", "Sample modules");

    public PluginInfo Info { get; } = new(
        "flyback.sample",
        "Sample modules",
        "Example modules, used by the tests to load a real plugin off disk.");

    public void Register(IPluginRegistry registry) => registry.AddModules(Provider,
    [
        new NodeDef(
            "flyback.sample.ripple", "Ripple", "Sample",
            [
                new PortSpec("x") { Standard = true },
                new PortSpec("y") { Standard = true },
                new PortSpec("freq", PortKind.Scalar, 4f, 0f, 32f) { Help = "Rings to each unit out from the middle." },
                new PortSpec("offset") { Help = "Shifts the rings, one whole ring at 1." },
            ],
            [new PortSpec("out") { Help = "The rings, -1 to 1." }],
            (em, i) =>
            {
                var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
            },
            "Concentric sine rings. Drive offset from Time to pulse outward.")
        {
            // A background of its own, which a plugin's category gets none of:
            // "Sample" is nothing the shell has an accent for, so these would
            // otherwise both be gray. A grain over the color, and a mark on the
            // same twenty-four unit box the engine's own are drawn on.
            Skin = new ModuleSkin.Grain(new Swatch(0x4A, 0x7E, 0xC8), GrainCut.Beaded)
            {
                Glyph = "M4,12 A8,8 0 1,1 20,12 A8,8 0 1,1 4,12 M9,12 A3,3 0 1,1 15,12 A3,3 0 1,1 9,12",
            },
        },

        new NodeDef(
            "flyback.sample.halve", "Halve", "Sample",
            [new PortSpec("in", PortKind.Any) { Help = "A number or a color." }],
            [new PortSpec("out", PortKind.Any) { Help = "Half of it." }],
            (em, i) => [em.Mul(i[0], 0.5f)],
            "Halves whatever arrives — scalar or color.")
        {
            // The same blue without the grain, so the two read as one plugin and
            // still tell each other apart.
            Skin = new ModuleSkin.Palette(new Swatch(0x4A, 0x7E, 0xC8)),
        },
    ]);
}
