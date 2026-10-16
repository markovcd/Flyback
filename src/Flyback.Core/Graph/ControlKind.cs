namespace Flyback.Core.Graph;

/// <summary>What a panel knob is turned with: a knob, a switch or a button.</summary>
public enum ControlKind
{
    /// <summary>Anywhere from 0 to 1.</summary>
    Knob,

    /// <summary>Off at 0 or on at 1, and stays where it was left.</summary>
    Switch,

    /// <summary>On at 1 only while it is held down, and off at 0 otherwise.</summary>
    Button,
}
