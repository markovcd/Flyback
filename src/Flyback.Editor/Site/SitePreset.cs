using Flyback.Core.Graph;

namespace Flyback.App.Site;

/// <summary>A preset the site lists.</summary>
/// <param name="FileName">What it was shared as, whose extension says whether it is a bundle.</param>
/// <param name="Still">A frame of it, once the site has rendered one.</param>
/// <param name="Rating">Its stars on the site, which only the site gives.</param>
/// <param name="Listed">Everything the site said of it, as the JSON it said it in.</param>
/// <param name="PageLacks">What the editor in a page lacks to open it, in a few words, or null where a page opens it.</param>
internal sealed record SitePreset(
    string Id,
    string Name,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    string FileName,
    Uri File,
    Uri? Still,
    SiteRating Rating,
    string Listed,
    string? PageLacks = null) : IPreset;