using Flyback.Core.Compile;

namespace Flyback.Editor.Notices;

/// <summary>What the patch names is measured from somewhere else now, so it has to be read again.</summary>
internal sealed record FilesMoved(ISampleLibrary Sounds, IImageLibrary Pictures);
