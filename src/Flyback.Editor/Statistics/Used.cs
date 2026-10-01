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

    /// <summary>A step was taken back.</summary>
    Undone,

    /// <summary>A step taken back was put again.</summary>
    Redone,

    /// <summary>The canvas or the text was tidied.</summary>
    Tidied,

    /// <summary>Modules were copied or cut.</summary>
    Copied,

    /// <summary>Modules were pasted.</summary>
    Pasted,

    /// <summary>The selection was duplicated.</summary>
    Duplicated,

    /// <summary>Modules were switched off or on.</summary>
    Switched,

    /// <summary>Modules were drawn as a group.</summary>
    Grouped,

    /// <summary>A group was undrawn.</summary>
    Ungrouped,

    /// <summary>Modules were Shift-dragged into or out of a group.</summary>
    Regrouped,

    /// <summary>A shut box was looked into.</summary>
    Peeked,

    /// <summary>An unpatched input was turned with the right button.</summary>
    Dialed,

    /// <summary>The view was framed round the patch.</summary>
    Framed,

    /// <summary>The picture and the sound were paused or played on.</summary>
    Paused,

    /// <summary>Rewind was pressed.</summary>
    Rewound,

    /// <summary>The speakers were muted or unmuted.</summary>
    Muted,

    /// <summary>Looping the patch's length was switched on.</summary>
    Looped,

    /// <summary>The stats line was put over a full-screen picture.</summary>
    Stats,

    /// <summary>A knob was added to the panel.</summary>
    Knob,

    /// <summary>A socket was linked to a knob.</summary>
    Linked,

    /// <summary>A knob learned a MIDI controller.</summary>
    Learned,

    /// <summary>The text was applied to the patch.</summary>
    Applied,

    /// <summary>A patch the text owned was handed back to the canvas.</summary>
    HandedBack,

    /// <summary>The preset gallery was opened.</summary>
    Gallery,

    /// <summary>A patch was kept as one of the person's own presets.</summary>
    KeptPreset,

    /// <summary>A preset from the preset site was opened.</summary>
    Shared,

    /// <summary>A group was kept in the module list.</summary>
    KeptGroup,

    /// <summary>A kept group was added from the module list.</summary>
    FromLibrary,

    /// <summary>The plugins window was opened.</summary>
    Plugins,

    /// <summary>A plugin was installed or updated.</summary>
    Installed,

    /// <summary>The letter to the author was opened.</summary>
    Letter,

    /// <summary>The knob panel was randomized.</summary>
    Randomized,
}
