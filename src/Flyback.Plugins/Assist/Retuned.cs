namespace Flyback.Plugins.Assist;

/// <summary>One setting changed on the canvas, and whether it was carried onto the other patch.</summary>
/// <param name="Module">The module it is on, or null for one of the patch's own.</param>
/// <param name="Port">The input socket it sets, or -1 where it is not a knob.</param>
/// <param name="Field">What it is where it is not a knob: a module's name, off or state key, a panel knob, or a patch field.</param>
/// <param name="To">What it was set to, written short, or null where it is too long to say.</param>
/// <param name="Kept">False where the other patch had changed it too, and kept its own.</param>
internal sealed record Retuned(Guid? Module, int Port, string Field, string? To, bool Kept)
{
    public bool SameSetting(Retuned other) =>
        Module == other.Module && Port == other.Port && Field == other.Field;
}
