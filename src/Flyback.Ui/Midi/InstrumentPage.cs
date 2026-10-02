namespace Flyback.Ui.Midi;

/// <param name="Name">The page as the instrument names it: Filter, Amp, LFO 1.</param>
/// <param name="Kind">The kind of track this page belongs to, or null for the ordinary tracks.</param>
/// <param name="Controls">The knobs on it, in the instrument's order.</param>
internal sealed record InstrumentPage(string Name, string? Kind, IReadOnlyList<InstrumentControl> Controls);