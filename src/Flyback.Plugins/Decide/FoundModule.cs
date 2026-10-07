using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>A module a phrase was taken to mean, and how likely the model thought it.</summary>
internal sealed record FoundModule(NodeDef Module, double Probability);
