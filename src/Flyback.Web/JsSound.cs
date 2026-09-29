using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Flyback.Core.Compile;
using Flyback.Core.Render;

namespace Flyback.Web;

/// <summary>
/// A sound's program as JavaScript, run by the page's engine on the program's own
/// memory: the arrays are pinned where they are and the script is told where.
/// </summary>
/// <remarks>
/// The script makes the evaluations and <see cref="AudioRenderer.Decimate"/> makes the
/// samples of them, so the filter, the clock and every Meter and Scope reading the
/// memory are the interpreter's own. See ADR-0160.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed partial class JsSound : IDisposable
{
    public const string Module = "program";

    private readonly AudioRenderer renderer;
    private readonly List<GCHandle> pins = [];
    private int id;

    private float[] evaluated = [];
    private GCHandle evaluatedPin;

    private JsSound(AudioRenderer renderer) => this.renderer = renderer;

    /// <summary>
    /// The program compiled to JavaScript for <paramref name="renderer"/> to play, or null
    /// with <paramref name="why"/> saying what stopped it, where the interpreter plays instead.
    /// </summary>
    public static JsSound? Create(
        CompiledPatch program,
        DelayState? memory,
        LiveValues live,
        AudioRenderer renderer,
        out string? why)
    {
        why = null;

        if (JsEmitter.Emit(program) is not { } source)
        {
            why = "The sound reads a picture, which only the interpreter does.";
            return null;
        }

        var sound = new JsSound(renderer);

        try
        {
            var layout = JsLayout.Of(program, memory, live, renderer.SampleRate, renderer.Oversample, sound.Pin);

            sound.id = JsCompile(source, layout);
            if (sound.id != 0) return sound;

            why = JsError();
        }
        catch (Exception ex)
        {
            why = ex.Message;
        }

        sound.Dispose();
        return null;
    }

    private long Pin(Array array)
    {
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        pins.Add(handle);

        return handle.AddrOfPinnedObject();
    }

    /// <summary>Fills an interleaved stereo buffer, as <c>AudioRenderer.Render</c> does.</summary>
    public void Render(Span<float> interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        var count = frames * renderer.Oversample * 2;

        if (evaluated.Length < count)
        {
            if (evaluatedPin.IsAllocated) evaluatedPin.Free();

            evaluated = new float[count];
            evaluatedPin = GCHandle.Alloc(evaluated, GCHandleType.Pinned);
        }

        JsRender(id, renderer.Time, frames, renderer.Aspect, (int)(evaluatedPin.AddrOfPinnedObject() / sizeof(float)));
        renderer.Decimate(evaluated.AsSpan(0, count), interleavedStereo);
    }

    public void Dispose()
    {
        if (id != 0) JsRelease(id);
        id = 0;

        foreach (var pin in pins) pin.Free();
        pins.Clear();

        if (evaluatedPin.IsAllocated) evaluatedPin.Free();
    }

    [JSImport("compile", Module)] private static partial int JsCompile(string source, string layout);
    [JSImport("error", Module)] private static partial string JsError();
    [JSImport("render", Module)] private static partial void JsRender(int id, double time, int frames, double aspect, int output);
    [JSImport("release", Module)] private static partial void JsRelease(int id);
}
