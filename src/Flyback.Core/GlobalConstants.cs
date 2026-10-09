namespace Flyback.Core;

/// <summary>Constants every part of Flyback agrees on.</summary>
public static class GlobalConstants
{
    /// <summary>The program's name, which names its data folder.</summary>
    public const string ApplicationName = nameof(Flyback);
    /// <summary>The sound's sample rate, in hertz.</summary>
    internal const int SampleRate = 48_000;

    /// <summary>Where Flyback keeps its settings and plugins, under the user's application data.</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationName);
}