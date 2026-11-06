namespace Flyback.Plugins.Assist;

/// <summary>How what an assistant builds reaches the person, which decides what <c>propose</c> and <c>reset</c> are.</summary>
internal enum Delivery
{
    /// <summary>A conversation in the editor's own column: a turn ends at a proposal, which the column puts on the canvas.</summary>
    Turn,

    /// <summary>A program driving the open editor: edits stay on the bench, and <c>propose</c> puts the patch on the canvas.</summary>
    Proposal,

    /// <summary>A patch file the tools write after each edit, so every edit is live and <c>propose</c> only checks.</summary>
    Live,
}
