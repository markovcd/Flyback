using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Engine.Packs;

/// <summary>
/// The two modules a pack's module is built around to find its edges: a socket stands
/// for each input, a result takes each output. Neither is ever compiled; expanding a
/// placed module takes both out.
/// </summary>
internal static class PackStandIns
{
    public static readonly ModuleProvider Provider = new("flyback.pack", "Pack sockets");

    public const string SocketTypeId = "flyback.pack.socket";
    public const string ResultTypeId = "flyback.pack.result";

    private static readonly NodeDef Socket = new(
        SocketTypeId, "Socket", "Packs", [], [new PortSpec("out", PortKind.Any)], Never);

    private static readonly NodeDef Result = new(
        ResultTypeId, "Result", "Packs", [new PortSpec("in", PortKind.Any)], [], Never);

    /// <summary><paramref name="catalog"/> with the stand-ins in it, to build a module's stencil against.</summary>
    public static ModuleCatalog Into(ModuleCatalog catalog) =>
        catalog.HasProvider(Provider.Id) ? catalog : catalog.With(Provider, [Socket, Result]).Catalog;

    private static Slot[] Never(Emitter emitter, EmitContext node) =>
        throw new InvalidOperationException("A pack's stand-in is taken out before the patch is compiled.");
}
