using Flyback.Core.Graph;

namespace Flyback.Engine.Language.Values;

/// <summary>One output of a placed module.</summary>
internal sealed record Socket(Guid Id, NodeDef Def, int Port) : Value;
