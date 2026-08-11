using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// Lowers one node to register-machine ops. Inputs arrive already resolved —
/// either the upstream node's result or a constant from the port default — and
/// already coerced to the width the port declared.
/// </summary>
public delegate Slot[] EmitFn(Emitter emitter, Slot[] inputs);

/// <summary>
/// The static description of a node type: its sockets and how it compiles.
/// Adding a new module to the synth means adding one of these to
/// <see cref="NodeCatalog"/> — nothing else in the pipeline needs to change.
/// </summary>
public sealed record NodeDef(
    string TypeId,
    string Name,
    string Category,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs,
    EmitFn Emit,
    string Description = "");
