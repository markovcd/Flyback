namespace Flyback.Plugins.Assist;

/// <summary>
/// Everything a workbench has to be given back to carry on a conversation that was
/// put away: the patch it started from, the one it is building, what it calls each
/// module, and how much of the run it has spent.
/// </summary>
/// <param name="Start">
/// The patch the run began on, as patch JSON — what <c>reset</c> goes back to, and
/// what a workbench carrying the conversation on is built over.
/// </param>
/// <param name="Working">The patch being built, as patch JSON.</param>
/// <param name="Handles">
/// What each module is called, by id. Kept rather than worked out again, because a
/// handle the model chose for itself is not the one the type id would give, and the
/// conversation is written in these names.
/// </param>
/// <param name="Edits"></param>
/// <param name="ToolCalls"></param>
public sealed record WorkbenchState(
    string Start,
    string Working,
    IReadOnlyDictionary<string, Guid> Handles,
    int Edits,
    int ToolCalls);
