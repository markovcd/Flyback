namespace Flyback.Plugins.SystemOne;

/// <summary>Offers a decision model reached over HTTP in the System One format.</summary>
// ReSharper disable once UnusedType.Global - found by reflection
public sealed class SystemOnePlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.systemone",
        "Decision server",
        "Answers the editor's questions through an endpoint that speaks the System One format, such as a laya-serve.");

    public void Register(IPluginRegistry registry) => registry.AddDecisionModel(new SystemOneModel());
}
