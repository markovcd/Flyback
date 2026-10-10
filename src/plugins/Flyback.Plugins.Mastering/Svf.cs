using Flyback.Core.Compile;

namespace Flyback.Plugins.Mastering;

/// <summary>What three of a state-variable filter's outputs are, read at once.</summary>
internal readonly record struct Svf(Slot Low, Slot Band, Slot High);
