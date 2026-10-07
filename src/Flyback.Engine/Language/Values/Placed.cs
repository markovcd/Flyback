using Flyback.Core.Graph;

namespace Flyback.Engine.Language.Values;

/// <summary>A placed module, standing for every one of its outputs at once.</summary>
internal sealed record Placed(Guid Id, NodeDef Def) : Value;
