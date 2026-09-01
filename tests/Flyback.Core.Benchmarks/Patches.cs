using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Core.Benchmarks;

/// <summary>Compiled programs the benchmarks evaluate, named by the preset they came from.</summary>
public static class Patches
{
    public static CompiledPatch Video(Func<ModuleCatalog, Patch> preset) =>
        preset(NodeCatalog.Current).CompileForVideo().Program;

    public static CompiledPatch Audio(Func<ModuleCatalog, Patch> preset) =>
        preset(NodeCatalog.Current).CompileForAudio().Program;
}
