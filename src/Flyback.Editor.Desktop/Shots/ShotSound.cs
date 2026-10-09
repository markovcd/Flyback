using Flyback.Engine.Compile;
using Flyback.Ui.Audio;
using Flyback.Engine.Render;

namespace Flyback.Editor.Desktop.Shots;

/// <summary>
/// The sound of a shot: compiled, so the window says what it costs, and never played. Its
/// clock is wherever the shot puts it, and the picture follows it as it follows a speaker.
/// </summary>
/// <remarks>Nothing is heard, so a Scope or an Analyzer has nothing to chart.</remarks>
internal sealed class ShotSound : UnplayedSound
{
    private double time;

    public override double Time => time;

    public override float Aspect { get; set; } = 1f;

    public override int Oversample { get; set; } = AudioRenderer.DefaultOversample;

    public override double Speed => 0;

    public override float Gain { get; set; } = 1f;

    public override void SeekTo(double seconds) => time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;

    public override void Listen(CompiledPatch drawn, LiveValues watching)
    {
    }
}
