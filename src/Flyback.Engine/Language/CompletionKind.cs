namespace Flyback.Engine.Language;

/// <summary>What a word offered for completion is.</summary>
internal enum CompletionKind
{
    /// <summary>A socket a call or a knob line names: <c>freq</c> in <c>sine(freq: 2)</c>.</summary>
    Socket,

    /// <summary>A def's parameter, named in its call or read in its body.</summary>
    Parameter,

    /// <summary>One of a module's outputs, taken by name after a dot.</summary>
    Output,

    /// <summary>A name the text bound with <c>let</c>.</summary>
    Name,

    /// <summary>A def the text wrote.</summary>
    Definition,

    /// <summary>A module in the catalog.</summary>
    Module,

    /// <summary>A name the language itself gives: <c>t</c>, <c>x</c>, <c>sum</c>, <c>_</c>.</summary>
    Builtin,

    /// <summary>A word that starts a statement or a loop.</summary>
    Keyword,
}
