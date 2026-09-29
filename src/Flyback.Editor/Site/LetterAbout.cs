namespace Flyback.App.Site;

/// <summary>What goes with a letter besides what was typed, which the letter shows before it is sent.</summary>
/// <param name="Version">This build, as the About window names it.</param>
/// <param name="Platform">The operating system, as the runtime describes it.</param>
/// <param name="Plugins">Which plugins loaded and which sound backend opened.</param>
internal sealed record LetterAbout(string Version, string Platform, string Plugins);