using Flyback.Core.Compile;

namespace Flyback.Gpu;

/// <summary>
/// What a page says in place of a picture its shader cannot draw. A page draws on the
/// GPU alone: the processor that stands in on the desktop is too slow there.
/// </summary>
public static class UndrawnPicture
{
    /// <summary>Why <paramref name="picture"/> is left out in a browser, or null where the shader draws it.</summary>
    public static string? Why(CompiledPatch picture)
    {
        if (picture.ShaderCanDraw) return null;

        var charts = picture.Taps.Select(tap => tap.Spectrum ? "an Analyzer" : "a Scope").Distinct().ToList();
        var what = charts.Count == 0 ? "a Sample" : string.Join(" or ", charts);

        return $"Flyback in a browser cannot draw {what}, so this picture is left out and the sound plays alone. "
            + "Open the patch in Flyback to see it.";
    }
}
