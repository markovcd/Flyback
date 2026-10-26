namespace Flyback.Host;

/// <summary>
/// How the patch looks over a full-screen picture while it is played live: how much of
/// the picture shows through the modules and behind the text, and how long each is left
/// alone before it fades away.
/// </summary>
public sealed class LivePatchingLook
{
    public const int Clear = 0, Faintest = 10, Solid = 100;

    /// <summary>The longest wait before a fade, in seconds.</summary>
    public const double LongestWait = 600;

    /// <summary>How opaque the modules' fills, the boxes and the wires are, in percent.</summary>
    public int ModuleOpacity { get; set; } = 78;

    /// <summary>How opaque the band behind each line of text is, in percent; 0 is no band.</summary>
    public int TextOpacity { get; set; } = 72;

    /// <summary>Seconds the modules are left alone before they fade; 0 never fades them.</summary>
    public double ModuleFadeSeconds { get; set; } = 3;

    /// <summary>Seconds the text is left alone before it fades; 0 never fades it.</summary>
    public double TextFadeSeconds { get; set; } = 5;

    /// <summary>Whether the pointer brings faded text back, as well as the keys.</summary>
    public bool TextWakesOnPointer { get; set; } = true;

    /// <summary>Brings a hand-edited file's numbers into range.</summary>
    public void Clamp()
    {
        ModuleOpacity = Math.Clamp(ModuleOpacity, Faintest, Solid);
        TextOpacity = Math.Clamp(TextOpacity, Clear, Solid);
        ModuleFadeSeconds = Wait(ModuleFadeSeconds);
        TextFadeSeconds = Wait(TextFadeSeconds);
    }

    private static double Wait(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, LongestWait) : 0;
}
