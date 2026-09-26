namespace Flyback.App.Statistics;

/// <summary>
/// The things a run is counted doing, each said at the end as how many times — in
/// a band — and nothing about what it was done to (ADR-0103).
/// </summary>
public enum Used
{
    /// <summary>A patch began to play.</summary>
    Played,

    /// <summary>A preset was picked from the list.</summary>
    Preset,

    /// <summary>A patch, a bundle or a text file was opened.</summary>
    Opened,

    /// <summary>A patch, a bundle or a text file was saved.</summary>
    Saved,

    /// <summary>A module was added to the canvas.</summary>
    Added,

    /// <summary>A take was recorded to the end and written.</summary>
    Recorded,

    /// <summary>The preview was given the whole window.</summary>
    FullScreen,

    /// <summary>The preview and the canvas swapped places.</summary>
    Swapped,

    /// <summary>The text view was put over the canvas.</summary>
    Text,

    /// <summary>A MIDI device that is not the computer keyboard was heard.</summary>
    Instrument,

    /// <summary>A message was sent to an assistant.</summary>
    Asked,

    /// <summary>The settings window was opened.</summary>
    Settings,
}