namespace Flyback.Ui.Controls;

/// <summary>What a panel knob is played with on screen: a <see cref="Knob"/> or a <see cref="PanelSwitch"/>.</summary>
internal interface ITurnable
{
    /// <summary>Where it is, 0 to 1.</summary>
    double Value { get; set; }

    /// <summary>Whether it is tinted, for the knob whose sockets are being linked.</summary>
    bool Lit { get; set; }

    /// <summary>A hand moved it. Not raised when <see cref="Value"/> is set from code.</summary>
    event Action<double>? Turned;

    /// <summary>The hand came off it.</summary>
    event Action? Released;
}
