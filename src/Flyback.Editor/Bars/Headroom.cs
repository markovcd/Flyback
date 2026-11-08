namespace Flyback.Editor.Bars;

/// <summary>How far ahead of real time the sound is worked out, as the status bar's rungs show it.</summary>
internal enum Headroom
{
    /// <summary>Nothing is timed: no sound plays, or it plays on the interpreter, which says nothing of the machine.</summary>
    Untimed,

    /// <summary>Comfortably ahead.</summary>
    Plenty,

    /// <summary>Only just keeping up: a heavier patch or a busier machine and it falls behind.</summary>
    Tight,

    /// <summary>Slower than real time with no oversampling left to give up, so it stutters.</summary>
    Behind,
}
