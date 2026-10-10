using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.FakeAssistant;

/// <summary>
/// A plugin that offers an assistant which has already made up its mind.
/// </summary>
/// <remarks>
/// It exists to be loaded off disk by the tests, and it doubles as the worked
/// example: this is the whole of what contributing an assistant takes, minus
/// the part where a provider is asked anything. Everything it does goes through
/// <see cref="PatchWorkbench"/> and the contract, so if it can build a patch
/// from out here then so can a real one.
/// </remarks>
public sealed class RehearsedAssistantPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.rehearsed",
        "Rehearsed assistant",
        "Replays a fixed sequence of edits, so the tests can drive the whole path without a network.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new RehearsedAssistant());
}
