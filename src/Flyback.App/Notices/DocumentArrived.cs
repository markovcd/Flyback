using Flyback.Core.Compile;

namespace Flyback.App.Notices;

/// <summary>A different document has arrived, with where its sounds and pictures are read from.</summary>
internal sealed record DocumentArrived(ISampleLibrary Sounds, IImageLibrary Pictures);
