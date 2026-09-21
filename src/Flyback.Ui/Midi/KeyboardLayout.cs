namespace Flyback.App.Midi;

/// <summary>How the computer keyboard is laid out on a patch that has just gained its first MIDI In.</summary>
public enum KeyboardLayout
{
    /// <summary>The tracker's piano: white notes on the bottom row, black ones above.</summary>
    Piano,

    /// <summary>The notes of a scale, one to a key.</summary>
    Scale,
}
