namespace Flyback.Core.Compile;

/// <summary>Anything the compiler wants to tell the user about a patch.</summary>
/// <remarks>
/// Beside the module API rather than beside the compiler, because a module's
/// extra may raise one: what a node carries can be wrong in ways only the part
/// that reads it can say.
/// </remarks>
public sealed record CompileIssue(Guid? NodeId, string Message, IssueSeverity Severity = IssueSeverity.Error);
