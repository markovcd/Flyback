namespace Flyback.Engine.Language;

/// <summary>What opened a <see cref="Level"/> in the text.</summary>
public enum LevelKind
{
    /// <summary>A <c>group</c> block.</summary>
    Group,

    /// <summary>A call to a def, stamped out.</summary>
    Call,

    /// <summary>A <c>for</c> loop, holding one <see cref="Pass"/> per item.</summary>
    Loop,

    /// <summary>One pass of a loop.</summary>
    Pass,
}
