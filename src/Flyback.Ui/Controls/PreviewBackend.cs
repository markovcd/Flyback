namespace Flyback.Ui.Controls;

/// <summary>Which renderer is drawing the preview.</summary>
public enum PreviewBackend
{
    /// <summary>The interpreter, over rows, on the processor.</summary>
    Cpu,

    /// <summary>The patch compiled to a fragment shader.</summary>
    Gpu,
}