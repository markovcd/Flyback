using Flyback.Core.Compile;
using Flyback.Core.Graph;

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
    /// catalogue enforces that, and it is what lets a saved patch say which
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
                new PortSpec("x"),
                new PortSpec("y"),
                new PortSpec("freq", PortKind.Scalar, 4f, 0f, 32f),
                new PortSpec("offset"),
            ],
            [new PortSpec("out")],
            (em, i) =>
            {
                var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
            },
            "Concentric sine rings. Drive offset from Time to pulse outward."),

        new NodeDef(
            "flyback.sample.halve", "Halve", "Sample",
            [new PortSpec("in", PortKind.Any)],
            [new PortSpec("out", PortKind.Any)],
            (em, i) => [em.Mul(i[0], 0.5f)],
            "Halves whatever arrives — scalar or color."),
    ]);
}
