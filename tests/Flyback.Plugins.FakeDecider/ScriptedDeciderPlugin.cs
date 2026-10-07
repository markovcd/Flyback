namespace Flyback.Plugins.FakeDecider;

/// <summary>Offers a decision model that answers from a script, so the tests can drive every use of one without a model.</summary>
public sealed class ScriptedDeciderPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.scripted-decider",
        "Scripted decider",
        "Answers questions from a script, so the tests can drive decisions without a model.");

    public void Register(IPluginRegistry registry) => registry.AddDecisionModel(new ScriptedDecider());
}
