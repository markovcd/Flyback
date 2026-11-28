namespace Flyback.Plugins.Hosting;

/// <summary>A module pack the preset site lists: one published version of it.</summary>
/// <param name="Id">The site's id for this version, which its file is fetched by.</param>
/// <param name="Pack">The pack's own id, as its header names it.</param>
/// <param name="Modules">The modules it offers, by type id and name.</param>
internal sealed record SitePack(
    string Id,
    string Pack,
    string Version,
    IReadOnlyList<(string Id, string Name)> Modules,
    string Sha256,
    long Size,
    int Downloads,
    Uri File);
