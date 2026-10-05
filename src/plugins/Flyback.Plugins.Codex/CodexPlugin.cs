namespace Flyback.Plugins.Codex;

/// <summary>
/// Offers an assistant that is the Codex the person is already signed in to.
/// </summary>
/// <remarks>
/// Flyback holds no token: the installed <c>codex</c> program is run and does its
/// own signing in, so the ChatGPT plan it is signed in to pays for the turns and no
/// API key exists anywhere in Flyback (ADR-0158).
/// </remarks>
// ReSharper disable once UnusedType.Global - found by reflection
public sealed class CodexPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.codex",
        "Codex",
        "Builds patches through the Codex you are already signed in to, with no API key.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new CodexAssistant());
}
