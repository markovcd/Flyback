namespace Flyback.Plugins.Assist;

/// <summary>
/// One thing an assistant may do to a patch, in the shape every provider's
/// function calling asks for: a name, prose the model reads to decide when to
/// reach for it, and a schema for the arguments.
/// </summary>
/// <param name="Schema">
/// The body of a JSON Schema object — its <c>properties</c> and <c>required</c>
/// — as JSON text. Text rather than a typed tree because every provider wants
/// JSON in the end and no two of them want the same object model to build it
/// from, so a tree here would be a thing each adapter had to undo.
/// </param>
public sealed record PatchTool(string Name, string Description, string Schema);