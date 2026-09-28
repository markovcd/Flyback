namespace Flyback.Cli.Models;

/// <summary>What draws a render's picture.</summary>
internal enum PictureBackend
{
    /// <summary>The GPU where there is one, and the processor where there is not.</summary>
    Any,

    /// <summary>The GPU, or no render at all.</summary>
    Gpu,

    /// <summary>The processor, whose picture is the interpreter's to the bit (ADR-0035).</summary>
    Processor,
}
