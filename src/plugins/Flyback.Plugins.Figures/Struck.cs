using Flyback.Core.Compile;

namespace Flyback.Plugins.Figures;

/// <summary>How long ago something was struck, and how hard.</summary>
/// <param name="Age">Seconds since the strike, nought on the evaluation it lands.</param>
/// <param name="Level">The velocity latched at the strike, and nought before there was one.</param>
internal readonly record struct Struck(Slot Age, Slot Level);
