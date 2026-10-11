namespace Flyback.Editor;

/// <summary>What pressing Record would do now, which is what its button shows.</summary>
internal enum TakeMode
{
    /// <summary>Nothing: nothing is wired into the Output, so there is nothing to record.</summary>
    Nothing,

    /// <summary>Start a take.</summary>
    Ready,

    /// <summary>Call off the count-in.</summary>
    Counting,

    /// <summary>End the take and write the file.</summary>
    Recording,

    /// <summary>Nothing: the last take's file is still being written.</summary>
    Finishing,
}
