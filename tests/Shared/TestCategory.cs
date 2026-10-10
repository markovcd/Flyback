namespace Flyback.Tests;

/// <summary>What a test needs beyond the .NET runtime, carried as its <c>Category</c> trait.</summary>
/// <remarks>
/// The gate's image provides each one. Select or leave out a category with the runner's
/// <c>-trait "Category=ffmpeg"</c> and <c>-trait- "Category=browser"</c>, or a feature's <c>@ffmpeg</c> tag.
/// </remarks>
internal enum TestCategory
{
    /// <summary>The headless Avalonia thread: every <c>[AvaloniaFact]</c> and <c>[AvaloniaTheory]</c>, without being marked.</summary>
    Ui,

    /// <summary>The ffmpeg program, to encode or decode a clip.</summary>
    Ffmpeg,

    /// <summary>The Node program, to run what a page runs.</summary>
    Node,

    /// <summary>A Chromium to open the pages in.</summary>
    Browser,

    /// <summary>A JACK server to play into.</summary>
    Jack,

    /// <summary>An OpenGL context to draw the picture in.</summary>
    Gpu,
}
