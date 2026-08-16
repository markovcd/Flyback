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

/// <summary>
/// What a tool call produced.
/// </summary>
/// <remarks>
/// A refusal is a value, not an exception, and that is load-bearing rather than
/// a matter of taste: providers reject the whole next request when a tool call
/// has no matching result, so a tool that threw would end the conversation
/// instead of the call. The model is meant to read <see cref="Text"/>, learn
/// what it did wrong, and try again — which it cannot do if it never gets a
/// turn. The same reasoning already governs <see cref="Hosting.PluginProblem"/>
/// and <see cref="Core.Graph.ModuleAddition"/>.
/// </remarks>
public sealed record ToolOutcome(bool Ok, string Text, byte[]? Png = null)
{
    public static ToolOutcome Fine(string text) => new(true, text);

    public static ToolOutcome Refused(string text) => new(false, text);

    /// <summary>A picture and the words that go with it — see <c>render</c>.</summary>
    public static ToolOutcome Looked(byte[] png, string caption) => new(true, caption, png);
}
