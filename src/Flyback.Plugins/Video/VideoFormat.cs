namespace Flyback.Plugins.Video;

/// <summary>What a <see cref="IVideoOutput"/> is opened for: the picture's size in pixels and how many frames a second it is drawn at.</summary>
public readonly record struct VideoFormat(int Width, int Height, double FramesPerSecond);
