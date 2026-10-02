namespace Flyback.Editor.Gallery;

/// <summary>
/// Where the build's stills are, drawn by <c>flyback-cli stills</c> (ADR-0163): a folder
/// beside the program, or a site's <c>stills/</c> for a page.
/// </summary>
internal interface IStillShelf
{
    /// <summary>The file called <paramref name="name"/>, or null where there is none.</summary>
    Task<byte[]?> Read(string name);
}
