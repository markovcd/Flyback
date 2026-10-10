namespace Flyback.Tests;

/// <summary>A preset as the fake site lists it.</summary>
/// <param name="Needs">The plugin a browser page lacks to open it, which the site lists as <c>lacks</c>.</param>
internal sealed record Posted(
    string Id,
    string Name,
    string Author = "",
    string Description = "",
    string FileName = "",
    byte[]? File = null,
    double Average = 0,
    int Ratings = 0,
    string[]? Tags = null,
    byte[]? Still = null,
    string? Needs = null);
