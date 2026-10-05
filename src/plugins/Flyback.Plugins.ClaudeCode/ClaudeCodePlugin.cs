namespace Flyback.Plugins.ClaudeCode;

/// <summary>
/// Offers an assistant that is the Claude Code the person is already signed in to.
/// </summary>
/// <remarks>
/// Flyback holds no token: the installed <c>claude</c> program is run and does its
/// own signing in, so the plan it is signed in to pays for the turns and no API key
/// exists anywhere in Flyback (ADR-0158).
/// </remarks>
// ReSharper disable once UnusedType.Global - found by reflection
public sealed class ClaudeCodePlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.claudecode",
        "Claude Code",
        "Builds patches through the Claude Code you are already signed in to, with no API key.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new ClaudeCodeAssistant());
}
