using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>Lowers one node to register-machine ops.</summary>
public delegate Slot[] EmitFn(Emitter emitter, EmitContext node);