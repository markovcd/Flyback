namespace Flyback.Engine.Render;

/// <summary>
/// Where a Line In's sound comes from, asked for one frame at a time by the sound's
/// renderer.
/// </summary>
public interface ILineInSource
{
    /// <summary>The next frame, and silence where there is none to give. Never blocks.</summary>
    void Next(out float left, out float right);
}
