using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>A patch and the files it names, wherever they were kept.</summary>
public readonly record struct Opened(
    Patch Patch,
    ISampleLibrary Samples,
    IImageLibrary Pictures);