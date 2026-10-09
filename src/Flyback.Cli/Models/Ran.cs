namespace Flyback.Cli.Models;

/// <summary>What ffmpeg said and how it ended.</summary>
internal sealed record Ran(int Exit, byte[] Output, string Error, bool TimedOut = false)
{
    public bool Ok => Exit == 0 && !TimedOut;
}
