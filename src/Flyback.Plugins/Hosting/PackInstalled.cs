namespace Flyback.Plugins.Hosting;

/// <summary>A pack written into the packs folder.</summary>
/// <param name="Replaced">The version the folder held under the same id before, or null where it held none.</param>
/// <param name="Modules">The modules it offers, by type id.</param>
/// <param name="File">Where it was written.</param>
internal sealed record PackInstalled(string Id, string Version, string? Replaced, IReadOnlyList<string> Modules, string File);
