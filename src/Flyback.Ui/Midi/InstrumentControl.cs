namespace Flyback.Ui.Midi;

/// <param name="Name">The knob as the instrument names it.</param>
/// <param name="Controller">The control change it sends, 0 to 127.</param>
internal sealed record InstrumentControl(string Name, int Controller);
