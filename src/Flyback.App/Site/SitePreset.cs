namespace Flyback.App.Site;

/// <summary>A preset the site lists.</summary>
/// <param name="FileName">What it was shared as, whose extension says whether it is a bundle.</param>
/// <param name="Still">A frame of it, once the site has rendered one.</param>
/// <param name="Rating">Its stars on the site, which only the site gives.</param>
internal sealed record SitePreset(
    string Id,
    string Name,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    string FileName,
    Uri File,
    Uri? Still,
    SiteRating Rating);