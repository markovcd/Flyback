namespace Flyback.App.Midi;

/// <summary>What a hardware controller does to a knob that sits somewhere else.</summary>
public enum Takeover
{
    /// <summary>The knob jumps to wherever the controller is.</summary>
    Jump,

    /// <summary>The controller is ignored until it passes where the knob is.</summary>
    PickUp,
}
