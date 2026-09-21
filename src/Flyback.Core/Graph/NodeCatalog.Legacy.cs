using System.Collections.Immutable;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// Type ids Filter, Random, Slew, Drive, Delay and Reverb used while they were
    /// still Voice's and Effects', kept so a patch saved under one still opens.
    /// </summary>
    /// <remarks>
    /// Consulted by <see cref="ModuleCatalog.Get"/>, so every lookup resolves an old
    /// id the same way — the compiler, the text language, the workbench, and a
    /// module's <see cref="ModuleCatalog.Require"/>. <c>PatchIO.Read</c> goes
    /// further and rewrites a loaded node's <see cref="NodeInstance.TypeId"/> to the
    /// new id outright, which is what makes saving write the new one. See ADR-0128.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> LegacyTypeIds { get; } =
        new Dictionary<string, string>
        {
            ["flyback.voice.filter"] = FilterTypeId,
            ["flyback.voice.random"] = RandomTypeId,
            ["flyback.voice.slew"] = SlewTypeId,
            ["flyback.voice.drive"] = DriveTypeId,
            ["flyback.effects.delay"] = DelayTypeId,
            ["flyback.effects.reverb"] = ReverbTypeId,
        }.ToImmutableDictionary();
}
