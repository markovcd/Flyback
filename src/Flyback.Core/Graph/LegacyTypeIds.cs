using System.Collections.Immutable;

namespace Flyback.Core.Graph;

/// <summary>The type ids modules were saved under before they moved.</summary>
internal static class LegacyTypeIds
{
    /// <summary>
    /// Type ids Filter, Noise, Slew, Drive, Delay and Reverb used while they were
    /// still Voice's and Effects', kept so a patch saved under one still opens.
    /// </summary>
    /// <remarks>
    /// Consulted by <see cref="ModuleCatalog.Get"/>, so every lookup resolves an old
    /// id the same way — the compiler, the text language, the workbench, and a
    /// module's <see cref="ModuleCatalog.Require"/>. <c>PatchIO.Read</c> goes
    /// further and rewrites a loaded node's <see cref="NodeInstance.TypeId"/> to the
    /// new id outright, which is what makes saving write the new one. See ADR-0128.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> Renamed { get; } =
        new Dictionary<string, string>
        {
            ["flyback.voice.filter"] = FilterModule.TypeId,
            ["flyback.voice.random"] = NoiseModule.TypeId,
            ["flyback.voice.slew"] = SlewModule.TypeId,
            ["flyback.voice.drive"] = DriveModule.TypeId,
            ["flyback.effects.delay"] = DelayModule.TypeId,
            ["flyback.effects.reverb"] = ReverbModule.TypeId,
        }.ToImmutableDictionary();
}
